# Browser WebGL full audit — v61

## Фактическая проблема по overlay

Симптом при включённом Transform Animation:

- `ClientHS: on`
- `GPUAnim: on`
- `JS Frame ~0.06–0.10 ms`
- `JSPatch T/S: 0/0`
- `TransformUpload ~1.83 MB`
- `StateUpload ~468 KB`
- `Packet/Backend ~390 ms`

Это доказывает, что тормоз был не в JS draw/culling и не в shader animation. Старый C# upload path всё ещё пересоздавал retained buffers. JS рисовал быстро, но до него каждый кадр выполнялся full snapshot/full batch upload.

## Найденные причины

### 1. Structural version был слишком чувствительным
`WebGlClientHighScaleRenderer.BuildStructuralVersion(...)` зависел от volatile state:

- `layer.Chunks.Version`
- `layer.LodPolicy.Version`

Для GPU/shader-owned animation эти значения не должны инвалидировать retained transform/state buffers. LOD policy меняет только правила выбора LOD в JS, а dirty chunk/chunk version не означает изменение batch membership.

Итог: runtime считал слой structural-dirty и каждый кадр заново выполнял `UploadFullBatch(...)` для всех visible/runtime batches.

### 2. Chunk rebuild мог запускаться перед reuse-check
Даже если JS animation должна владеть движением, старые transform diffs или переходные состояния могли выставить `Chunks.RebuildRequested`. До v61 renderer сначала делал C# `Chunks.Rebuild(...)`, и только потом решал, можно ли переиспользовать JS runtime.

Для 10k это дорогая операция и может заново менять structural signature.

### 3. LOD policy change ошибочно форсировал full state update
В client runtime `lodPolicyChanged` участвовал в full-state decision. Для текущего default `EnableHighScaleDynamicFadeState=false` LOD policy не должен переписывать state buffer.

## Исправления v61

### Stable structural signature
Structural signature теперь строится из реальной batch topology:

- template id;
- instance count;
- template part count;
- palette texture mode;
- chunk cell size;
- chunk count;
- chunk key + instance count.

Не участвуют:

- dirty chunk versions;
- LOD policy version;
- state/material telemetry versions;
- transform versions.

### GPU animation reuse guard
Если `EnableWebGlClientGpuTransformAnimation=true`, а template/instance count/palette mode не изменились, существующий JS-owned runtime переиспользуется даже при несовпадении volatile structural hash. Это защищает benchmark от случайного возврата в full upload loop.

### Suppressed CPU chunk rebuild under GPU animation
Если `Chunks.RebuildRequested=true`, но активна shader-owned animation и runtime можно переиспользовать, C# chunk rebuild подавляется через `ClearRebuildRequested()`. JS использует уже загруженные conservative chunk bounds, а vertex shader делает только визуальное смещение.

### LOD no longer forces state-buffer rewrite
`lodPolicyChanged` больше не вызывает full state upload в browser client runtime. State buffer обновляется только при material/color/visibility changes или resolver changes.

## Ожидаемый профиль после v61

После warmup при `10k racks + Transform Animation`:

- `ClientHS: on`
- `GPUAnim: on`
- `TransformUpload: 0.00 MB` на обычных кадрах;
- `InstanceBufferUploads: 0` на обычных кадрах;
- `StateUpload`: только небольшие dirty ranges при telemetry;
- `JS Frame`: остаётся малым;
- `Packet/Backend`: не должен уходить в сотни ms.

Если `TransformUpload` снова растёт каждый кадр, значит где-то ещё идёт explicit external transform telemetry или слой реально пересоздаётся из-за смены template/instance count/palette mode.
