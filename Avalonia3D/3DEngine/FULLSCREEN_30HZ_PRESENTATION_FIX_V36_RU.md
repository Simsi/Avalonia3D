# v36: fullscreen 60 -> 30 Hz presentation fix

Симптом: в окне сцена держит 60 FPS, после разворачивания на весь экран стабилизируется около 30 FPS. Это характерно для half-VSync cadence: кадр или запрос кадра чуть не попадает в 16.6 ms окно, и compositor/OpenGL presentation начинает выдавать каждый второй VSync.

Что изменено:

- Continuous rendering при locked 55-75 FPS больше не управляется 16 ms DispatcherTimer.
- Для 60 FPS используется render-after-frame pump: следующий OpenGL frame запрашивается сразу после завершения предыдущего `OnOpenGlRender`.
- DispatcherTimer оставлен для низких target FPS и как fallback, но не для 60 Hz continuous rendering.

Почему это важно:

DispatcherTimer не синхронизирован с OpenGL/VSync. В fullscreen даже небольшая задержка UI dispatcher может стабильно перевести поток кадров в 30 Hz. Render-after-frame pump выравнивает запрос следующего кадра относительно фактического presentation loop.

Если после этого fullscreen всё равно держит 30 FPS, значит один fullscreen кадр реально превышает 16.6 ms по GPU/driver path. Тогда следующий шаг — снижать per-frame GPU work: baked rack mesh, упрощённое освещение или render-scale/offscreen upscale.
