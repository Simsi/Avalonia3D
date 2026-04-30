# WebGL retained runtime — v41 hotfix

Исправлены два практических дефекта, найденные при проверке previewer/browser drop-а.

## PreviewerApp

`PreviewerApp/Program.cs` больше не передает `ShutdownMode.OnMainWindowClose` в `StartWithClassicDesktopLifetime`. На некоторых версиях Avalonia desktop package этот enum не виден previewer-проекту, из-за чего сборка падала с ошибкой `CS0103: ShutdownMode does not exist in the current context`.

Previewer использует дефолтное поведение classic desktop lifetime: закрытие главного окна завершает приложение.

## Browser / WebGL

Ошибка `Visual was invalidated during the render pass` возникала из-за того, что `WebGlScenePresenter` поднимал `FrameRendered` синхронно внутри `Control.Render`. Подписчики, например benchmark `MainView`, в этом callback могли обновлять telemetry, scene state или Avalonia text controls. Это приводило к invalidation visual tree во время активного render pass.

В v41 `FrameRendered` в WebGL presenter откладывается через `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)`, то есть пользовательские callbacks выполняются после выхода из render pass.

Дополнительно `WebGlScenePresenter.RequestRender()` теперь не вызывает `InvalidateVisual()` синхронно. Invalidation coalesce-ится и планируется через dispatcher на `DispatcherPriority.Render`. Это защищает от повторного invalidation из scene callbacks, pointer events и telemetry updates.

## Что проверить

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=.\Avalonia3D.csproj
```

В browser приложение должно стартовать без `System.Diagnostics.Process is not supported on this platform` и без `Visual was invalidated during the render pass`.
