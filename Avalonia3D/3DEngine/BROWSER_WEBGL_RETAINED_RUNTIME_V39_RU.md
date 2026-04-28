# WebGL retained runtime v39

Цель v39 — убрать главный browser bottleneck без добавления пользовательских функций: WebGL больше не должен каждый кадр получать огромный JSON с матрицами и цветами всех high-scale instances.

## Что изменено

1. Для high-scale layers добавлен retained WebGL path.

   Desktop OpenGL уже работал по модели `mesh once + transform buffer + state buffer`. WebGL до v39 каждый кадр строил `SceneRenderPacket`, где `instanceData` содержал 20 float на каждый visible part instance. Затем JS парсил JSON, создавал `Float32Array` и делал `gl.bufferData` на каждый batch. Это было главным отличием от desktop.

   В v39 WebGL path для high-scale делает:

   - mesh geometry upload один раз;
   - transform buffer upload только при изменении chunk/LOD/transform membership;
   - state buffer upload при telemetry/status update;
   - per-frame render packet содержит только ссылки на retained batches.

2. Baked mesh и GPU palette теперь поддержаны в WebGL path.

   Baked detailed mesh сохраняет `materialSlot` как vertex attribute. Для high-scale retained batches shader берёт `materialVariantId` из instance state и цвет из маленькой palette texture `slot × variant`.

3. Legacy JSON path оставлен как fallback.

   Обычные Object3D, ControlPlane3D и non-retained режим продолжают использовать старый packet path. High-scale retained включается через уже существующий `Scene.Performance.EnableRetainedInstanceBuffers`.

## Ограничения

- WebGL retained path требует `ANGLE_instanced_arrays`. Без этого high-scale retained batch не рисуется; для современных браузеров это обычно доступно.
- WebGL всё ещё использует string/base64 interop для transform/state uploads. Это значительно лучше, чем большой JSON каждый кадр, но ещё не равно настоящему binary interop.
- LOD в WebGL retained path выбран на уровне chunk, а не на уровне каждого instance. Это сделано специально ради стабильности.
- Per-instance/per-slot independent state пока не реализован. Сейчас состояние конкретного instance выбирает material variant; material slots внутри baked mesh окрашиваются через variant palette.

## Что смотреть в benchmark

Ожидаемые признаки, что retained path работает:

- `serialization_ms` должен резко упасть по сравнению со старой browser-версией;
- `packet_build_ms` должен снизиться, потому что high-scale instances не разворачиваются в JSON каждый кадр;
- `draw_calls` для baked 10k rack при крупном chunk size остаётся малым;
- при статичной камере transform upload должен быть около нуля после warmup;
- при telemetry должен меняться state upload, а не transform upload.

