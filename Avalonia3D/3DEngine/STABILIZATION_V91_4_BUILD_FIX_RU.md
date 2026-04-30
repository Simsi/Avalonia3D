# Avalonia3D / 3DEngine v91.4 build-fix audit

Дата: 2026-04-30
База сравнения: v80 stable (`3DEngine_v80_vsct_physics_debugger_fix_sources.zip`)
Текущий проблемный срез: v91.3

## Что найдено

1. В v90/v91 в source-drop добавлен подпроект `SmokeTests/`.
   Хост `Avalonia3D.csproj` компилирует вложенные `.cs` файлы через SDK glob, поэтому при сборке `PreviewerApp` через `ProjectReference` в хост попадает `SmokeTests/Program.cs`.
   Ошибка: `CS0246 PreviewerApp`.

2. Предыдущие фиксы через root-level `Directory.Build.props/targets` не сработали у пользователя, потому что root-файлы source-drop не оказались рядом с `Avalonia3D.csproj`.
   Это видно по `dir`: рядом с `Avalonia3D.csproj` отсутствуют `Directory.Build.props` и `Directory.Build.targets`.

3. Duplicate assembly attributes из `obj/Debug/net8.0/*.cs` были вторичным эффектом загрязнённого compile item list / stale obj.
   После повторного запуска остался только `SmokeTests/Program.cs`, то есть основной blocker — nested source-drop glob.

4. В host/sample code используется `SceneFrameRenderedEventArgs.Kind`, а новый контракт использует `Backend`.
   Для обратной совместимости оставлен alias `Kind => Backend`.

## Исправление v91.4

`PreviewerApp.csproj` теперь передаёт в referenced host project `DefaultItemExcludes` и `DefaultItemExcludesInProjectFolder` через `ProjectReference.AdditionalProperties`.

Это важно: фикс больше не зависит от того, были ли скопированы root-level `Directory.Build.*` файлы. Он находится в `PreviewerApp/PreviewerApp.csproj`, который пользователь уже обновляет при применении source-drop.

Исключаются из host build:

- `PreviewerApp/**`
- `SmokeTests/**`
- `VSIXConnector/**`
- `bin/**`
- `obj/**`

## Проверки в текущем окружении

`dotnet build` не запускался: в окружении нет `dotnet/MSBuild`.

Статически проверено:

- XML parse: `PreviewerApp.csproj`, `SmokeTests.csproj`, `Directory.Build.props`, `Directory.Build.targets`, `.vsct`;
- `Microsoft.CodeAnalysis.CSharp` NuGet package не добавлен;
- `SceneFrameRenderedEventArgs.Kind` присутствует как alias;
- `ForceWebGlJsOwnedHighScaleRuntime` default остаётся `false`;
- VSCT `KeyBindings` остаётся вне `<Commands>`, перед `<Symbols>`, hotkey `Alt+Q`;
- `RoslynDebuggerSourceExporter` не использует напрямую `PreviewOnly`/`Preview`/5-arg `Completed`, чтобы не ловить design-time mismatch.

## Если после v91.4 CLI build всё ещё цепляет SmokeTests

Тогда `Avalonia3D.csproj` содержит явный поздний `Compile Include="**/*.cs"` или кастомный target после SDK default items.
В этом случае нужно править сам `Avalonia3D.csproj`, добавив:

```xml
<ItemGroup>
  <Compile Remove="PreviewerApp\**\*.cs" />
  <Compile Remove="SmokeTests\**\*.cs" />
  <Compile Remove="VSIXConnector\**\*.cs" />
  <Compile Remove="bin\**\*.cs" />
  <Compile Remove="obj\**\*.cs" />
</ItemGroup>
```

или прислать текущий `Avalonia3D.csproj` для точного patch-а.
