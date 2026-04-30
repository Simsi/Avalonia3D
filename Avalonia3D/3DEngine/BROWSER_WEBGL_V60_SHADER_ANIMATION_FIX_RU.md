# Browser WebGL v60: transform animation hot path fix

## Проблема

В v59 transform animation всё ещё могла убивать браузер до ~2 FPS. Причина была не в C# packet build, а в новом JS animation path: он каждый кадр пересчитывал `animatedTransformData` для каждого visible retained batch и делал полный `gl.bufferSubData` всего transform-buffer-а.

Для 10k racks это означает большой CPU-loop в JS + большой upload в WebGL каждый кадр. При нескольких десятках batches это снова превращалось в full transform rewrite per frame.

## Исправление

v60 переводит synthetic transform animation в shader-owned режим:

- C#/WASM не генерирует synthetic transform diffs для browser high-scale runtime.
- `WebGlClientHighScaleRenderer` при `EnableWebGlClientGpuTransformAnimation` принудительно дренирует dirty transform queue без upload-а.
- JS больше не переписывает retained transform buffers каждый кадр.
- `renderHighScaleFrame` включает uniforms `uClientAnimationEnabled`, `uClientAnimationTime`, `uClientAnimationAmplitude`.
- Vertex shader применяет лёгкое смещение к world position на GPU.

## Ожидаемые признаки в overlay

- `ClientHS: on`
- `GPUAnim: on`
- `JSPatch T: 0`
- `JSAnim: 0 batches/0 KB`
- `TransformUpload: 0.00 MB` после прогрева

Если `JSAnim` или `TransformUpload` растут каждый кадр, значит сцена не попала в v60 shader-animation path или включён внешний transform telemetry path.
