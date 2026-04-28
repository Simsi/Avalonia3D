# Сборка VSIX без стабильного доступа к NuGet

`ThreeDEngine.PreviewerVsix.csproj` использует пакеты Visual Studio SDK:

- `Microsoft.VisualStudio.SDK`
- `Microsoft.VSSDK.BuildTools`

Если машина не может открыть `https://api.nuget.org/v3/index.json`, restore падает до компиляции. Это не ошибка кода connector-а.

В v42 проект настроен так, чтобы не считать недоступный NuGet fatal-ошибкой, если нужные пакеты уже есть в глобальном NuGet cache или в Visual Studio offline packages feed.

## Проверка 1: обычная сборка

```powershell
dotnet build .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug
```

## Проверка 2: явно игнорировать недоступные package sources

```powershell
dotnet restore .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj --ignore-failed-sources
dotnet build .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug --no-restore
```

## Проверка 3: добавить Visual Studio offline packages feed

Обычно Visual Studio offline feed лежит здесь:

```text
C:\Program Files (x86)\Microsoft SDKs\NuGetPackages
```

Команда:

```powershell
dotnet restore .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj `
  --ignore-failed-sources `
  -s "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages" `
  -s "$env:USERPROFILE\.nuget\packages"

dotnet build .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj -c Debug --no-restore
```

## Если пакетов нет ни в cache, ни в offline feed

Нужен один из вариантов:

1. временно дать доступ к NuGet и выполнить restore один раз;
2. установить workload Visual Studio `Visual Studio extension development`;
3. скачать/положить `.nupkg` пакеты `Microsoft.VisualStudio.SDK` и `Microsoft.VSSDK.BuildTools` в локальную папку и передать её как source:

```powershell
dotnet restore .\VSIXConnector\ThreeDEngine.PreviewerVsix.csproj `
  --ignore-failed-sources `
  -s C:\LocalNuGet
```

После успешного restore сборку можно выполнять через `--no-restore`.

## Почему нельзя полностью убрать эти пакеты

VSIX компилируется против API Visual Studio Shell и должен собрать `.vsix` package. Эти build targets и reference assemblies поставляются через Visual Studio SDK / VSSDK BuildTools. Без них можно собрать отдельный helper-exe, но не полноценное расширение Visual Studio.


## Что означает `NU1101: Microsoft.VisualStudio.SDK / Microsoft.VSSDK.BuildTools not found`

Это значит, что NuGet недоступен, а нужных `.nupkg` нет ни в локальном cache, ни в Visual Studio offline feed. `RestoreIgnoreFailedSources` может игнорировать недоступный `nuget.org`, но не может собрать пакет, которого физически нет.

Проверка:

```powershell
dir "$env:USERPROFILE\.nuget\packages\microsoft.visualstudio.sdk"
dir "$env:USERPROFILE\.nuget\packages\microsoft.vssdk.buildtools"
dir "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\Microsoft.VisualStudio.SDK*"
dir "C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\Microsoft.VSSDK.BuildTools*"
```

Если каталогов нет, нужен один restore с доступом к NuGet или локальная папка с `.nupkg`.

Временный рабочий вариант без VSIX описан в `EXTERNAL_TOOL_FALLBACK_RU.md`.
