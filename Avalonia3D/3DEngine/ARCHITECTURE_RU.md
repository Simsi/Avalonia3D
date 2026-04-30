# Архитектура движка

## Общая модель

Движок является прикладным 3D-layer для Avalonia UI, а не игровым движком общего назначения. Основной объектный API остаётся удобным для UI-приложений: `Scene3D`, `Object3D`, примитивы, `CompositeObject3D`, `Scene3DControl`.

Для малых сцен используется обычное дерево объектов. Для highload-сцен используется отдельный путь: `HighScaleInstanceLayer3D` + `CompositeTemplate3D` + chunk/LOD/render batching.

## Основные сущности

`Scene3D` хранит камеру, свет, объекты, high-scale layers, performance options и метрики.

`Object3D` — базовый логический объект с transform/material/visibility/interactions.

`CompositeObject3D` — составной объект, собираемый через `CompositeBuilder3D`. Для high-scale он компилируется в template.

`CompositeTemplate3D` — оптимизированное представление composite-объекта для instancing. В этой ветке detailed LOD может быть baked: несколько частей composite склеиваются в один mesh.

`HighScaleInstanceLayer3D` — слой большого количества однотипных экземпляров. Хранит transforms, material variants, flags, chunk index и state buffer.

`InstanceStateBuffer3D` — компактный state-path для telemetry/status. State changes не должны приводить к rebuild transform batches.

`TelemetryDiffQueue3D` — coalesced queue. Последнее обновление instance выигрывает, применение ограничено frame budget.

`Scene3DControl` — Avalonia control, выбирающий presenter backend.

`OpenGlSceneRenderer` — основной desktop highload backend.

`WebGlScenePresenter` / `mini3d.webgl.js` — browser fallback backend.

## Highload data flow

Правильный поток данных:

```text
external telemetry
  -> coalesced TelemetryDiffQueue3D
  -> frame-budgeted apply
  -> InstanceStateBuffer3D dirty ranges
  -> GPU state update
  -> render using persistent transforms + current state
```

Что не должно происходить при status update:

- rebuild chunk index;
- rebuild transform buffer;
- invalidation of scene registry;
- per-instance Object3D property/event storm;
- full layer rebuild из-за смены цвета.

## Baked detailed mesh

Для composite-объектов detailed LOD теперь может быть baked. Это означает, что части rack/control собираются в один mesh. Цель — уменьшить draw calls и driver overhead без потери возможности красить внутренние части.

Механизм:

- `HighScaleTemplateCompiler.Compile(..., bakeDetailedMesh: true)` строит baked detailed part.
- `Mesh3D` хранит `MaterialSlots` и `BaseColors` для вершин.
- Каждый исходный part composite получает material slot.
- OpenGL shader может выбирать цвет из palette по slot + material variant.

Ограничение: полноценный независимый state отдельной внутренней части конкретного instance пока не является отдельным GPU state channel. Сейчас это решается через material variants и material slots. Если нужен независимый per-instance-per-slot status, следующий шаг — отдельный slot-state texture/buffer.

## GPU palette texture

Для baked path цвет не должен передаваться как сложный per-part material state. Вместо этого используется palette texture: `materialSlot x materialVariant`.

Плюсы:

- меньше uniform pressure;
- проще shader path;
- status update остаётся compact state update;
- baked mesh не теряет внутренние material slots.

## Chunking

Практический результат тестов: для 10k racks слишком мелкие chunks вредили. Они создавали много batches/draw calls и driver overhead. Большие chunks резко помогли.

Текущий совет:

- для 10k racks использовать крупный chunk, фактически несколько больших batches;
- для 50k+ не ставить один гигантский chunk без проверки, потому что редкий rebuild большого batch может стать дорогим;
- aggregate layer batches оставить выключенными.

## FPS / presentation

Unlocked >60 FPS внутри Avalonia `OpenGlControlBase` не гарантирован. Presentation path зависит от Avalonia compositor, monitor refresh и VSync. Ветка v36 перевела locked 60 на frame pump, но fullscreen 30 FPS при тяжёлой сцене всё ещё возможен, если кадр не попадает в 16.6 ms budget или compositor переходит в half-rate cadence.
