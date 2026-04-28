# Разрешение типов в PreviewerApp

PreviewerApp принимает `--assembly` и необязательный `--type`.

В версии 0.2.5 previewer стал терпимее к ошибкам namespace:

- сначала ищется точный CLR full name;
- затем ищется совпадение по короткому имени типа;
- если тип не найден, выводятся похожие типы из assembly;
- если в assembly есть previewable-типы, но не под запрошенным именем, ошибка предлагает запустить previewer без `--type`.

Пример для класса `BenchmarkRack3D`, который находится в файле с `namespace Avalonia3D.Views;`:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -- --assembly .\bin\Debug\net8.0\Avalonia3D.dll --type Avalonia3D.Views.BenchmarkRack3D
```

Если namespace неизвестен:

```powershell
dotnet run --project .\PreviewerApp\PreviewerApp.csproj -- --assembly .\bin\Debug\net8.0\Avalonia3D.dll --type BenchmarkRack3D
```

Для просмотра всех previewable entry points можно временно не передавать `--type`.
