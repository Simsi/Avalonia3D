using System;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Resources;

/// <summary>CPU/GPU resource budgets and deferred destruction policy for an engine scope.</summary>
public sealed class EngineResourceOptions3D
{
    private long _maxCpuTextureBytes = 512L * 1024L * 1024L;
    private long _maxCpuShaderBytes = 64L * 1024L * 1024L;
    private long _maxGpuResidentBytes = 1024L * 1024L * 1024L;
    private long _maxGpuTextureBytes = 768L * 1024L * 1024L;
    private int _deferredReleaseFrames = 3;

    public long MaxCpuTextureBytes
    {
        get => _maxCpuTextureBytes;
        set => _maxCpuTextureBytes = Guard3D.Range(value, 1L * 1024L * 1024L, 64L * 1024L * 1024L * 1024L, nameof(value));
    }


    public long MaxCpuShaderBytes
    {
        get => _maxCpuShaderBytes;
        set => _maxCpuShaderBytes = Guard3D.Range(value, 1L * 1024L * 1024L, 4L * 1024L * 1024L * 1024L, nameof(value));
    }

    public long MaxGpuResidentBytes
    {
        get => _maxGpuResidentBytes;
        set => _maxGpuResidentBytes = Guard3D.Range(value, 1L * 1024L * 1024L, 128L * 1024L * 1024L * 1024L, nameof(value));
    }

    public long MaxGpuTextureBytes
    {
        get => _maxGpuTextureBytes;
        set => _maxGpuTextureBytes = Guard3D.Range(value, 1L * 1024L * 1024L, 128L * 1024L * 1024L * 1024L, nameof(value));
    }

    public int DeferredReleaseFrames
    {
        get => _deferredReleaseFrames;
        set => _deferredReleaseFrames = Guard3D.Range(value, 0, 16, nameof(value));
    }

    internal EngineResourceConfiguration3D Freeze()
    {
        if (_maxGpuTextureBytes > _maxGpuResidentBytes)
            throw new InvalidOperationException("MaxGpuTextureBytes cannot exceed MaxGpuResidentBytes.");
        return new EngineResourceConfiguration3D(_maxCpuTextureBytes, _maxGpuResidentBytes, _maxGpuTextureBytes, _deferredReleaseFrames, _maxCpuShaderBytes);
    }
}

public sealed record EngineResourceConfiguration3D
{
    public EngineResourceConfiguration3D(
        long MaxCpuTextureBytes,
        long MaxGpuResidentBytes,
        long MaxGpuTextureBytes,
        int DeferredReleaseFrames,
        long MaxCpuShaderBytes = 64L * 1024L * 1024L)
    {
        this.MaxCpuTextureBytes = Guard3D.Range(MaxCpuTextureBytes, 1L * 1024L * 1024L, 64L * 1024L * 1024L * 1024L, nameof(MaxCpuTextureBytes));
        this.MaxCpuShaderBytes = Guard3D.Range(MaxCpuShaderBytes, 1L * 1024L * 1024L, 4L * 1024L * 1024L * 1024L, nameof(MaxCpuShaderBytes));
        this.MaxGpuResidentBytes = Guard3D.Range(MaxGpuResidentBytes, 1L * 1024L * 1024L, 128L * 1024L * 1024L * 1024L, nameof(MaxGpuResidentBytes));
        this.MaxGpuTextureBytes = Guard3D.Range(MaxGpuTextureBytes, 1L * 1024L * 1024L, 128L * 1024L * 1024L * 1024L, nameof(MaxGpuTextureBytes));
        this.DeferredReleaseFrames = Guard3D.Range(DeferredReleaseFrames, 0, 16, nameof(DeferredReleaseFrames));
        if (this.MaxGpuTextureBytes > this.MaxGpuResidentBytes)
            throw new ArgumentException("GPU texture budget cannot exceed the total GPU resident budget.", nameof(MaxGpuTextureBytes));
    }

    public long MaxCpuTextureBytes { get; }
    public long MaxCpuShaderBytes { get; }
    public long MaxGpuResidentBytes { get; }
    public long MaxGpuTextureBytes { get; }
    public int DeferredReleaseFrames { get; }
}
