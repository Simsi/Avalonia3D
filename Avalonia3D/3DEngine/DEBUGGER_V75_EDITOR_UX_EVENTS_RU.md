# 3DEngine Debugger v75 — editor UX, parts, light gizmos and event drafts

## Основные изменения

- При открытии debugger-а composite-объекты разблокируются для редактирования по частям: родительский composite больше не перехватывает selection/manipulation всех children.
- Выбор объекта из viewport синхронизируется с левым списком/inspector-ом.
- Левая панель теперь прокручивается целиком.
- Правая и левая панели получили GridSplitter и кнопки сворачивания.
- Добавлена верхняя menu bar с быстрыми View/Tools действиями.
- Выбор preview-класса скрыт, если доступен только один preview.
- Create / debug больше не вызывает Frame/teleport камеры после создания объекта.
- Vector/RGBA rows переработаны на stretch layout, чтобы поля не вылезали за границы панели.
- Сцена и свет применяются live, без обязательной кнопки Apply.
- Добавлены light gizmos: источник/direction directional light и объект point light/range.
- Блок Physics исправлен: поля больше не сбрасываются перед применением.
- Добавлен блок Events для выбранного объекта: можно сохранить тело C# handler-а, скопировать его и экспортировать вместе с Build(...).

## Event drafts

Debugger не выполняет произвольный C#-код в текущем runtime-процессе. Текст события хранится как исходник и при экспериментальном Roslyn export добавляется в класс как private handler, а Build(...) подписывает объект:

```csharp
part1.Object.Clicked += OnPart1Clicked;

private void OnPart1Clicked(object? sender, ScenePointerEventArgs e)
{
    // body from debugger
}
```

Это безопаснее, чем компилировать произвольный код внутри previewer-а, и подходит для прототипирования поведения с последующей пересборкой проекта.
