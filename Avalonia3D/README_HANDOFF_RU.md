# 3DEngine / Avalonia3D — handoff snapshot

Дата фиксации: 2026-04-28.

Это текущий архив исходников после серии итераций по desktop high-scale runtime, WebGL retained runtime, PreviewerApp и VSIXConnector. Архив нужен для переноса разработки в новый чистый чат.

## Содержимое

- `3DEngine/` — исходники движка.
- `PreviewerApp/` — отдельное Avalonia-приложение превьюера.
- `VSIXConnector/` — Visual Studio extension connector. Сейчас это проблемная часть: расширение устанавливается, но команда в Visual Studio не появляется.
- `MainView.axaml`, `MainView.axaml.cs` — benchmark/replacement для проверки runtime.
- Документы в корне и в подпапках — актуальные заметки по архитектуре, browser/WebGL, previewer/VSIX и текущим ограничениям.

## Краткий статус

Desktop OpenGL runtime доведён до промежуточно стабильного состояния. Самый удачный high-scale режим для 10k racks — baked detailed mesh, GPU palette texture, крупный chunk size, FPS lock 60, adaptive off, frame interpolation off. Render scale удалён как неудачная опция. Aggregate layer batches отключены как неудачная ветка.

WebGL запускается после исправлений `System.Diagnostics.Process is not supported on this platform` и `Visual was invalidated during the render pass`, но производительность браузерной версии всё ещё недостаточна. Последняя попытка v43 перевела WebGL high-scale state updates ближе к retained model, но пользователь сообщил, что CPU в браузере всё ещё умирает, GPU загружается слабо. Нужна отдельная клиентская JS-side архитектура, а не мелкие правки C#/WASM packet path.

PreviewerApp собирается и запускается вручную через `dotnet run`. Type resolution был улучшен: previewer ищет тип по полному имени и по короткому имени. Ошибка `ShutdownMode` исправлена.

VSIXConnector остаётся нерешённым. Расширение удаётся собрать и установить, но команда в Visual Studio не появляется ни в меню, ни в Keyboard Options. Текущая ветка v53 содержит диагностические команды, но у пользователя они тоже не видны. Вероятнее всего command table не мержится или пакет не регистрирует menu resource корректно. Дальше надо диагностировать через `devenv /log` и/или пересоздать VSIXConnector с нуля из официального Visual Studio VSIX + Command template.

## Команды проверки

PreviewerApp:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj
```

Ручной запуск previewer-а:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -- `
  --assembly .\bin\Debug\net8.0\Avalonia3D.dll `
  --type Avalonia3D.BenchmarkRack3D
```

VSIX из Visual Studio:

1. Открыть `VSIXConnector\ThreeDEngine.PreviewerVsix.csproj`.
2. Собрать проект.
3. Установить полученный `.vsix`.
4. Перезапустить Visual Studio.
5. Проверить `Tools`, `Extensions`, `Tools > Options > Environment > Keyboard`.

Если команды нет, запускать:

```powershell
devenv /log
```

и смотреть:

```text
%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml
```

Искать строки:

```text
ThreeDEngine
Menus.ctmenu
Open3DPreview
ThreeDEnginePreviewerDiagnostics
ProvideMenuResource
```

## Что не надо повторять

- Не возвращать render scale как публичную опцию. Эксперимент признан неудачным.
- Не включать aggregate layer batches по умолчанию. На тесте пользователя v34 ухудшил FPS и GPU usage.
- Не пытаться чинить WebGL только уменьшением JSON. Браузеру нужен JS-owned retained runtime.
- Не продолжать бесконечно править текущий VSIX вслепую. Если ActivityLog не даёт очевидной причины, лучше создать чистый VSIX-проект по official template и перенести туда handler.

