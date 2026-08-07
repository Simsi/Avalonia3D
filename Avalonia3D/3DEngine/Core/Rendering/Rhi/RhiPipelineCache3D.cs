using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>Descriptor-keyed logical pipeline cache. Native backend pipeline objects use the same handles.</summary>
internal sealed class RhiPipelineCache3D
{
    private readonly RhiResourceRegistry3D _resources;
    private readonly Dictionary<RhiRenderPipelineDescriptor3D, RhiResourceHandle3D> _render = new();
    private readonly Dictionary<RhiComputePipelineDescriptor3D, RhiResourceHandle3D> _compute = new();
    private long _hits;
    private long _misses;

    public RhiPipelineCache3D(RhiResourceRegistry3D resources) => _resources = resources ?? throw new ArgumentNullException(nameof(resources));

    public int Count => _render.Count + _compute.Count;
    public long Hits => _hits;
    public long Misses => _misses;

    public RhiResourceHandle3D GetOrCreate(RhiRenderPipelineDescriptor3D descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        if (_render.TryGetValue(descriptor, out var existing) && _resources.Contains(existing))
        {
            _hits++;
            return existing;
        }
        var handle = _resources.RegisterRenderPipeline($"render-pipeline:{_misses + 1}:{descriptor.GetHashCode():X8}", descriptor, 1, "pipeline-cache");
        _render[descriptor] = handle;
        _misses++;
        return handle;
    }

    public RhiResourceHandle3D GetOrCreate(RhiComputePipelineDescriptor3D descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        if (_compute.TryGetValue(descriptor, out var existing) && _resources.Contains(existing))
        {
            _hits++;
            return existing;
        }
        var handle = _resources.RegisterComputePipeline($"compute-pipeline:{_misses + 1}:{descriptor.GetHashCode():X8}", descriptor, 1, "pipeline-cache");
        _compute[descriptor] = handle;
        _misses++;
        return handle;
    }

    public void Clear(bool releaseResources)
    {
        if (releaseResources)
        {
            foreach (var handle in _render.Values) _resources.Release(handle);
            foreach (var handle in _compute.Values) _resources.Release(handle);
        }
        _render.Clear();
        _compute.Clear();
    }
}
