# V37: baked composite mesh + GPU palette texture + render scale

Цель этой ветки — не менять LOD-политику и не ограничивать количество detailed instances, а уменьшить цену detailed rack-rendering без заметной потери качества.

## Baked detailed mesh

`HighScaleTemplateCompiler.Compile(..., bakeDetailedMesh: true)` теперь склеивает detailed-представление composite в один mesh. Вершины получают `materialSlot`, поэтому исходные части composite не теряют материал/цвет. Для rack-like объекта это должно уменьшить количество detailed draw calls примерно с `chunks * parts` до `chunks * 1`.

Состояние logical instance по-прежнему обновляется через state buffer. Цвет берётся в shader из palette texture по двум индексам: `instance materialVariantId` и `vertex materialSlot`. Поэтому variant может менять весь объект или конкретные слоты/части через `HighScaleMaterialVariant3D.SetPartColor(slot, color)`.

Ограничение: независимое состояние отдельной внутренней части *внутри конкретного instance* пока не является отдельным state buffer channel. Его можно кодировать через material variants, но полноценный per-instance-per-slot state buffer — отдельная следующая работа.

## GPU palette texture

Для baked mesh renderer не грузит `uVariantColors[]` для каждой части. Он грузит маленькую RGBA texture `slot x variant`, а shader выбирает цвет по `aMaterialSlot` и `aInstanceState.x`. Это сохраняет визуальную раздельность частей внутри одного склеенного mesh.

## Render scale

`ScenePerformanceOptions.RenderScale` добавлен как экспериментальная fullscreen-опция. Значение ниже `1.0` рендерит 3D-слой во внутренний framebuffer меньшего размера и апскейлит его в control framebuffer. Например `0.85` снижает pixel workload примерно до 72% от native resolution.

Если OpenGL backend не отдаёт FBO/renderbuffer-функции, renderer автоматически падает обратно на native resolution.

## Benchmark UI

В `MainView.axaml.cs` добавлены:

- `Baked high-scale meshes`
- `GPU palette texture`
- `Render scale`

Для первого теста:

- 10k racks
- chunk size больше числа объектов, например 16384/32768 или твой текущий 1000000
- Baked high-scale meshes = on
- GPU palette texture = on
- Render scale = 1.00, затем 0.90 и 0.85 для fullscreen
- Adaptive = off
- Frame interpolation = off
