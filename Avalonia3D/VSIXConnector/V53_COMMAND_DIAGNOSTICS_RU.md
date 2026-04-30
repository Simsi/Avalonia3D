# VSIX v53: команда и диагностика

Цель v53 — не скрывать ошибку. После установки должно быть минимум два способа увидеть, что command table попала в Visual Studio:

1. `Tools > Open 3D Preview` и `Tools > Show 3DEngine Previewer Diagnostics`.
2. `Tools > Options > Environment > Keyboard`: команды `Tools.Open3DPreview` и `Tools.ThreeDEnginePreviewerDiagnostics`.

Добавлена команда диагностики:

- `Tools > Show 3DEngine Previewer Diagnostics`
- command window / keyboard name: `Tools.ThreeDEnginePreviewerDiagnostics`

Диагностика показывает GUID пакета, command set, command ids, активный документ, активный проект и найденный `PreviewerApp.csproj`.

Если в Keyboard Options команды есть, но в меню нет, значит VSCT command table установлена, но Visual Studio не показывает выбранный menu placement. В этом случае назначьте временный hotkey на `Tools.Open3DPreview` и запустите команду: если handler сработает, проблема только в placement меню.

Если команд нет даже в Keyboard Options, значит `.vsix` не содержит/не установил command table. Тогда нужен `ActivityLog.xml` после запуска `devenv /log`.
