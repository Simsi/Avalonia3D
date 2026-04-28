# RENDER AUDIT V33 — 10k stable 60 FPS pass

Цель этой итерации — не повышать потолок FPS и не оптимизировать 50k/100k, а убрать периодические фризы в 10k rack-сцене при locked 60 FPS.

## Диагноз по benchmark

Новый benchmark показал, что v31/v32 уже сняли основной state-buffer провал, но остались редкие фризы: frame gap 100-300 ms, telemetry_apply_ms до 280-300 ms и большие state_upload_kb spikes. При этом backend/draw сам по себе часто оставался в пределах нескольких миллисекунд. Это указывает не на чистый GPU bottleneck, а на синхронные волны telemetry apply + state upload + render scheduling.

## Что изменено

1. Telemetry apply теперь жёстко ограничен frame-budget-ом.
   Автоматический budget больше не делает catch-up при просадке FPS. Для 10k/s он держится около 184-192 updates/frame, что достаточно для 60 FPS и не создаёт волн по 1000-2000 updates.

2. Telemetry budget по времени снижен до 0.75 ms.
   Queue drain проверяет deadline чаще, каждые 8 updates, чтобы не зависать в длинном apply-loop.

3. Dynamic fade state отключён по умолчанию.
   Раньше движение камеры меняло fadeVersion и могло форсировать full state refresh по всем visible batches. Для high-scale rack-сцен fade должен быть LOD/culling-фичей, а не причиной постоянного state-buffer rewrite. При необходимости можно включить Scene.Performance.EnableHighScaleDynamicFadeState.

4. Partial state upload больше не схлопывает все dirty offsets в один широкий диапазон.
   Предыдущая эвристика `first..last` могла грузить почти весь state buffer из-за нескольких разнесённых dirty offsets. Теперь грузятся coalesced contiguous ranges с маленьким merge gap.

5. HighScaleState больше не запускает SyncControlAdapters и UpdateNavigationTimerState.
   Это не structural/control change. Для telemetry state нужно только request render.

6. Overlay update interval увеличен до 1 секунды.
   Это снижает регулярные строковые аллокации и Avalonia TextBlock invalidation во время benchmark.

## Ожидаемый результат

Для 10k rack composite, locked 60 FPS, adaptive off, interpolation off, telemetry 10k/s:

- telemetry_apply_ms должен стать стабильно ниже 1 ms;
- state_upload_kb должен упасть без 128-156 KB регулярных spikes;
- schedule_delay_ms должен перестать ловить 100-300 ms пики;
- движение камеры не должно вызывать full state refresh только из-за fadeVersion.

Если фризы останутся, следующий кандидат — transform batch rebuild при LOD/chunk changes. Для чистой проверки надо выставить Detailed LOD так, чтобы все 10k были Detailed, и временно поставить FadeDistance = 0.
