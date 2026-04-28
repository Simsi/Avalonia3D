# VSIX command registration v52

В v52 VSIXConnector переведён с SDK-style `Microsoft.NET.Sdk` проекта на классический VSIX/VSPackage `.csproj`-проект.

Причина: SDK-style сборка устанавливала расширение, но command table не регистрировалась стабильно в Visual Studio. В результате расширение было видно в `Extensions > Manage Extensions`, но команда не появлялась ни в `Tools`, ни в списке клавиатурных команд.

Что изменено:

- `ThreeDEngine.PreviewerVsix.csproj` теперь классический MSBuild-проект с импортами `Microsoft.CSharp.targets` и `Microsoft.VsSDK.targets`.
- `.vsct` упрощён до одного placement: `Tools > Open 3D Preview`.
- Убран context-menu placement до стабилизации базовой команды.
- `LocCanonicalName` теперь `Tools.Open3DPreview`.
- `VSCTCompile` оставлен с `ResourceName=Menus.ctmenu`.
- `VsixPackage` содержит `ProvideMenuResource("Menus.ctmenu", 1)` и `ProvideAutoLoad(ShellInitialized)`.

Проверка после установки:

1. Удалить старую версию расширения.
2. Закрыть Visual Studio.
3. Удалить `VSIXConnector/bin` и `VSIXConnector/obj`.
4. Собрать `ThreeDEngine.PreviewerVsix`.
5. Установить `VSIXConnector/bin/Debug/ThreeDEngine.PreviewerVsix.vsix`.
6. Перезапустить Visual Studio.
7. Проверить меню `Tools > Open 3D Preview`.
8. Проверить `Tools > Options > Environment > Keyboard`, поиск `Open 3D Preview` или `Tools.Open3DPreview`.

Если команда не появилась, запускать:

```powershell
devenv /log
```

И смотреть `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml` по строкам `ThreeDEngine`, `Menus.ctmenu`, `Open3DPreview`.
