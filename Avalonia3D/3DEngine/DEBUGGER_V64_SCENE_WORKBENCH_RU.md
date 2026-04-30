# 3DEngine Debugger v64 — Scene Workbench

v64 переводит Previewer в режим практического debugger/workbench.

## Что добавлено

- Левая панель теперь называется `3D Debugger`.
- Добавлен блок `Create / debug` для создания временных объектов прямо в сцене:
  - `Box`
  - `Sphere`
  - `Cylinder`
  - `Cone`
  - `Plane`
  - `Ellipse`
- Можно задать имя, размер/radius/height/depth и RGBA перед созданием.
- Созданные объекты помечаются как workbench-created и могут быть очищены кнопкой `Clear created`.
- Добавлены действия:
  - `Create`
  - `Duplicate`
  - `Delete / hide`
  - `Clear created`
- Дублирование создаёт standalone root-object даже если исходник был частью composite.
- Для source-built частей composite удаление невозможно без изменения исходника, поэтому `Delete / hide` скрывает такую часть.

## Инспектор

Инспектор v63 сохранён и расширен блоком `Primitive geometry`.

Теперь можно редактировать не только transform/material, но и геометрию базовых примитивов:

- `Box/Rectangle`: width, height, depth
- `Sphere`: radius, segments, rings
- `Cylinder`: radius, height, segments
- `Cone`: radius, height, segments
- `Plane`: width, height, segmentsX, segmentsY
- `Ellipse`: width, height, depth, segments

Изменения применяются сразу при включённом `Auto apply valid values`.

## C# snippet

`Copy code` теперь генерирует не только property-block, но и construction snippet для workbench-created объектов:

```csharp
var obj = new Box3D { Width = 1f, Height = 1f, Depth = 1f };
scene.Add(obj);
obj.Name = "Debug Box";
...
```

Для объектов, пришедших из исходного preview, остаётся property-snippet, который можно перенести в код соответствующей части.

## Почему не удаляем composite parts физически

Большинство частей composite пересоздаются методом `Build(...)` в исходном классе. Если удалить такую часть из runtime-списка, следующий rebuild вернёт её обратно. Поэтому debugger скрывает source-built part через `IsVisible = false`, а для постоянного результата нужно перенести snippet в source code.
