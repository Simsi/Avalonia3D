namespace Avalonia3D.Views;

public partial class MainView
{
    private void ShowSceneDiagnostics()
    {
        _showingDiagnosticReport = true;
        _statusText.Text = _sceneControl.CreateDiagnosticReport(maximumLogEntries: 128);
    }

}
