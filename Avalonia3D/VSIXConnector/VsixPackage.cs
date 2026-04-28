using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;
using ThreeDEngine.PreviewerVsix.Commands;

namespace ThreeDEngine.PreviewerVsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("3DEngine Previewer Connector", "Open 3D Preview command for 3DEngine classes", "0.3.1")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(Open3DPreviewCommandPackageGuids.PackageGuidString)]
public sealed class VsixPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService is null)
        {
            return;
        }

        await Open3DPreviewCommand.InitializeAsync(this, commandService);
    }
}
