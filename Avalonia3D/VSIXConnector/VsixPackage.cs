using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;
using ThreeDEngine.PreviewerVsix.Commands;

namespace ThreeDEngine.PreviewerVsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("3DEngine Previewer Connector", "Open 3D Preview command for 3DEngine classes", "0.1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(Open3DPreviewCommandPackageGuids.PackageGuidString)]
public sealed class VsixPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService is null)
        {
            return;
        }

        await Open3DPreviewCommand.InitializeAsync(this, commandService);
    }
}
