# VSIXConnector — текущее состояние и план диагностики

## Симптом

Расширение `3DEngine Previewer Connector` устанавливается и видно в списке расширений Visual Studio. Но команды нет:

- нет в `Tools`;
- нет в `Extensions`;
- нет в контекстном меню редактора;
- нет в `Tools > Options > Environment > Keyboard`.

Это означает, что сам VSIX как extension устанавливается, но команда не регистрируется как команда Visual Studio. Возможные причины:

1. `Menus.ctmenu` не попадает в пакет или не мержится.
2. `[ProvideMenuResource("Menus.ctmenu", 1)]` не попадает в `.pkgdef`.
3. Package не грузится или не проходит registration.
4. GUID/ID в `.vsct` не совпадают с C# handler.
5. Asset `Microsoft.VisualStudio.VsPackage` в manifest указывает не на тот output.
6. Текущий проект всё ещё отличается от official command template.

## Что уже проверено

- Ручной zip-пакет `.vsix` был неправильным: VSIXInstaller выдавал `InvalidExtensionPackageException`.
- Сборка VSIX через Visual Studio стала возможной.
- Расширение устанавливается.
- Команды не появляются.

## Что сделать в следующем чате

### 1. Получить ActivityLog

```powershell
devenv /log
```

Открыть:

```text
%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml
```

Искать:

```text
ThreeDEngine
PreviewerVsix
Menus.ctmenu
ProvideMenuResource
Open3DPreview
ThreeDEnginePreviewerDiagnostics
```

### 2. Проверить содержимое установленного VSIX

Открыть `.vsix` как zip. Проверить наличие:

```text
extension.vsixmanifest
[Content_Types].xml
ThreeDEngine.PreviewerVsix.dll
*.pkgdef
```

Если есть `.pkgdef`, проверить, что там есть регистрация package и menu resource.

### 3. Проверить DLL resources

Нужно убедиться, что compiled command table присутствует в assembly как `Menus.ctmenu`.

### 4. Сравнить с чистым template

Создать в Visual Studio новый проект:

```text
Extensibility > VSIX Project
Add > New Item > Extensibility > Command
```

Собрать, установить, убедиться, что команда появляется. Затем перенести из текущего connector-а только логику:

- найти активный документ;
- найти активный проект;
- определить тип под курсором;
- build host project;
- build/run PreviewerApp.

Не переносить старый `.csproj`, `.vsct`, manifest вслепую.

## Предпочтительный итоговый дизайн

Минимально рабочий VSIX:

- классический VSIX Project из шаблона Visual Studio;
- одна команда `Tools > Open 3D Preview`;
- одна diagnostic command `Tools > Show 3DEngine Previewer Diagnostics`;
- command handler выводит MessageBox при старте, чтобы доказать загрузку;
- только после этого добавлять context menu editor placement.

