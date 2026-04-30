# Browser/WebGL — что делать дальше

## Текущий статус

Браузерная версия запускается, но производительность неудовлетворительная. Пользователь сообщил, что GPU загружен только на 10–20%, CPU перегружен, сцена лагает.

Текущие исправления уже убрали очевидные crash-баги и часть retained buffer path, но браузерная архитектура всё ещё недостаточно client-side.

## Почему текущий WebGL медленный

Desktop OpenGL path после оптимизаций держит persistent buffers и обновляет state отдельно от transform. Browser path всё ещё частично зависит от C#/WASM orchestration, interop calls, packet building и передачи данных в JS. Даже retained path не стал полностью JS-owned scene graph.

## Правильное направление

Нужен `WebGlClientRuntime`, где JS владеет:

- geometry buffers;
- transform buffers;
- state buffers;
- material palette texture;
- high-scale batches;
- render loop;
- camera uniforms;
- telemetry state application.

.NET/WASM должен передавать только compact команды:

```text
createLayer(templateId, instanceCount)
setTransforms(layerId, binaryTransformBuffer)
updateStateRange(layerId, start, count, binaryStateBuffer)
setCamera(viewProjection)
setPalette(templateId, palette)
```

Не надо каждый кадр пересобирать JSON/packet с данными всех visible instances.

## Минимальный следующий шаг

1. Оставить C# scene API как public facade.
2. При создании high-scale layer один раз отправлять template + transforms в JS.
3. Telemetry updates coalesce на C# стороне, но в JS передавать только compact ranges.
4. JS сам решает draw на кадр.
5. C# не участвует в per-frame draw packet, если сцена не изменилась структурно.

## Цель

Для browser high-scale после warmup:

- `packet_build_ms` близко к нулю;
- `serialization_ms` близко к нулю;
- telemetry вызывает только small state buffer updates;
- GPU загружается существенно выше 10–20%;
- CPU не умирает на packet/interp/JSON path.

