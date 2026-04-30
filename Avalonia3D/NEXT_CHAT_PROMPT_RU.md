# Prompt для нового чистого чата

Ты профессиональный C# Avalonia/OpenGL/WebGL/Visual Studio extensibility инженер. Мы разрабатываем встраиваемый 3D-движок для Avalonia UI: `3DEngine`, `PreviewerApp`, `VSIXConnector`.

Текущая кодовая база приложена архивом. Важно: не продолжай слепо старые patch-итерации. Нужно сначала подтвердить текущее состояние.

## Статус runtime

Desktop OpenGL high-scale runtime промежуточно стабилен. Для 10k racks лучше всего работает режим: baked detailed mesh, GPU palette texture, крупный chunk size, FPS lock 60, adaptive off, frame interpolation off. Render scale удалён. Aggregate layer batches отключены, потому что ухудшили FPS. Baked mesh и GPU palette нужно сохранить.

## Статус browser

WebGL запускается, но производительность плохая: CPU перегружен, GPU 10–20%. Текущий retained path недостаточен. Нужна клиентская JS-side retained architecture: JS владеет geometry/transform/state buffers и render loop; .NET передаёт только compact structural commands и telemetry state ranges.

## Статус previewer

`PreviewerApp` собирается и запускается вручную через `dotnet run`. Исправлены `ShutdownMode` и type resolution. Ручной preview работает лучше, чем VSIX.

## Статус VSIX

Самая проблемная часть. Расширение устанавливается и видно в Manage Extensions, но команда не появляется ни в меню, ни в Keyboard Options. Не надо дальше гадать. Нужно:

1. Запустить Visual Studio с `devenv /log`.
2. Изучить `ActivityLog.xml`.
3. Проверить `.vsix` как zip: manifest, pkgdef, compiled command table.
4. Если причина не очевидна — создать новый VSIX Project из official Visual Studio template + Command item и перенести туда только handler logic.

Цель по VSIX: сначала получить минимальную работающую команду `Tools > Open 3D Preview`, которая просто показывает MessageBox. Потом подключать запуск PreviewerApp.

## Что нельзя ломать

- Не возвращать render scale.
- Не включать aggregate layer batches по умолчанию.
- Не делать visible detailed instance cap: пользователь отверг эту идею.
- Не ухудшать 10k desktop baseline.
- Не смешивать status overlay с base material model.

## Следующие продуктовые фичи после инфраструктуры

- `ObjectId/Tags/Metadata + SceneIndex3D`.
- `CameraFocusService3D` с focus bubble/transparency sphere.
- `InteractionProxyStore3D` и pointer events.
- `Material3D v2 + StatusMaterialPalette3D`.
- `StaticModel3D + glTF/GLB importer`.
- Previewer 2.0: material slots, baked mesh inspection, status/LOD preview.

