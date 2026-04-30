# 3DEngine Debugger v65 — tooling and experimental source stub

v65 переводит previewer в более полноценный debugger/workbench.

## Новые debug-инструменты

### Ghost unselected
Делает все объекты, кроме текущего выбранного объекта, полупрозрачными. Удобно для проверки внутренней геометрии, вложенных частей composite-объекта и визуального совпадения деталей.

### Hide unselected / Solo
Скрывает всё, кроме выбранного объекта. Кнопка `Solo` включает этот режим сразу. `Reset view modes` возвращает сохранённые visibility/material значения.

### Only workbench objects
Фильтрует левый список до объектов, созданных прямо в debugger-е. Это удобно, когда в сцене много исходных частей, но отлаживается временная заготовка.

### Pin selection while filtering
Сохраняет выбранный объект при перестройке списка, если он всё ещё доступен после фильтрации.

### Copy scene code
Копирует C#-код для всех workbench-created root-объектов. Если таких объектов нет, экспортирует выбранный объект, если его тип поддерживает construction export.

### Copy diagnostics
Копирует summary сцены, выбранный объект и текущий видимый список. Это полезно для bug reports и handoff между итерациями.

## Experimental source generation

Кнопка `Generate source stub (experimental)` создаёт файл:

```text
ThreeDEngineDebugger.Generated.cs
```

Файл пишется рядом с найденным source project root. Если debugger запущен из `bin/...`, root определяется подъёмом к папке перед `bin` или к ближайшему `.csproj`.

Важно: v65 намеренно не переписывает существующий исходный файл и не пытается автоматически вставлять код в пользовательский класс. Вместо этого создаётся companion file с `ThreeDEngineDebuggerGenerated.CreateScene()`. Это безопаснее: файл можно просмотреть, отредактировать и только потом переносить в production-код.

## Почему не прямое silent-edit исходника

VS/editor source mutation должна быть cooperative и явной. Для полноценной вставки в конкретный метод нужен следующий этап: Roslyn-backed patcher или VSIX-команда, которая покажет diff/preview и применит патч только после подтверждения.

## Следующий этап

- добавить source markers вроде `// <3DEngineDebuggerInsert>`;
- искать marker в исходном `.cs`;
- генерировать patch preview;
- применить через VSIX/editor API или записать `.patch` файл.
