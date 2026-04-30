# VSIX v54: исправление регистрации команд

Симптом: расширение устанавливалось и отображалось в Manage Extensions, но команды `Open 3D Preview` не было ни в меню `Tools`, ни в `Tools > Options > Environment > Keyboard`.

Исправления:

1. `source.extension.vsixmanifest` теперь использует `%CurrentProject%` для `PkgdefProjectOutputGroup`:

```xml
<Asset Type="Microsoft.VisualStudio.VsPackage"
       d:Source="Project"
       d:ProjectName="%CurrentProject%"
       Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
```

Старый вариант `|ThreeDEngine.PreviewerVsix;PkgdefProjectOutputGroup|` мог не указывать на текущий VSIX-проект как на источник `.pkgdef`.

2. В `.csproj` возвращены свойства классического VSIX/VSPackage-проекта:

```xml
<SchemaVersion>2.0</SchemaVersion>
<ProjectTypeGuids>{82b43b9b-a64c-4715-b499-d71e9ca2bd60};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
<UseCodebase>true</UseCodebase>
```

`UseCodebase=true` нужен, чтобы сгенерированный `.pkgdef` регистрировал путь к assembly пакета внутри установленного VSIX.

3. `.vsct` упрощён до базового, проверяемого размещения:

- `Tools > Open 3D Preview`
- `Tools > Show 3DEngine Previewer Diagnostics`

Убран дополнительный верхний пункт `3DEngine`, чтобы исключить ошибки placement-а и сначала стабилизировать command table.

4. Версия пакета поднята до `0.3.1`.

Проверка после установки:

1. Удалить старый `3DEngine Previewer Connector` из Visual Studio.
2. Закрыть Visual Studio.
3. Очистить `VSIXConnector\bin` и `VSIXConnector\obj`.
4. Собрать `VSIXConnector\ThreeDEngine.PreviewerVsix.csproj` в Visual Studio 2022 с workload `Visual Studio extension development`.
5. Установить `VSIXConnector\bin\Debug\ThreeDEngine.PreviewerVsix.vsix`.
6. Перезапустить Visual Studio.
7. Проверить:
   - `Tools > Open 3D Preview`
   - `Tools > Show 3DEngine Previewer Diagnostics`
   - `Tools > Options > Environment > Keyboard`, поиск `Tools.Open3DPreview`.

Если команды всё равно нет, открыть `.vsix` как zip и проверить, что внутри есть `.pkgdef`. Затем запустить `devenv /log` и искать в `ActivityLog.xml` строки `ThreeDEngine`, `PkgDef`, `Menus.ctmenu`, `Open3DPreview`.
