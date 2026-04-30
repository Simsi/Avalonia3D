# V83 — post-stabilization audit fixes

Повторный аудит v82 нашёл несколько мест, где контракты уже были введены, но часть старых путей ещё оставалась слишком размытой.

## Исправлено

### HighScale structure changes

`HighScaleInstanceLayer3D` больше не поднимает `Unknown` через `base.RaiseChanged()` для структурных изменений. Теперь используется `SceneChangeKind.HighScaleStructure`. Это важно для renderer/cache logic: high-scale structural rebuild не должен маскироваться под generic full unknown change.

### Scene structure version

`Scene3D.StructureVersion` теперь увеличивается не только для `Structure`, но и для `HighScaleStructure`.

### Registry invalidation

`HighScaleStructure` добавлен в список изменений, которые инвалидируют scene registry.

### ControlPlane changes

`ControlPlane3D` теперь поднимает `SceneChangeKind.Control` для:

- `AlwaysFaceCamera`;
- dirty snapshot;
- обновления snapshot texture.

Это убирает попадание control-plane обновлений в generic unknown path.

### Rotation change duplicate event

`Object3D.RotationDegrees` больше не поднимает дополнительный `RaiseChanged(...)` после `Transform.SetEulerDegrees(...)`, потому что сам `Transform` уже вызывает `OnTransformChanged(...)`. В `OnTransformChanged(...)` добавлены уведомления для `RotationDegrees` и `Rotation`.

### Static validation

Проверены:

- баланс фигурных скобок в `.cs` файлах;
- XML-валидность `.vsct` и `.csproj` файлов;
- отсутствие `KeyBindings` внутри `<Commands>`;
- отсутствие старого debugger physics timer в `Scene3DPreviewControl`.

## Не сделано в этом проходе

Полную компиляцию здесь выполнить нельзя: в окружении отсутствует `dotnet`/MSBuild. Для локальной проверки:

```powershell
dotnet build .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=..\Avalonia3D.csproj
dotnet run --project .\SmokeTests\ThreeDEngine.SmokeTests.csproj -- .
```
