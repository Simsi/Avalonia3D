# Render audit v32: frame-pacing stabilization

Цель v32 — не повышать пик FPS, а убрать рывки на 10k и снизить CPU starvation на 50k.

## Что показал benchmark

- 10k detailed telemetry после v31 близко к 60 FPS, но остаются редкие frame gaps в сотни миллисекунд.
- 50k лучше, но остаётся CPU/presentation starvation: GPU падает примерно до 40–50%, потому что UI/render thread не успевает стабильно подавать кадры.
- При движении камеры появляются новые visible chunks и/или меняется LOD-состав chunk-а. Это вызывает transform-batch rebuild/upload и даёт рывок.
- State upload уже меньше, чем в v30, но partial updates могли давать много мелких glBufferSubData-вызовов.

## Основные изменения v32

1. Chunk-level LOD planning для крупных high-scale layers.
   Для layers от 8000 instances и chunks от 64 instances renderer выбирает LOD на уровне chunk-а, а не по каждому instance. Это снижает per-frame CPU scan и LOD thrash при движении камеры.

2. Budget на transform-batch rebuild/upload.
   За кадр по умолчанию перестраивается не больше 12 high-scale transform batches. Если камера резко открыла много новых chunks, renderer растягивает подготовку на несколько кадров вместо одного большого фриза. Уже готовые старые batches могут быть использованы как временно stale, новые ещё неготовые batches пропускаются на один-два кадра.

3. Coalesced state subdata.
   Если в batch грязных state offsets больше 4, renderer делает один contiguous BufferSubData range вместо множества мелких вызовов. Это намеренно немного увеличивает байты upload, но резко снижает количество GL calls.

4. Telemetry apply time budget.
   TelemetryDiffQueue3D получил DrainTo(layer, maxUpdates, maxMilliseconds). Benchmark использует Scene.Performance.TelemetryApplyBudgetMilliseconds = 2.25 ms. Это защищает UI/render thread от больших catch-up пачек.

5. Benchmark defaults.
   Frame interpolation и adaptive performance теперь выключены по умолчанию. Это диагностически честнее: adaptive не скрывает bottleneck, а текущая interpolation не является настоящим frame generation.

6. Palette upload reduced.
   Renderer больше не грузит 32 material-variant uniforms на каждый part draw, если template использует только 4 variants. Для benchmark rack это уменьшает uniform calls примерно в 8 раз.

## Ожидаемый эффект

- 10k full/detailed должен быть ближе к стабильным 60 FPS, без регулярных 300–500 ms gaps.
- При движении камеры возможна небольшая delayed-подгрузка новых chunks, но без большого freeze.
- 50k должен меньше проваливать GPU из-за CPU stalls. Цель для следующего теста — не пик 60, а устойчивые 35–45 FPS без зависаний.

## Что ещё остаётся

- Текущий Avalonia OpenGlControlBase всё ещё ограничивает displayed FPS примерно частотой compositor/monitor. Unlock выше 60 требует отдельный presenter/render surface.
- Полноценный frame generation нельзя честно реализовать поверх capped presenter-а. Нужен render loop, способный реально выводить дополнительные frames, плюс history/depth/motion/reprojection path.
- Для 50k detailed rack bottleneck может перейти в draw-call/geometry cost: сотни draw calls и миллионы triangles. Следующий крупный шаг — baked rack mesh для Detailed LOD или grouped draw organization по part across chunks.
