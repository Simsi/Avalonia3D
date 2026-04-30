# Журнал проделанной работы

## Runtime / performance

Главная исходная проблема: high-scale renderer был CPU/main-thread bound. Telemetry/status updates приводили к dirty chunks, пересборке instance buffers и полным upload-ам, из-за чего GPU простаивал.

Сделано по итерациям:

- Отделён high-scale state path от structural/chunk dirty path.
- Добавлен `SceneChangeKind.HighScaleState`, чтобы telemetry не инвалидировала scene registry.
- Desktop OpenGL high-scale path разделён на transform buffer и state buffer.
- Убраны строковые batch keys, заменены value-type keys.
- `TelemetryDiffQueue3D` переведён с dictionary-like подхода на dense coalesced arrays.
- Добавлен frame-budgeted telemetry apply без catch-up волн.
- Исправлен баг dirty-state logic, из-за которого каждый batch мог делать full state upload.
- Отключён dynamic fade state по умолчанию, чтобы движение камеры не переписывало state buffers.
- Aggregate layer batches пробовались и откатаны: на реальном тесте стали хуже.
- Fullscreen frame pump пробовался для ухода от 30 Hz half-vsync; частично полезно, но корневой fullscreen limit зависит от presentation/GPU cost.
- Добавлен baked detailed mesh для composite templates.
- Добавлена GPU palette texture для desktop path.
- Render scale добавлялся и удалён как неудачная опция.

Текущий практический вывод: для 10k racks лучше всего работает крупный chunk size, baked detailed mesh, GPU palette, adaptive off, frame interpolation off. Для 50k+ нужна отдельная high-scale visual LOD/baked pipeline, но не ограничение visible detailed instances, поскольку пользователь это отверг.

## Browser / WebGL

Исправлено:

- `System.Diagnostics.Process is not supported on this platform` — CSV/log folder logic не должен запускать Process в browser/WASM.
- `Visual was invalidated during the render pass` — WebGL presenter больше не вызывает invalidation/frame callbacks синхронно внутри render pass.
- Добавлен retained WebGL high-scale renderer: geometry/transform/state разделены, state update стал batch-local, добавлен `bufferSubData` range upload.

Оставшаяся проблема: браузерная версия всё ещё недостаточно производительна. Пользователь сообщил: приложение запускается, но лагает, GPU 10–20%, CPU перегружен. Вероятная причина: C#/WASM всё ещё слишком активно участвует в packet/state/build path. Следующий шаг — JS-owned high-scale scene graph и binary/compact telemetry packets из .NET в JS.

## PreviewerApp

Исправлено:

- Ошибка `ShutdownMode` при сборке.
- Жёсткая привязка `PreviewerApp.csproj` к `..\Avalonia3D.csproj` заменена на параметр `ThreeDEngineHostProject`.
- Type resolution стал терпимее: полный тип, короткое имя, подсказки похожих типов.
- Улучшены сообщения об ошибках загрузки типов.

Текущий статус: ручной запуск работает у пользователя.

## VSIXConnector

Итерации:

- Сначала SDK-style VSIX project.
- Попытка ручной упаковки `.vsix` через zip — неверно, VSIXInstaller считал пакет невалидным.
- Попытки подключить VSSDK packaging targets к SDK-style проекту — нестабильно: то `.vsix` не создавался, то resource manifest писал в `C:\resources.json`.
- Переход на classic `.csproj` приблизил проект к official VSIX template.
- Команды добавлялись в `.vsct`, версии 0.2.4–0.2.9 устанавливались.
- На текущем состоянии пользователь видит extension в Manage Extensions, но не видит команды в меню/Keyboard Options.

Текущий статус: нерешено. С высокой вероятностью command table/menu resource не мержится в VS. Следующий разработчик должен использовать `devenv /log` и сравнить проект с чистым `VSIX Project + Command` template.


## v91.2 build-fix

- Усилен MSBuild exclude для source-drop utility projects (`PreviewerApp`, `SmokeTests`, `VSIXConnector`).
- `Directory.Build.props` теперь задаёт `DefaultItemExcludes` и `DefaultItemExcludesInProjectFolder` с slash/backslash паттернами.
- `Directory.Build.targets` теперь удаляет utility compile items и top-level, и через target `BeforeTargets=CoreCompile`, включая absolute-path patterns.
- Цель — исправить CLI build blocker, где host `Avalonia3D.csproj` компилировал `SmokeTests/Program.cs`.


## v91.3 build-fix

- Усилен compile exclusion для source-drop подпроектов и generated `obj/bin` sources через фильтрацию `@(Compile)` по `FullPath` перед `CoreCompile/Csc`.
- Добавлен backward-compatible alias `SceneFrameRenderedEventArgs.Kind => Backend` для host/sample code.
- Добавлен `3DEngine/STABILIZATION_V91_3_BUILD_FIX_RU.md`.
