using System;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeDEngine.Core.Rendering.Extensions;

public readonly record struct RenderExtensionCompilationResult3D(
    long RegistryVersion,
    int ExtensionCount,
    int PassCount,
    string Backend,
    DateTimeOffset CompiledUtc);

/// <summary>
/// Native GPU compiler for render-extension snapshots. Implementations must compile declared GPU
/// shaders and resource dependencies; legacy callback emulation and CPU execution are prohibited.
/// </summary>
public interface IRenderExtensionBackend3D
{
    string Name { get; }
    ValueTask<RenderExtensionCompilationResult3D> CompileAsync(
        RenderExtensionSnapshot3D snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class RenderExtensionRuntime3D
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _compileGate = new(1, 1);
    private IRenderExtensionBackend3D? _backend;
    private RenderExtensionCompilationResult3D? _lastCompilation;

    public bool IsAvailable { get { lock (_gate) return _backend is not null; } }
    public string BackendName { get { lock (_gate) return _backend?.Name ?? "unavailable"; } }
    public RenderExtensionCompilationResult3D? LastCompilation { get { lock (_gate) return _lastCompilation; } }

    public void AttachBackend(IRenderExtensionBackend3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (string.IsNullOrWhiteSpace(backend.Name)) throw new ArgumentException("Render-extension backend name cannot be empty.", nameof(backend));
        lock (_gate)
        {
            if (_backend is not null) throw new InvalidOperationException($"Render-extension backend '{_backend.Name}' is already attached.");
            _backend = backend;
            _lastCompilation = null;
        }
    }

    public void DetachBackend(IRenderExtensionBackend3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            if (!ReferenceEquals(_backend, backend)) throw new InvalidOperationException("The supplied render-extension backend is not attached.");
            _backend = null;
            _lastCompilation = null;
        }
    }

    public async ValueTask<RenderExtensionCompilationResult3D> CompileAsync(
        RenderExtensionSnapshot3D snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _compileGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IRenderExtensionBackend3D backend;
            lock (_gate) backend = _backend ?? throw new InvalidOperationException("Custom render passes require a native GPU render-extension backend. Legacy raster emulation is prohibited.");
            var result = await backend.CompileAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(result.Backend, backend.Name)) throw new InvalidOperationException($"Render-extension backend reported identity '{result.Backend}', expected '{backend.Name}'.");
            if (result.RegistryVersion != snapshot.Version || result.ExtensionCount != snapshot.Extensions.Count || result.PassCount != snapshot.PassCount)
                throw new InvalidOperationException("Render-extension backend returned compilation metadata that does not match the immutable registry snapshot.");
            lock (_gate)
            {
                if (!ReferenceEquals(_backend, backend)) throw new InvalidOperationException("Render-extension backend changed while compilation was in progress.");
                if (_lastCompilation is null || result.RegistryVersion >= _lastCompilation.Value.RegistryVersion)
                    _lastCompilation = result;
            }
            return result;
        }
        finally
        {
            _compileGate.Release();
        }
    }
}
