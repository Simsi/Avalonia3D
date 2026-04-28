# VSIX command registration v50

Исправление v50 убирает размещение команды в `IDM_VS_MENU_EXTENSIONS`. Этот символ не входит в стандартный набор IDs, доступный через `vsshlids.h` в текущей сборке VSSDK, поэтому VSCT-компилятор выдавал `VSCT1103`.

Команда теперь регистрируется только в двух местах:

- `Tools > Open 3D Preview` через `IDM_VS_MENU_TOOLS`;
- контекстное меню редактора кода через `IDM_VS_CTXT_CODEWIN`.

Это соответствует базовой схеме VSIX command: команда объявлена в `.vsct`, package содержит `ProvideMenuResource("Menus.ctmenu", 1)`, а handler регистрируется через `OleMenuCommandService`.

Проверка после установки:

1. Удалить старую версию расширения.
2. Очистить `bin` и `obj` у `VSIXConnector`.
3. Собрать `ThreeDEngine.PreviewerVsix` в Visual Studio.
4. Установить `.vsix` версии `0.2.7`.
5. Перезапустить Visual Studio.
6. Проверить `Tools > Open 3D Preview` и правый клик в C# editor.

Если команды нет, запускать Visual Studio через `devenv /log` и смотреть `ActivityLog.xml` по словам `ThreeDEngine`, `Menus.ctmenu`, `Open3DPreview`.
