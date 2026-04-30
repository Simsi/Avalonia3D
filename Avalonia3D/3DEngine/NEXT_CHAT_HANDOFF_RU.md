# Handoff для нового чата

Ты профессиональный C# / Avalonia / OpenGL performance engineer. Мы разрабатываем встраиваемый 3D-движок для Avalonia UI. Текущая ветка зафиксирована как промежуточно стабильная после highload-итераций.

## Текущий статус

- 10k rack composite в desktop OpenGL уже близко к стабильному locked 60 FPS при правильном chunk size.
- Baked high-scale detailed mesh включён по умолчанию.
- GPU palette texture включена по умолчанию для OpenGL baked path.
- Render scale удалён как неудачный эксперимент.
- Aggregate layer batch path выключен по умолчанию как неудачный эксперимент.
- Telemetry идёт через coalesced/frame-budgeted state path.
- State update не должен пачкать transform chunks.
- Browser/WebGL запускается как fallback, но highload WebGL ещё не retained/binary-buffer architecture.

## Что важно не сломать

- Не возвращать render scale в стабильную ветку.
- Не включать aggregate layer batches по умолчанию.
- Не превращать material/status update в chunk dirty / transform rebuild.
- Не гонять 10k telemetry через Object3D events/properties.
- Не включать adaptive performance во время честной диагностики.
- Не считать browser WebGL равным desktop OpenGL.

## Лучший известный 10k preset

- Rack composite.
- 10000 instances.
- Large chunk: больше числа объектов слоя или несколько больших chunks.
- Baked high-scale meshes: on.
- GPU palette texture: on.
- FPS lock: on, target 60.
- Continuous render: on.
- Adaptive: off.
- Frame interpolation: off.
- Transform animation: off.
- Overlay off для чистого теста.
- CSV logging только desktop.

## Следующие осмысленные ветки

### 1. WebGL retained buffers

Если нужен browser highload: отказаться от JSON per frame, сделать retained JS-side buffers, binary state/transform updates и partial buffer uploads.

### 2. Per-instance-per-slot state

Если внутри baked composite нужно независимо менять состояние конкретных внутренних элементов конкретного instance, нужен slot-state texture/buffer: `instanceId x materialSlot -> state/variant`.

### 3. 50k stable profile

Для 50k нужен отдельный preset: не один гигантский chunk и не десятки мелких. Нужен гибрид крупных stable chunks, prewarm, chunk-level LOD и строгий запрет на full transform rebuild при telemetry.

### 4. Previewer productization

Заменить VSIX regex на Roslyn semantic model и сделать persistent previewer process.

## Последняя известная browser ошибка

Ошибка:

```text
System.Diagnostics.Process is not supported on this platform.
```

Исправлена в этой ветке отключением CSV logging на browser/WASM и удалением `Process.GetCurrentProcess()` из CSV naming.
