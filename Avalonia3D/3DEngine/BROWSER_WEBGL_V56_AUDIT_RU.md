# Browser WebGL backend audit v56

## Что реально тормозило

По телеметрии браузера:

- `Frame ~190-200 ms`
- `Packet ~190-198 ms`
- `Serialize ~1 ms`
- `Upload ~0.03 ms`
- `GPU почти простаивает`

Это означает, что основная проблема находится не в draw-call и не в GPU, а в CPU-side frame generation:

1. retained high-scale path слишком часто считался **structural dirty**;
2. из-за этого каждый кадр заново пересобирался полный transform-buffer на весь chunk;
3. затем он целиком сериализовался в base64 и переотправлялся в JS/WebGL;
4. браузер декодировал и заново делал `gl.bufferData(...)` на полный instance-buffer.

Для сцены `10k` инстансов даже при умеренном telemetry-rate это превращалось в full-buffer rebuild almost every frame.

## Корневые причины

### 1) Неправильное определение structural changes
В `WebGlRetainedHighScaleRenderer` structural dirty определялся через:

- `chunk.Version`
- `layer.Instances.Version`

Обе версии меняются не только при реальном изменении membership/order, но и при обычных transform/material/visibility update.

Итог: почти любой telemetry update выглядел как structural mutation.

### 2) Нет частичных transform updates
Для state buffer уже был partial path через `UploadRetainedBatchStateRange`, а для transform buffer — нет.

Итог: даже если изменилось несколько сотен инстансов из 10k, пересобирался и переливался весь transform-buffer.

### 3) Chunk dirty не сбрасывался после обработки
Desktop path делает `chunk.MarkClean()` после consume. В WebGL retained path этого не было.

Итог: chunk долго жил в состоянии «грязный», что ухудшало поведение retained path и мешало стабилизации pipeline.

## Что изменено

### C# / retained high-scale renderer
Файл: `3DEngine/WebGL/Rendering/WebGlRetainedHighScaleRenderer.cs`

Сделано:

- structural signature теперь зависит только от реальной структуры batch:
  - mesh identity,
  - part index,
  - resolved LOD,
  - instance count,
  - точного порядка `InstanceIndices`.
- transform/material/state update внутри того же chunk больше **не считается structural rebuild**.
- retained batch теперь хранит:
  - `TransformData`,
  - `TransformVersions`,
  - mapping `instanceIndex -> batch offset`,
  - separate dirty lists for transforms and states.
- добавлен scan visible batch по `record.TransformVersion` и частичный update только изменённых offsets.
- добавлен full-vs-partial heuristic:
  - sparse dirty → `bufferSubData` ranges,
  - broad dirty → one full transform rewrite.
- после обработки visible chunk вызывается `chunk.MarkClean()`.

### JS / WebGL module
Файл: `3DEngine/WebGL/mini3d.webgl.js`

Сделано:

- добавлен `uploadRetainedBatchTransformsRange(...)`;
- partial transform updates применяются через `gl.bufferSubData(...)`;
- full transform buffer теперь создаётся как `gl.DYNAMIC_DRAW`, а не `gl.STATIC_DRAW`, потому что буфер обновляемый.

### JSImport bridge
Файл: `3DEngine/WebGL/Interop/WebGlInterop.cs`

Сделано:

- добавлен bridge method `UploadRetainedBatchTransformsRange(...)`;
- обновлён embedded JS module base64, чтобы WASM-path реально подхватил новый JS backend.

## Ожидаемый эффект

Для сценария `10k racks` с telemetry-rate порядка `~500-1000 updates/frame` ожидаемое ускорение должно идти в основном за счёт:

- устранения ложных full structural rebuild,
- перехода с full transform upload на partial transform subrange upload,
- стабилизации retained batches.

Практически это должно перевести pipeline из режима:

- `full transform rebuild every frame`

в режим:

- `stable retained batches + sparse transform/state patching`.

## Что ещё можно сделать следующим шагом

Если после этого браузер всё ещё заметно отстаёт от desktop, следующий уровень оптимизации:

1. бинарный interop вместо base64 строк;
2. JS-owned retained runtime с patch streams без сборки JSON packet в C#;
3. optional aggregate layer batches для очень однородных сцен;
4. loose culling margin / conservative chunk bounds expansion для сверхподвижных инстансов.

Но прежде чем делать это, нужно снять новые метрики после текущего фикса.
