# Browser WebGL v59 — JS-owned render and animation hot-path fix

## Что исправлено

### 1. `texImage2D: ArrayBufferView not big enough`

Причина: JS-ветка `uploadRetainedBatchStateBytes(...)` вызывала palette `texImage2D(...)` даже когда C# передавал пустой `byte[]` как сигнал «палитру не обновлять». Для JS typed array пустой payload был truthy, поэтому WebGL получал `0` байт на texture upload.

Исправление:

- добавлен `hasNonEmptyPayload(...)`;
- palette texture обновляется только при реально непустом payload;
- добавлен `coerceRgbaPayload(...)`, который гарантирует размер `width * height * 4` перед любым `texImage2D`;
- undersized texture/palette payload больше не спамит WebGL errors, а заменяется безопасным fallback RGBA payload;
- добавлены счётчики `TexErr texture/palette`.

### 2. Transform Animation больше не идёт через C#/WASM transform telemetry

Причина падения FPS до ~2: синтетическая transform animation могла попадать в C# transform queue или в shader-side per-vertex trig. Для 10k racks per-vertex `sin/cos` в vertex shader слишком дорогой.

Исправление:

- benchmark synthetic transform animation в browser high-scale runtime теперь не вызывает `SetInstanceTransform(...)`;
- `TelemetryDiffQueue3D.DrainTo(...)` получил режим `applyTransforms: false`, чтобы старые queued transform diffs не пробивали fast path;
- browser GPU animation включается сразу в WASM/browser окружении, а не только после первого `FrameRendered`;
- shader-side animation отключена в high-scale client runtime;
- animation применяется на JS стороне один раз на инстанс, а не на вершину: JS обновляет retained transform buffers из `baseTransformData` через `bufferSubData` перед draw.

### 3. JS теперь владеет transform animation storage

Для каждого retained batch JS хранит:

- `baseTransformData`;
- `animatedTransformData`;
- `animationFrameId`;
- `animationActive`.

При обычных external transform patches обновляется base transform storage. При включённой animation JS сам формирует animated transforms и загружает их в GPU buffer без участия C# framegen.

### 4. Метрики

В overlay добавлено:

- `JSAnim: <batches>/<KB>`;
- `TexErr: <texture>/<palette>`.

Ожидаемое поведение для 10k racks + Transform Animation:

- `ClientHS: on`;
- `GPUAnim: on`;
- `JSPatch T` около 0 для synthetic animation;
- `JSAnim` показывает JS-side dynamic transform upload;
- `TexErr` не растёт каждый кадр;
- WebGL console больше не спамит `texImage2D: ArrayBufferView not big enough`.

## Архитектурное состояние после v59

Для чистых high-scale browser scenes рендер уже JS-owned:

- C# загружает mesh и initial high-scale snapshot;
- JS хранит retained batches;
- JS делает frustum culling chunks;
- JS выбирает LOD;
- JS строит draw list;
- JS применяет synthetic transform animation;
- JS вызывает WebGL draw.

C# остаётся change producer для structural/state updates и fallback renderer для смешанных сцен с обычными renderables/control planes.
