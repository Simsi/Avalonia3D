# Previewer v63 — debug inspector

Цель v63: сделать превьюер удобнее для отладки составных 3D-объектов и переноса подобранных значений обратно в код.

## Что изменено

- Левый список объектов теперь строится обходом от корней сцены с явным `HashSet` по `Object3D.Id`, поэтому один и тот же объект не должен появляться в списке несколько раз.
- В список добавлен фильтр по имени, типу, path и id.
- Правая панель заменена с readonly text dump на редактируемый inspector.
- Можно менять:
  - `Name`, `IsVisible`, `IsPickable`, `IsManipulationEnabled`;
  - `Position`, `RotationDegrees`, `Scale`;
  - `Material.BaseColor`, `Opacity`, `Lighting`, `Surface`, `CullMode`;
  - для `HighScaleInstanceLayer3D`: LOD distances, draw/fade distance и billboard fallback.
- Есть `Auto apply valid values`: корректные изменения применяются сразу в preview scene.
- Кнопка `Apply` применяет все поля и обновляет список объектов, если менялось имя.
- Кнопка `Frame` наводит камеру на выбранный объект/часть по bounds.
- Кнопка `Copy code` копирует C# snippet с текущими значениями.

## Почему это исправляет дубли

Старый previewer брал `Scene.Registry.AllObjects` напрямую. Registry — это hot-path плоский cache для рендера/пикинга, а не UI tree. Для preview/debug UI лучше строить собственное дерево от `Scene.Objects` через `CompositeObject3D.Children` и дополнительно защищаться от повторов по стабильному `Object3D.Id`.

## Ограничения

- Inspector не пытается редактировать произвольные private/специфичные свойства всех наследников через reflection. Для отладки сейчас покрыт основной практический набор свойств, который влияет на визуальный результат.
- Snippet для nested part использует `root.FindPart("name")`; если часть вложена глубже или part names повторяются в разных композитах, snippet может потребовать ручной адаптации.
