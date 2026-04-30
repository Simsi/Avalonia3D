# Browser/WebGL backend audit — v55

Цель проверки: приблизить browser backend к desktop OpenGL high-scale path для сценария 10k обновляющихся стоек.

## Найденные расхождения с desktop path

1. WebGL JS runtime удалял `meshResources` по видимым batch-ам текущего кадра. Desktop удаляет GPU mesh resource по lifetime сцены/registry, а не по фрустуму. Из-за этого mesh мог быть удалён, пока C# `_meshGeometryVersions` всё ещё считал его загруженным. Симптом: объект ушёл за границу видимости, JS удалил mesh buffer, потом объект вернулся, но C# не переотправил geometry, и draw silently skipped.

2. WebGL retained renderer удалял retained instance batches сразу после выхода chunk-а из видимости. Desktop retained high-scale batches живут между кадрами и переиспользуются при возврате chunk-а. В браузере это приводило к постоянным transform/state uploads при вращении камеры и к низкой загрузке GPU.

3. WebGL high-scale state всегда использовал chunk fade alpha. При движении камеры alpha менялась почти каждый кадр, `fadeChanged` форсировал full state-buffer upload для всех видимых retained batches. Desktop по умолчанию держит `EnableHighScaleDynamicFadeState = false`, поэтому state не переписывается от движения камеры.

4. WebGL partial state upload отправлял много мелких interop вызовов без merge gap и без full-update threshold. Desktop path принимает решение full-vs-partial на уровне batch-а и объединяет соседние dirty ranges.

5. WebGL retained path не учитывал `MaxVisibleHighScaleChunks`, тогда как desktop path ограничивает количество видимых high-scale chunks при включённом лимите.

6. Canvas создавался с `antialias: true`, что для dense high-scale benchmark увеличивало raster/GPU cost без явной пользы для теста стоек.

## Исправления v55

- `WebGlScenePacket` теперь передаёт `liveMeshIds` и `liveTextureIds`, собранные по registry/lifetime сцены. JS sweep больше не удаляет mesh buffers только потому, что объект не виден в текущем кадре.
- Retained WebGL batches получили grace retention: невидимые batches хранятся 600 кадров и переиспользуются при возврате во фрустум.
- `ResolveChunkFadeAlpha` в WebGL теперь соответствует desktop default: если `EnableHighScaleDynamicFadeState == false`, state alpha всегда `1f`, камера не вызывает full state uploads.
- Dirty state uploads в WebGL теперь выбирают partial ranges только для sparse updates; если batch широко dirty, отправляется один full state upload. Partial ranges используют `HighScalePartialStateMergeGap`.
- WebGL retained path учитывает `MaxVisibleHighScaleChunks`.
- WebGL context создаётся с `antialias: false` и `powerPreference: 'high-performance'`.
- Исправлена коллизия `ThreeDEngine.Core.Math` vs `System.Math` в preview discovery/complexity report.

## Ожидаемый эффект

После warmup browser path больше не должен делать transform reupload при простом уходе/возврате chunk-а во фрустум. При telemetry/status updates должны идти только sparse `bufferSubData` ranges либо один full state-buffer upload на сильно dirty batch. Самый важный показатель в overlay: `InstanceBufferUploads` должен падать к нулю после прогрева, а `StateUploadBytes` не должен расти как полный объём всех видимых стоек на каждый кадр.

## Что ещё остаётся для +-5% к desktop

Текущий v55 остаётся C#/WASM-orchestrated renderer: каждый кадр всё ещё строится маленький JSON draw packet и выполняется C# frustum/chunk planning. Для стабильного паритета с desktop на слабых браузерных main-thread-ах следующий архитектурный шаг — JS-owned high-scale runtime: камера, visible chunk list, draw list и telemetry coalescing должны жить в JS, а C# должен передавать только структурные изменения и compact binary state deltas.
