# Browser WebGL v62 — Transform Animation root-cause fix

## Симптом

При включённом Transform Animation overlay показывал:

- `ClientHS: on`
- `GPUAnim: on`
- `JS Frame: ~0.06 ms`
- `JSPatch T/S: 0/0`
- `TransformUpload: ~1.83 MB`
- `StateUpload: ~468 KB`
- `Packet: ~390 ms`

Это доказывает, что JS-side culling/draw уже быстрый. Узкое место оставалось в C# `Packet` phase: `WebGlClientHighScaleRenderer` каждый кадр заново строил и отправлял полный retained snapshot.

## Причина

`BuildAndUploadLayer(...)` вызывался повторно во время shader-owned animation. То есть формально `GPUAnim` был включён, но C# всё равно проходил через structural snapshot rebuild и полный upload всех transform/state buffers.

Дополнительно structural hash был основан на перечислении `Dictionary.Values` из chunk index. Такой порядок нельзя использовать как надёжную идентичность кадра. Даже редкая нестабильность тут означает полный upload всей high-scale сцены.

## Исправление

1. В `EnsureSnapshots(...)` добавлен жёсткий ранний guard:

   Если `EnableWebGlClientGpuTransformAnimation=true`, runtime уже создан, template/instance count/palette mode совпадают, то C# не выполняет:

   - chunk rebuild;
   - structural hash;
   - `BuildAndUploadLayer(...)`;
   - full transform/state upload.

   Dirty transform queue в этом режиме только дренируется, без upload-а.

2. `BuildStructuralVersion(...)` теперь использует детерминированный sorted hash по chunk keys, а не порядок `Dictionary.Values` и не `System.HashCode`.

3. Смысловой invariant v62:

   Когда overlay показывает `GPUAnim: on`, обычный кадр не имеет права увеличивать:

   - `TransformUpload`;
   - `InstanceBufferUploads`;
   - full `StateUpload`.

## Ожидаемый overlay после прогрева

```text
ClientHS: on
GPUAnim: on
TransformUpload: 0.00 MB
StateUpload: patch-size only
InstanceBufferUploads: 0
JSPatch T: 0 или почти 0
JS Frame: малый
Packet: не сотни ms
```
