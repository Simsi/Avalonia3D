# Browser / WebGL статус

Browser/WebGL backend является вторичным backend-ом. Его цель — работоспособное отображение сцен в браузере, но не равная desktop OpenGL highload-производительность.

## Исправление текущей ошибки

В предыдущей версии benchmark view мог падать в browser/WASM с ошибкой:

```text
System.Diagnostics.Process is not supported on this platform.
```

Причина: `MainView.axaml.cs` включал CSV logging по умолчанию и использовал `Process.GetCurrentProcess()` для имени CSV-файла. В browser/WASM `System.Diagnostics.Process` не поддерживается.

В этой ветке исправлено:

- CSV logging выключен по умолчанию на browser/WASM.
- `Process.GetCurrentProcess()` удалён из генерации имени CSV.
- `Open log folder` в browser/WASM не пытается вызвать `Process.Start`.
- Если пользователь вручную включит CSV logging в браузере, будет показано сообщение, что CSV недоступен на browser/WASM.

## Ограничения WebGL backend

WebGL path пока строит JSON/packet представление сцены через `WebGlScenePacketBuilder`. Это проще и переносимо, но не является retained GPU data-flow как desktop OpenGL path.

Поддерживается:

- обычные renderable objects;
- high-scale layers через template parts;
- baked detailed mesh косвенно поддерживается, потому что template detailed LOD может состоять из одного baked part;
- базовая telemetry/status окраска через CPU-resolved colors;
- control planes как textured quads.

Не является полноценным highload path:

- нет desktop-like retained transform/state buffers;
- нет GPU palette texture path как в OpenGL;
- JSON/JS interop overhead остаётся дорогим;
- 10k/50k browser targets требуют отдельной оптимизации: binary buffers, retained JS-side buffers, state sub-updates, меньше JSON traffic.

## Как проверять browser

1. CSV logging должен быть off.
2. Начинать с small/simple scene.
3. Затем проверять rack composite 1k/5k/10k.
4. Не сравнивать напрямую с desktop OpenGL.
5. Если нужен browser highload target, следующая ветка должна быть `WebGL retained buffers`, а не дальнейшая оптимизация desktop renderer.

## v39: retained high-scale runtime

В v39 браузерная часть переведена ближе к desktop data-flow. Старый WebGL path пересылал большой JSON с instanceData каждый кадр. Новый high-scale path хранит batch buffers в JS/WebGL и обновляет transform/state отдельно. Это не добавляет нового пользовательского функционала, но должно резко снизить packet build / serialization / JS parse overhead в браузере.

Подробности: `3DEngine/BROWSER_WEBGL_RETAINED_RUNTIME_V39_RU.md`.
