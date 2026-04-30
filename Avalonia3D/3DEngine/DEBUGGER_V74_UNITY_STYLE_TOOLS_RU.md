# 3D Debugger v74 — inspector polish and scene tools

Изменения v74:

- Генератор C# больше не пишет `global::` в экспортируемый `Build(...)` и snippets.
- Roslyn exporter добавляет нужные `using`-директивы в целевой файл: `System.Numerics`, `ThreeDEngine.Core.Scene`, `ThreeDEngine.Core.Primitives`, `ThreeDEngine.Core.Materials`, `ThreeDEngine.Core.Physics`.
- Выбор объекта в viewport синхронизируется с правым inspector-ом и левым списком.
- Левая и правая панели переведены на сворачиваемые секции `Expander` в более аккуратном Unity-like стиле.
- Добавлен переключатель `Show debug overlay` для скрытия performance overlay.
- Добавлены scene/debug toggles: bounds, colliders, picking ray, physics on/off.
- Добавлены настройки первого directional light и point light.
- В inspector добавлен блок Physics для выбранного объекта: Rigidbody, kinematic, gravity, mass, friction, restitution.
- Экспорт C# теперь сохраняет Rigidbody, если он включён у объекта.
- Уменьшены ширины/высоты полей, увеличена ширина inspector-панели, чтобы RGBA и vector rows не вылезали за границы.

Ограничения:

- Light panel управляет первым directional light и первым point light сцены. Если их нет, они создаются при Apply scene settings.
- Source export остаётся экспериментальным и создаёт backup перед записью.
