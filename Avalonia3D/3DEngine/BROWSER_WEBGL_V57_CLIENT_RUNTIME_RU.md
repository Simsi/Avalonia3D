# Browser WebGL backend v57 — client-owned high-scale runtime

## Цель

v57 переводит high-scale браузерный backend из режима `C#/WASM builds draw packet every frame` в режим `JS owns retained high-scale runtime`.

Legacy packet path оставлен как fallback для смешанных сцен: обычные `Object3D`, `ControlPlane3D` и старый retained path продолжают работать через `RenderScene(...)`.

## Реализовано

### 1. Binary/typed interop для hot-path buffers

Добавлены JS/C# entrypoints:

- `uploadRetainedBatchTransformsBytes`
- `uploadRetainedBatchStateBytes`
- `uploadRetainedBatchTransformsRangeBytes`
- `uploadRetainedBatchStateRangeBytes`

Внутри JS payload принимается как `Uint8Array`, `ArrayBuffer`, typed-array view или обычный JS number-array. Старый base64 path оставлен для fallback.

Ограничение: в .NET 8/JSImport `Span<T>`/`ArraySegment<T>` как импортируемый `MemoryView` не является устойчивым импортным контрактом. Поэтому v57 использует `byte[]` как практический binary path без base64; это всё ещё копия, но без строки и `atob`.

### 2. JS resource tables с numeric ids

JS теперь ведёт:

- `meshResourceList`
- `meshIdToIndex`
- `retainedBatchList`
- `retainedBatchIdToIndex`

Legacy path продолжает работать по string ids. Client runtime внутри draw loop использует numeric batch indices.

### 3. JS-owned high-scale layer snapshots

Добавлены entrypoints:

- `createHighScaleLayer`
- `uploadHighScaleLayerSnapshot`

Snapshot содержит:

- LOD policy;
- chunk bounds;
- chunk instance count;
- retained batch ids per LOD.

C# один раз загружает full retained buffers для всех chunk/LOD/part batches и отправляет snapshot. JS конвертирует string batch ids в numeric indices.

### 4. renderHighScaleFrame

Добавлен `renderHighScaleFrame(hostId, frameJson)`.

C# отправляет только frame uniforms:

- viewport;
- viewProjection;
- camera position;
- clear color;
- lighting uniforms.

JS выполняет:

- viewport setup;
- frustum culling chunks;
- chunk-level LOD;
- draw-list assembly;
- retained batch drawing.

### 5. JS-side frustum culling

Chunk culling перенесён в JS. Snapshot хранит center/extents. Culling выполняется через clip-space AABB corner test.

Bounds расширяются на conservative margin `max(0.5, chunkCellSize * 0.10)`, чтобы мелкая анимация внутри chunk не вызывала агрессивные исчезновения у края frustum.

### 6. Telemetry patch application

C# больше не строит full draw packet для telemetry. Он формирует binary ranges и вызывает JS range entrypoints. JS применяет их через `gl.bufferSubData(...)`.

В v57 есть высокоуровневый placeholder `applyHighScaleTelemetryPatch(...)`; текущий production path использует typed range calls, потому что они уже позволяют делать precise buffer patching без JSON patch stream.

### 7. WebGL2 optional path

`createHost()` сначала пытается получить `webgl2`, затем fallback на `webgl`.

Для WebGL2 используется native:

- `drawElementsInstanced`
- `vertexAttribDivisor`

Для WebGL1 остаётся `ANGLE_instanced_arrays`.

### 8. v57 metrics

Добавлены поля в `RenderStats` и overlay:

- `WebGlClientHighScaleRuntime`
- `WebGlVersion`
- `JsCullMilliseconds`
- `JsDrawMilliseconds`
- `JsFrameMilliseconds`
- `JsPatchMilliseconds`
- `JsDrawBatchCount`
- `JsTransformPatchRanges`
- `JsStatePatchRanges`
- `JsTransformPatchBytes`
- `JsStatePatchBytes`

## Fallback behavior

`WebGlScenePresenter` включает client runtime только если сцена является чистой retained high-scale сценой:

- есть visible `HighScaleInstanceLayer3D`;
- нет обычных `Scene.Registry.Renderables`;
- нет visible `ControlPlane3D` snapshots.

Иначе используется legacy `WebGlScenePacketBuilder + RenderScene` path.

## Что проверить в benchmark

Для `10k racks updating` после warmup ожидается:

- `ClientHS: on`;
- `Packet` резко ниже прежних ~190 ms;
- `JS Cull` и `JS Draw` появляются в overlay;
- `InstanceBufferUploads` падает после первого snapshot;
- `JSPatch T/S ranges` растёт при animation/telemetry;
- `TransformUpload` и `StateUpload` становятся patch-size, а не full-scene-size каждый кадр.

## Что осталось для v58

- настоящий single-call binary telemetry patch stream вместо нескольких range calls;
- VAO cache для WebGL2;
- mixed-scene JS client runtime для Object3D/control planes;
- optional aggregate mega-batches для очень однородных rack сцен;
- zero-copy MemoryView path, если проект перейдёт на .NET/JSImport версию с устойчивым import-side MemoryView contract.
