# Fallback без VSIX: запуск Previewer из Visual Studio External Tools

Если VSIX не собирается из-за отсутствия `Microsoft.VisualStudio.SDK` и `Microsoft.VSSDK.BuildTools`, можно временно подключить previewer как внешний инструмент Visual Studio. Это не требует VSSDK NuGet-пакетов.

## Настройка в Visual Studio

Открой `Tools > External Tools...` и добавь инструмент:

- Title: `Open 3D Preview`
- Command: `powershell.exe`
- Arguments:

```powershell
-NoProfile -ExecutionPolicy Bypass -File "$(SolutionDir)VSIXConnector\Previewer.ExternalTool.ps1" -ProjectPath "$(ProjectPath)" -ItemPath "$(ItemPath)" -Configuration "Debug"
```

- Initial directory:

```text
$(SolutionDir)
```

После этого команду можно запускать из меню `Tools > Open 3D Preview`, находясь в файле с `CompositeObject3D`, `PreviewScene3D` или `[Preview3D]`.

## Ограничения fallback-режима

- Нет context menu по правому клику в редакторе.
- Нет точного определения класса под курсором: скрипт берёт первый `class` / `record class` в текущем файле.
- Это временный обходной путь. Полноценный VSIX всё равно требует VSSDK packages.
