# Performance iterations 4-6: high-scale rendering foundation

Дата: 2026-04-25

Эти изменения продолжают предыдущий performance-pass и переводят renderer в сторону масштабной модели: authoring API остаётся объектным (`Object3D`, `CompositeObject3D`), но backend получает batches/instances.

## Итерация 4. OpenGL instancing-first path

Аудит показал, что главный потолок desktop-renderer — один draw call на объект/part. Даже после registry/cache сцена из тысяч одинаковых деталей продолжала оставаться draw-call bound.

Сделано:

- `OpenGlSceneRenderer` переведён на batch build перед draw phase.
- Batch key: shared `Mesh3D.ResourceKey` + lighting mode.
- Для batch хранится плотный instance buffer: `Matrix4x4 model + ColorRgba color`.
- Добавлен OpenGL instancing path через `glVertexAttribDivisor` / `glDrawElementsInstanced`, с `ARB` fallback lookup.
- Если instancing недоступен, renderer падает обратно на legacy-loop, но уже поверх batch data.
- `uViewProj` загружается один раз на mesh pass.
- Mesh GPU resources шарятся по `ResourceKey`, а не по object id.
- Resource sweep выполняется только при изменении registry version.
- Добавлен frustum culling по local bounds + model + viewProjection до добавления instance в batch.

Ограничение: прозрачный/transparent pass пока не отделён. Instancing рассчитан на opaque/simple material path.

## Итерация 5. WebGL batched/instanced packet path

Аудит WebGL показал, что старый packet model передавал массив `meshes`, где каждый object был отдельной draw item. Это создавало большой JSON и draw-loop на каждый объект.

Сделано:

- `WebGlScenePacketBuilder` теперь строит `batches`, а не `meshes`.
- Каждый batch содержит mesh id, lighting flag, instance count и плотный `instanceData` buffer.
- Regular objects и high-scale layers собираются в один batch pipeline.
- В JS backend добавлен `ANGLE_instanced_arrays` path.
- Если instancing extension недоступен, используется fallback per-instance loop.
- Embedded JS module обновлён из `mini3d.webgl.js`.
- `node --check` проходит для WebGL module.

Ограничение: transport всё ещё JSON-based. Для настоящих 100k+ WebGL instances следующий шаг — binary/typed-array retained buffer updates вместо JSON serialization.

## Итерация 6. HighScale layer integration

Предыдущий архив содержал заготовку template/instance runtime, но renderer её не потреблял как first-class path.

Сделано:

- Добавлен `HighScaleInstanceLayer3D`, который хранит `CompositeTemplate3D` + dense `InstanceStore3D`.
- Layer не рендерится как обычный mesh-object и не участвует в scene picking как обычный объект.
- OpenGL/WebGL renderers напрямую разворачивают layer в instanced batches.
- `HighScaleTemplateCompiler` сохраняет part mesh, local transform, material color и lighting mode.
- `CompositeTemplate3D` вычисляет aggregate local bounds для instance-level frustum culling.
- `HighScaleInstanceLayer3D` даёт методы `AddInstance`, `SetInstanceTransform`, `SetInstanceMaterialVariant`, чтобы изменения вызывали render invalidation.
- `RenderStats` расширен: `HighScaleInstanceCount`, `CulledObjectCount`.

## Новый масштабный workflow

Обычный API остаётся рабочим:

```csharp
scene.Add(new ServerRack3D());
```

Для тяжёлой сцены целевой путь теперь другой:

```csharp
var template = HighScaleTemplateCompiler.Compile(1, new ServerRack3D());
var layer = new HighScaleInstanceLayer3D(template, initialCapacity: racks.Count);

foreach (var rack in racks)
{
    layer.AddInstance(Matrix4x4.CreateTranslation(rack.Position), dataId: rack.Id);
}

scene.Add(layer);
```

Renderer видит не тысячи деревьев `CompositeObject3D`, а один layer, один template и плотный instance store.

## Что ещё не является завершённым production-level

- Нет бинарного WebGL transport; JSON всё ещё будет ограничивать очень крупные browser-сцены.
- Нет chunk/zone streaming.
- Нет spatial index для picking/physics high-scale instances.
- Нет LOD selection для `CompositeTemplate3D`.
- Нет transparent pass batching.
- Нет benchmark app внутри архива, потому что `3DEngine` должен оставаться clean source-drop без samples.

## Следующий обязательный этап

1. Локально прогнать `dotnet build`.
2. Добавить benchmark-сцену во внешний `MainView.axaml.cs` или отдельный проект, не внутрь `3DEngine`.
3. Сравнить:
   - 10k обычных `Box3D`;
   - 10k instances через `HighScaleInstanceLayer3D`;
   - 100k instances через `HighScaleInstanceLayer3D`;
   - WebGL packet build/serialization time.
4. После этого решать binary WebGL transport и spatial index.
