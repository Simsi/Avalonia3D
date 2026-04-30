# Сборка VSIX connector

Этот проект оставлен SDK-style, но VSIX packaging подключается так, как требует VSSDK: через `CustomAfterMicrosoftCSharpTargets`, то есть после `Microsoft.CSharp.targets`.

Причина: если импортировать `Microsoft.VsSDK.targets` вручную в произвольном месте или не импортировать его вообще, Visual Studio может собрать только `ThreeDEngine.PreviewerVsix.dll` и не создать `.vsix`.

## Требования

- Visual Studio 2022.
- Workload: **Visual Studio extension development / Разработка расширений Visual Studio**.
- NuGet packages: `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`.

## Сборка

```powershell
Remove-Item .\VSIXConnector\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\VSIXConnector\obj -Recurse -Force -ErrorAction SilentlyContinue
```

Затем собрать проект `ThreeDEngine.PreviewerVsix` из Visual Studio.

Ожидаемый файл:

```text
VSIXConnector\bin\Debug\net472\ThreeDEngine.PreviewerVsix.vsix
```

Если сборка успешна, но `.vsix` не создан, проект после `PrepareForRun` теперь падает явной ошибкой `FailIfVsixWasNotProduced`, а не молча оставляет только `.dll`.
