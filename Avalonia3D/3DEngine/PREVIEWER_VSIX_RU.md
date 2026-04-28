# Previewer / VSIX Connector — v40

## Назначение

PreviewerApp — отдельное desktop Avalonia-приложение для просмотра конкретного 3D-класса из основной сборки. VSIXConnector добавляет команду `Open 3D Preview` в Visual Studio и запускает PreviewerApp для класса под курсором.

Текущий previewer рассчитан на source-drop модель движка: папка `3DEngine` лежит внутри host-проекта Avalonia3D и компилируется в основную assembly. Поэтому PreviewerApp должен ссылаться на этот host-проект, чтобы типы `Scene3D`, `Object3D`, `CompositeObject3D`, `Preview3DAttribute` были type-identical.

## Поддерживаемые preview entry points

1. Конструктор composite-класса:

```csharp
public sealed class Rack3D : CompositeObject3D
{
    public Rack3D()
    {
        // build parts here
    }
}
```

2. Static method с атрибутом:

```csharp
[Preview3D("Normal")]
public static Object3D Preview()
{
    return new Rack3D();
}
```

3. Полная сцена:

```csharp
[Preview3D("Scene")]
public static Scene3D PreviewScene()
{
    var scene = new Scene3D();
    scene.Objects.Add(new Rack3D());
    return scene;
}
```

4. Несколько состояний:

```csharp
[Preview3D]
public static IEnumerable<PreviewScene3D> Previews()
{
    yield return PreviewScene3D.Object("Normal", new Rack3D());
    yield return PreviewScene3D.Object("Critical", Rack3D.CreateCritical());
}
```

## Ручной запуск PreviewerApp

Если основной проект называется `Avalonia3D.csproj` и лежит рядом с `PreviewerApp`, достаточно:

```powershell
dotnet build .\Avalonia3D.csproj -c Debug
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -c Debug -- --assembly .\bin\Debug\net8.0\Avalonia3D.dll --type MyNamespace.Rack3D
```

Если основной проект называется иначе, передавай host-проект явно:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -c Debug -p:ThreeDEngineHostProject=.\MyHostProject.csproj -- --assembly .\bin\Debug\net8.0\MyHostProject.dll --type MyNamespace.Rack3D
```

## Visual Studio connector

VSIX v40 делает следующее:

1. Берёт активный C# document.
2. Определяет класс под курсором, включая nested class CLR-имя `Outer+Inner`.
3. Определяет активный project и configuration.
4. Ищет `PreviewerApp\PreviewerApp.csproj` рядом с проектом или solution root.
5. Выполняет `dotnet build` host-проекта.
6. Выполняет `dotnet build` PreviewerApp с параметром `-p:ThreeDEngineHostProject=<host-csproj>`.
7. Запускает уже собранный `PreviewerApp.exe` или `PreviewerApp.dll`, а не `dotnet run`.

Это сделано, чтобы убрать часть проблем с импортом/запуском: previewer больше не зависит от фиксированного имени `Avalonia3D.csproj`, а ошибки сборки показываются с tail build output.

## Установка VSIX

```powershell
cd .\VSIXConnector
dotnet restore .\ThreeDEngine.PreviewerVsix.csproj
dotnet build .\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

После сборки пакет должен лежать в:

```text
VSIXConnector\bin\Debug\ThreeDEngine.PreviewerVsix.vsix
```

Если Visual Studio уже установила старую версию, удали старую extension через `Extensions > Manage Extensions`, перезапусти Visual Studio и установи новый `.vsix`.

## Важные ограничения

1. PreviewerApp — desktop-only. В browser/WASM он не используется.
2. Пока 3DEngine поставляется source-drop-ом внутри host assembly, hot-reload preview без перезапуска процесса ограничен type identity. Самый стабильный путь — закрыть старое окно preview и открыть команду заново после rebuild.
3. VSIX class detector не полноценный Roslyn semantic model. Он стал устойчивее к nested/generic/class/record-class формам, но inheritance через alias или сложные source generators лучше проверять через `[Preview3D]` methods.
4. Baked mesh и GPU palette texture поддерживаются previewer-ом как часть актуального runtime. Complexity report учитывает high-scale layers/templates, но не является точным GPU profiler-ом.

## Диагностика типовых ошибок

`PreviewerApp requires a host Avalonia3D project` — PreviewerApp не нашёл host project. Передай `-p:ThreeDEngineHostProject=<csproj>` или положи PreviewerApp рядом с главным csproj.

`Preview type was not found in the target assembly` — VSIX вычислил имя класса, но такого CLR type нет в сборке. Проверь namespace, nested class, active configuration и что проект действительно пересобран.

`No previewable 3D entry points were found` — тип найден, но не является `CompositeObject3D` с public parameterless constructor и не содержит `[Preview3D]` static methods.

`Project build failed` / `PreviewerApp build failed` — VSIX теперь показывает хвост stdout/stderr сборки. Исправлять надо обычные compile errors в host или previewer.
