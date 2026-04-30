# Browser WebGL v58 — Transform Animation hot path

Цель v58: удержать браузерный backend в районе стабильных 30 FPS для `10k racks`, high-scale LOD около `300`, включённой telemetry и включённой `Transform animation`.

## Симптом

После v57 чистый browser retained/client runtime стал выдавать около 27 FPS, GPU начал использоваться близко к desktop. Но включение `Transform animation` роняло FPS примерно до 2.

Это означает, что bottleneck оставался не в GPU draw, а в CPU/update path вокруг transform telemetry.

## Найденные тонкие проблемы

### 1. C# каждый кадр сканировал все transforms

`WebGlClientHighScaleRenderer.BuildDirtyTransformIndices` сравнивал `TransformVersion` для всех `layer.Instances.Count` каждый кадр. Для 10k это уже дорого в WASM, а при анимации дополнительно запускался per-batch patch path.

Исправление: v58 использует `InstanceStore3D.DrainDirtyTransforms(...)`. Теперь C# обрабатывает только реально изменённые transforms, а не весь слой.

### 2. Начальная dirty-transform очередь не очищалась после upload snapshot

После создания слоя все инстансы оставались в dirty transform queue. Первый runtime frame после snapshot мог принять это как массовое transform update событие.

Исправление: после полной загрузки layer snapshot очередь dirty transforms очищается.

### 3. Transform внутри того же chunk ошибочно шёл как generic scene/object change

`HighScaleInstanceLayer3D.SetInstanceTransform` всегда вызывал structural/object changed path. Это инвалидировало registry и создавало лишние scene notifications. Для high-scale telemetry это неверно: transform внутри того же chunk — это runtime data patch, а не изменение структуры сцены.

Исправление: `HighScaleChunkIndex3D.UpdateInstance(...)` теперь возвращает, изменилась ли chunk membership. Если инстанс остался в том же chunk, вызывается `StateChanged`, а не generic structural/object change. Structural path остаётся только при переходе между chunks.

### 4. Synthetic benchmark animation гоняла transforms через CPU/WASM

Для benchmark-флага `Transform animation` browser client runtime теперь использует GPU-side procedural animation. C# больше не генерирует transform patches для этой синтетической анимации в WebGL high-scale режиме. Материальная telemetry остаётся прежней.

В JS vertex shader добавлены uniforms:

- `uClientAnimationEnabled`
- `uClientAnimationTime`
- `uClientAnimationAmplitude`

Позиция слегка смещается в shader по phase от world position. Это даёт видимое движение без C# transform queue, без interop и без `bufferSubData` storm.

### 5. Широкие dirty ranges теперь уходят full-buffer update-ом

Если dirty transform/state count слишком велик для batch, v58 отправляет один full dynamic buffer upload, а не большое количество мелких range calls.

## Изменённые файлы

- `MainView.axaml.cs`
- `3DEngine/Core/HighScale/HighScaleChunkIndex3D.cs`
- `3DEngine/Core/HighScale/HighScaleInstanceLayer3D.cs`
- `3DEngine/Core/Scene/ScenePerformanceOptions.cs`
- `3DEngine/Core/Rendering/RenderStats.cs`
- `3DEngine/Avalonia/Controls/Scene3DControl.cs`
- `3DEngine/WebGL/Rendering/WebGlClientHighScaleRenderer.cs`
- `3DEngine/WebGL/mini3d.webgl.js`
- `3DEngine/WebGL/Interop/WebGlInterop.cs`

## Ожидаемый профиль

Для WebGL client runtime:

- `ClientHS: on`
- `GPUAnim: on` при включённом Transform animation
- `JSPatch T` должен оставаться низким или нулевым для synthetic animation
- `TransformUpload` не должен расти как полный transform buffer каждый кадр
- `Packet` должен не взлетать до сотен миллисекунд

Если `GPUAnim: off`, значит сцена не подходит под browser GPU-animation fast path, например mixed scene с interactive proxies или не-WebGL backend. Тогда используется обычный transform telemetry path.
