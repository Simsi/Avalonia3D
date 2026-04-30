# Avalonia 3D Engine — промежуточно стабильный drop

Статус: промежуточная стабильная версия после highload-итераций v18-v37. Эта ветка фиксирует рабочее состояние движка и прекращает дальнейшее копание в микрооптимизациях до появления новых измерений.

Целевая область: встраиваемый 3D-слой для Avalonia UI, цифровые двойники, серверные стойки, инфраструктурные панели, кастомные CompositeObject3D, preview 3D-компонентов из Visual Studio.

Состав архива:

- `3DEngine/` — исходники движка, которые копируются внутрь основного Avalonia-проекта.
- `PreviewerApp/` — отдельное Avalonia-приложение previewer-а.
- `VSIXConnector/` — Visual Studio extension/connector для запуска previewer-а.
- `MainView.axaml`, `MainView.axaml.cs` — replacement-файлы benchmark/demo view.

Ключевое состояние ветки:

- Baked high-scale detailed meshes включены по умолчанию.
- GPU palette texture включена по умолчанию для OpenGL high-scale baked path.
- Render scale удалён. Эксперимент признан неудачным для этой ветки.
- Aggregate layer batches выключены по умолчанию. Эксперимент v34 ухудшил стабильность.
- Telemetry updates идут через coalesced/frame-budgeted state path.
- State updates не должны пачкать transform chunks.
- Для desktop OpenGL целевой режим: стабильные 10k rack composite при locked 60 FPS в окне; fullscreen зависит от GPU/presentation path.
- Browser/WebGL работает как fallback/вторичный backend, но не равен desktop OpenGL по highload-возможностям.

Рекомендуемый baseline для дальнейших тестов:

- Scenario: Rack composite.
- Instances: 10000.
- Chunk size: значение больше числа объектов в слое, например 16384, 32768 или большой тестовый chunk, который у тебя дал лучший результат.
- Baked high-scale meshes: on.
- GPU palette texture: on.
- Adaptive performance: off.
- Frame interpolation: off.
- Transform animation: off.
- FPS lock: on.
- Target FPS: 60.
- Continuous render: on.
- CSV logging: on только на desktop; в browser/WASM отключено.

Команды сборки desktop:

```powershell
dotnet clean .\Avalonia3D.sln
dotnet build .\Avalonia3D.sln
dotnet run --project .\Avalonia3D.Desktop\Avalonia3D.Desktop.csproj
```

Команды для VSIX отдельно:

```powershell
dotnet clean .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet restore .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj
dotnet build .\Avalonia3D\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

Важно по структуре: `VSIXConnector` и `PreviewerApp` не должны случайно компилироваться как часть основного Avalonia3D app. Для этого используется `VSIXConnector/EngineDrop.Exclusions.targets`.

## Обновление v40: Previewer / VSIX

В v40 previewer и Visual Studio connector приведены к актуальному состоянию движка:

- PreviewerApp больше не жёстко зависит от имени `Avalonia3D.csproj`; host-проект можно передать через `ThreeDEngineHostProject`.
- VSIX ищет PreviewerApp рядом с проектом и solution root, а не только в двух фиксированных путях.
- VSIX сначала строит host project, затем строит PreviewerApp, затем запускает уже собранный exe/dll.
- Ошибки сборки host/previewer показываются с хвостом stdout/stderr.
- Определение класса под курсором улучшено для nested classes и generic metadata names.
- Previewer documentation обновлена в `3DEngine/PREVIEWER_VSIX_RU.md`.

## v41 hotfix

- Исправлена сборка PreviewerApp на Avalonia-конфигурациях, где `ShutdownMode` не доступен из previewer project surface.
- Исправлен browser/WebGL crash `Visual was invalidated during the render pass`: WebGL `FrameRendered` и `InvalidateVisual` теперь отложены через dispatcher и не выполняются синхронно внутри Avalonia render pass.


## v43 hotfix summary

- PreviewerApp собирается; предупреждения `NU1900` при недоступном NuGet не являются ошибкой previewer-а.
- VSIX по-прежнему требует физически доступные пакеты `Microsoft.VisualStudio.SDK` и `Microsoft.VSSDK.BuildTools`. Если их нет в cache/offline feed, исходниками это не исправить; добавлен fallback через Visual Studio External Tools.
- WebGL high-scale runtime получил partial dirty state upload через `gl.bufferSubData` и больше не копит `StateBuffer.DirtyIndices` бесконечно.
