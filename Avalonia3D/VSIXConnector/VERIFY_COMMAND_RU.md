# Проверка команды VSIX

Если расширение установлено, но команда `Open 3D Preview` не видна, сначала удалите старую версию расширения из Visual Studio, закройте Visual Studio и очистите артефакты проекта:

```powershell
Remove-Item .\VSIXConnector\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\VSIXConnector\obj -Recurse -Force -ErrorAction SilentlyContinue
```

Соберите `ThreeDEngine.PreviewerVsix` в Visual Studio и установите новый `.vsix`. Версия исправленного пакета: `0.3.1`.

Главное исправление этой версии: VSCT-файл теперь компилируется с ресурсным именем `Menus.ctmenu`, которое совпадает с `[ProvideMenuResource("Menus.ctmenu", 1)]`. Без этого Visual Studio могла установить расширение, но не показывать команду в меню.

Проверка после установки:

1. Перезапустите Visual Studio.
2. Откройте C# файл host-проекта.
3. Поставьте курсор внутрь previewable класса.
4. Проверьте меню `Tools` и контекстное меню редактора.
5. Если команды всё ещё нет, запустите `devenv /log` и проверьте `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml` на ошибки загрузки пакета `ThreeDEngine.PreviewerVsix`.
