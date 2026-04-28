# RENDER AUDIT V34 — 10k smooth 60 FPS target

Цель этой итерации — не максимальный unlocked FPS, а стабильные 60 FPS для 10k rack composite в fullscreen.

## Вывод по свежему benchmark

В 10k rack CSV backend/draw чаще всего находится в районе 5–7 ms, но frame_total_ms периодически уходит к 50 ms, а schedule_delay_ms — к 45+ ms. Это означает, что основная просадка не в самом GPU draw, а в подаче кадров: UI/render thread получает регулярные блокировки и не успевает стабильно выдавать frame pacing.

Также в 10k detailed было около 36 visible chunks × 11 composite parts = ~396 instanced draw calls на кадр. Для fullscreen это уже ощутимо: много uniform updates, part-local uploads, palette uploads, attribute binds и driver calls. При GPU ~80% нельзя просто “перенести нагрузку на GPU”; нужно уменьшить CPU/driver overhead и стабилизировать state-update path.

## Главное изменение

Для medium high-scale layers включён aggregate layer batch path: если слой меньше или равен HighScaleAggregateLayerInstanceThreshold, renderer строит один retained batch на layer/LOD, а не отдельные batch-и на каждый chunk/LOD.

Для 10k racks это переводит типичный кадр с ~396 draw calls к ~11 draw calls для detailed rack template. Это также убирает camera-move chunk rebuild spikes для целевого 10k режима.

## Telemetry pacing

Benchmark synthetic telemetry теперь генерируется из frame callback, а не из отдельного DispatcherTimer. Это устраняет наложение независимой telemetry wave на render loop. Избыточные synthetic updates не догоняются пачкой; они отбрасываются/coalesce-ятся ради ровного frame pacing.

State changes в continuous rendering больше не вызывают RequestNextFrameRendering на каждый telemetry batch. Следующий scheduled frame сам подхватывает state buffer.

## Ожидаемый эффект

Для 10k racks, locked 60 FPS, telemetry 10k/s:

- draw_calls должны снизиться примерно с 350–400 до ~11–20 при all-detailed;
- transform_upload_mb после warmup должен быть 0 или близко к 0;
- state_upload_kb должен быть единицы KB на кадр, а не десятки KB;
- telemetry_apply_ms должен оставаться ниже 1 ms;
- schedule_delay_ms не должен показывать регулярные 40–50 ms spikes при неподвижной камере.

## Ограничение

Для 50k aggregate path не включается по умолчанию. Там всё ещё нужен отдельный этап: baked template mesh / part atlasing / multi-draw / native presenter. V34 сознательно оптимизирует цель 10k smooth 60 FPS.
