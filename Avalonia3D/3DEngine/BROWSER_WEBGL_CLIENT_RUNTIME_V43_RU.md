# WebGL client-side retained runtime v43

Цель v43 — убрать главный CPU/WASM bottleneck браузерной версии: полный rebuild/upload state buffer-а при каждом telemetry/status update.

## Что было плохо до v43

v39/v41 уже перенесли high-scale geometry/transform/state в retained WebGL buffers, но state update всё ещё был слишком грубым:

- `InstanceStateBuffer3D.DirtyIndices` не очищался в WebGL path;
- dirty set накапливался до размеров всего слоя;
- любое изменение `layer.StateBuffer.Version` заставляло каждый visible batch перезаливать полный state buffer;
- C# каждый кадр строил `float[]`, base64 и вызывал `gl.bufferData` для больших state chunks.

Итог: WebGL запускался, но CPU/WASM умирал, GPU был недогружен.

## Что изменено

1. `WebGlRetainedHighScaleRenderer` теперь очищает `layer.StateBuffer.ClearDirty()` после того, как все visible batches обработали dirty state.

2. State updates стали batch-local:
   - global dirty indices фильтруются по конкретному retained batch;
   - если dirty instance не входит в batch, batch не обновляется;
   - если входит, обновляется только соответствующий offset.

3. В JS добавлена функция:

```js
uploadRetainedBatchStateRange(hostId, batchId, startInstance, stateFloatsBase64)
```

Она вызывает `gl.bufferSubData`, а не `gl.bufferData`.

4. Full state upload теперь выполняется только при:
   - первом появлении batch;
   - structural change;
   - смене material resolver / palette;
   - смене LOD policy или fade alpha;
   - если batch был невидим и пропустил state version.

5. Embedded WebGL module в `WebGlInterop.cs` обновлён и содержит новый JS runtime.

## Что смотреть в браузерном benchmark

Для 10k baked racks ожидаемый профиль:

- после warmup transform upload должен быть близок к нулю;
- `state_upload_kb` должен быть малым и зависеть от количества реально изменённых dirty instances, а не от всех visible instances;
- `serialization_ms` должен быть небольшим, потому что high-scale batches идут как retained refs;
- GPU usage должен вырасти относительно v41/v42, потому что CPU меньше держит кадр.

## Ограничения

Это всё ещё не полностью JS-owned scene graph. Scene/camera/telemetry остаются в .NET/WASM, но high-scale GPU data теперь живёт на JS/WebGL стороне и получает только dirty ranges.

Следующий крупный шаг, если потребуется: JS-side camera/frame loop и JS-side telemetry consumer, где .NET передаёт только compressed external telemetry packets, а не участвует в каждом high-scale update.
