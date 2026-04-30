# VSIX command registration v50

Причина правки: VSIX устанавливался, но команда не появлялась в Visual Studio.

Стандартная схема Visual Studio SDK:

1. Команда объявляется в `.vsct`.
2. `AsyncPackage` помечается `[ProvideMenuResource("Menus.ctmenu", 1)]`.
3. В `InitializeAsync` команда регистрируется через `OleMenuCommandService`.
4. GUID/ID в `.vsct` и C# `CommandID` должны совпадать.

В v50 сделано:

- Оставлен один command id: `cmdidOpen3DPreview = 0x0100`.
- Один и тот же command размещается через `CommandPlacements` в `Tools`, `Extensions` и context menu C# editor.
- Добавлен `LocCanonicalName`, чтобы команда была видна в настройках клавиатуры.
- Добавлен `BeforeQueryStatus`, который явно делает команду `Visible/Enabled`.
- Добавлен `ProvideAutoLoad(...ShellInitialized...)`, чтобы пакет загружался после старта shell и регистрировал handler заранее.

Проверка:

```powershell
Remove-Item .\VSIXConnector\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\VSIXConnector\obj -Recurse -Force -ErrorAction SilentlyContinue
```

Собрать `ThreeDEngine.PreviewerVsix`, установить `.vsix` версии `0.2.7`, перезапустить Visual Studio.

Проверить:

- меню `Tools`;
- меню `Extensions`;
- контекстное меню C# editor;
- `Tools > Options > Environment > Keyboard`, поиск `Open3DPreview` или `Tools.Open3DPreview`.

Если команды нет, запустить:

```powershell
devenv /log
```

Лог:

```text
%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml
```

Искать строки `ThreeDEngine.PreviewerVsix`, `Menus.ctmenu`, `Open3DPreview`.
