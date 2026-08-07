using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

internal enum RenderGraphResourceKind3D
{
    Buffer = 0,
    Texture = 1
}

internal readonly record struct RenderGraphResourceHandle3D(int Id)
{
    public bool IsValid => Id > 0;
}

internal readonly record struct RenderGraphResourceDescriptor3D(
    string Name,
    RenderGraphResourceKind3D Kind,
    RhiBufferDescriptor3D Buffer,
    RhiTextureDescriptor3D Texture,
    bool Transient,
    RhiResourceHandle3D ImportedHandle)
{
    public static RenderGraphResourceDescriptor3D CreateBuffer(string name, RhiBufferDescriptor3D descriptor, bool transient = true)
        => new(RequireName(name), RenderGraphResourceKind3D.Buffer, descriptor, default, transient, default);

    public static RenderGraphResourceDescriptor3D CreateTexture(string name, RhiTextureDescriptor3D descriptor, bool transient = true)
        => new(RequireName(name), RenderGraphResourceKind3D.Texture, default, descriptor, transient, default);

    public static RenderGraphResourceDescriptor3D ImportBuffer(string name, RhiResourceHandle3D handle, RhiBufferDescriptor3D descriptor)
    {
        if (!handle.IsValid || handle.Kind != RhiResourceKind3D.Buffer) throw new ArgumentException("Imported render-graph buffer handle is invalid.", nameof(handle));
        return new(RequireName(name), RenderGraphResourceKind3D.Buffer, descriptor, default, false, handle);
    }

    public static RenderGraphResourceDescriptor3D ImportTexture(string name, RhiResourceHandle3D handle, RhiTextureDescriptor3D descriptor)
    {
        if (!handle.IsValid || handle.Kind != RhiResourceKind3D.Texture) throw new ArgumentException("Imported render-graph texture handle is invalid.", nameof(handle));
        return new(RequireName(name), RenderGraphResourceKind3D.Texture, default, descriptor, false, handle);
    }

    public bool IsImported => ImportedHandle.IsValid;

    private static string RequireName(string name)
        => string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Render-graph resource name cannot be empty.", nameof(name)) : name;
}

internal readonly record struct RenderGraphResourceUse3D(
    RenderGraphResourceHandle3D Resource,
    RhiPipelineStage3D Stage,
    RhiResourceAccess3D Access,
    bool Write);

internal sealed class RenderGraphPass3D
{
    private readonly List<RenderGraphResourceUse3D> _uses = new();

    public RenderGraphPass3D(string name, Action<RenderGraphExecutionContext3D, RhiCommandEncoder3D> execute)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Render-graph pass name cannot be empty.", nameof(name)) : name;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public string Name { get; }
    public IReadOnlyList<RenderGraphResourceUse3D> Uses => _uses;
    public Action<RenderGraphExecutionContext3D, RhiCommandEncoder3D> Execute { get; }

    public RenderGraphPass3D Read(RenderGraphResourceHandle3D resource, RhiPipelineStage3D stage, RhiResourceAccess3D access)
    {
        ValidateUse(resource, stage, access);
        _uses.Add(new RenderGraphResourceUse3D(resource, stage, access, false));
        return this;
    }

    public RenderGraphPass3D Write(RenderGraphResourceHandle3D resource, RhiPipelineStage3D stage, RhiResourceAccess3D access)
    {
        ValidateUse(resource, stage, access);
        _uses.Add(new RenderGraphResourceUse3D(resource, stage, access, true));
        return this;
    }

    private static void ValidateUse(RenderGraphResourceHandle3D resource, RhiPipelineStage3D stage, RhiResourceAccess3D access)
    {
        if (!resource.IsValid) throw new ArgumentException("Render-graph resource handle is invalid.", nameof(resource));
        if (stage == RhiPipelineStage3D.None) throw new ArgumentOutOfRangeException(nameof(stage));
        if (access == RhiResourceAccess3D.None) throw new ArgumentOutOfRangeException(nameof(access));
    }
}

internal sealed class RenderGraphExecutionContext3D
{
    private RhiResourceHandle3D[] _physicalResources = Array.Empty<RhiResourceHandle3D>();

    internal void Reset(RhiResourceHandle3D[] physicalResources) => _physicalResources = physicalResources;

    public RhiResourceHandle3D GetResource(RenderGraphResourceHandle3D handle)
    {
        if (!handle.IsValid || handle.Id >= _physicalResources.Length)
            throw new ArgumentOutOfRangeException(nameof(handle));
        var resource = _physicalResources[handle.Id];
        if (!resource.IsValid) throw new InvalidOperationException($"Render-graph resource {handle.Id} has no physical allocation.");
        return resource;
    }
}

internal readonly record struct RenderGraphCompilationStatistics3D(
    int LogicalResourceCount,
    int PhysicalResourceCount,
    int AliasedResourceCount,
    int PassCount,
    int BarrierCount);

/// <summary>
/// Deterministic render graph with interval-based transient aliasing and explicit hazard barriers.
/// Passes remain in declaration order; cycles are impossible because dependencies only point to
/// prior passes. A missing declaration is treated as a programming error rather than inferred.
/// </summary>
internal sealed class RenderGraph3D : IDisposable
{
    private readonly List<RenderGraphResourceDescriptor3D> _resources = new();
    private readonly List<RenderGraphPass3D> _passes = new();
    private readonly RenderGraphExecutionContext3D _context = new();
    private CompiledGraph? _compiled;
    private RhiDevice3D? _device;
    private RhiResourceHandle3D[] _ownedPhysicalResources = Array.Empty<RhiResourceHandle3D>();
    private bool _disposed;

    public RenderGraphResourceHandle3D CreateBuffer(string name, RhiBufferDescriptor3D descriptor, bool transient = true)
        => AddResource(RenderGraphResourceDescriptor3D.CreateBuffer(name, descriptor, transient));

    public RenderGraphResourceHandle3D CreateTexture(string name, RhiTextureDescriptor3D descriptor, bool transient = true)
        => AddResource(RenderGraphResourceDescriptor3D.CreateTexture(name, descriptor, transient));

    public RenderGraphResourceHandle3D ImportBuffer(string name, RhiResourceHandle3D handle, RhiBufferDescriptor3D descriptor)
        => AddResource(RenderGraphResourceDescriptor3D.ImportBuffer(name, handle, descriptor));

    public RenderGraphResourceHandle3D ImportTexture(string name, RhiResourceHandle3D handle, RhiTextureDescriptor3D descriptor)
        => AddResource(RenderGraphResourceDescriptor3D.ImportTexture(name, handle, descriptor));

    public RenderGraphPass3D AddPass(string name, Action<RenderGraphExecutionContext3D, RhiCommandEncoder3D> execute)
    {
        if (_compiled is not null) throw new InvalidOperationException("Cannot add a pass after render-graph compilation.");
        var pass = new RenderGraphPass3D(name, execute);
        _passes.Add(pass);
        return pass;
    }

    public RenderGraphCompilationStatistics3D Compile(RhiDevice3D device, string resourceOwner)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_compiled is not null) return _compiled.Statistics;
        if (_passes.Count == 0) throw new InvalidOperationException("Render graph contains no passes.");
        if (string.IsNullOrWhiteSpace(resourceOwner)) throw new ArgumentException("Render-graph resource owner cannot be empty.", nameof(resourceOwner));

        var firstUse = new int[_resources.Count];
        var lastUse = new int[_resources.Count];
        Array.Fill(firstUse, int.MaxValue);
        Array.Fill(lastUse, -1);
        for (var passIndex = 0; passIndex < _passes.Count; passIndex++)
        {
            foreach (var use in _passes[passIndex].Uses)
            {
                var index = RequireResourceIndex(use.Resource);
                if (passIndex < firstUse[index]) firstUse[index] = passIndex;
                if (passIndex > lastUse[index]) lastUse[index] = passIndex;
            }
        }

        for (var i = 0; i < _resources.Count; i++)
        {
            if (lastUse[i] < 0) throw new InvalidOperationException($"Render-graph resource '{_resources[i].Name}' is never used.");
        }

        var physicalSlots = new List<PhysicalSlot>();
        var physicalIndexByLogical = new int[_resources.Count];
        for (var logicalIndex = 0; logicalIndex < _resources.Count; logicalIndex++)
        {
            var descriptor = _resources[logicalIndex];
            var assigned = -1;
            if (descriptor.IsImported)
            {
                assigned = physicalSlots.Count;
                physicalSlots.Add(new PhysicalSlot(descriptor, lastUse[logicalIndex]));
            }
            else if (descriptor.Transient)
            {
                for (var slotIndex = 0; slotIndex < physicalSlots.Count; slotIndex++)
                {
                    var slot = physicalSlots[slotIndex];
                    if (slot.LastUse < firstUse[logicalIndex] && Compatible(slot.Descriptor, descriptor))
                    {
                        assigned = slotIndex;
                        slot.LastUse = lastUse[logicalIndex];
                        break;
                    }
                }
            }

            if (assigned < 0)
            {
                assigned = physicalSlots.Count;
                physicalSlots.Add(new PhysicalSlot(descriptor, lastUse[logicalIndex]));
            }
            physicalIndexByLogical[logicalIndex] = assigned;
        }

        var physicalHandles = new RhiResourceHandle3D[physicalSlots.Count];
        var ownedPhysical = new List<RhiResourceHandle3D>(physicalSlots.Count);
        for (var i = 0; i < physicalSlots.Count; i++)
        {
            var descriptor = physicalSlots[i].Descriptor;
            if (descriptor.IsImported)
            {
                device.Resources.RequireLive(descriptor.ImportedHandle, "render-graph import");
                physicalHandles[i] = descriptor.ImportedHandle;
                continue;
            }
            var key = $"render-graph:{resourceOwner}:{i}:{descriptor.Name}";
            physicalHandles[i] = descriptor.Kind == RenderGraphResourceKind3D.Buffer
                ? device.CreateBuffer(key, descriptor.Buffer, 1, resourceOwner)
                : device.CreateTexture(key, descriptor.Texture, 1, resourceOwner);
            ownedPhysical.Add(physicalHandles[i]);
        }

        var logicalHandles = new RhiResourceHandle3D[_resources.Count + 1];
        for (var logicalIndex = 0; logicalIndex < _resources.Count; logicalIndex++)
            logicalHandles[logicalIndex + 1] = physicalHandles[physicalIndexByLogical[logicalIndex]];

        var barriersBeforePass = BuildBarriers(logicalHandles);
        var barrierCount = 0;
        foreach (var barriers in barriersBeforePass) barrierCount += barriers.Count;
        var statistics = new RenderGraphCompilationStatistics3D(
            _resources.Count,
            physicalSlots.Count,
            _resources.Count - physicalSlots.Count,
            _passes.Count,
            barrierCount);
        _compiled = new CompiledGraph(logicalHandles, barriersBeforePass, statistics);
        _device = device;
        _ownedPhysicalResources = ownedPhysical.ToArray();
        _context.Reset(logicalHandles);
        return statistics;
    }

    public void Encode(RhiCommandEncoder3D encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var compiled = _compiled ?? throw new InvalidOperationException("Render graph must be compiled before encoding.");
        encoder.PushDebugGroup("gpu-driven-render-graph");
        for (var passIndex = 0; passIndex < _passes.Count; passIndex++)
        {
            foreach (var barrier in compiled.BarriersBeforePass[passIndex]) encoder.Barrier(barrier);
            encoder.PushDebugGroup(_passes[passIndex].Name);
            _passes[passIndex].Execute(_context, encoder);
            encoder.PopDebugGroup();
        }
        encoder.PopDebugGroup();
    }

    public RenderGraphCompilationStatistics3D Statistics
        => _compiled?.Statistics ?? default;

    public RhiResourceHandle3D GetResource(RenderGraphResourceHandle3D handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_compiled is null) throw new InvalidOperationException("Render graph must be compiled before resolving resources.");
        return _context.GetResource(handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var device = _device;
        if (device is not null && !device.IsDisposed)
        {
            for (var i = 0; i < _ownedPhysicalResources.Length; i++)
                device.Resources.Release(_ownedPhysicalResources[i]);
        }
        _ownedPhysicalResources = Array.Empty<RhiResourceHandle3D>();
        _compiled = null;
        _device = null;
    }

    private RenderGraphResourceHandle3D AddResource(RenderGraphResourceDescriptor3D descriptor)
    {
        if (_compiled is not null) throw new InvalidOperationException("Cannot add a resource after render-graph compilation.");
        _resources.Add(descriptor);
        return new RenderGraphResourceHandle3D(_resources.Count);
    }

    private List<RhiResourceBarrier3D>[] BuildBarriers(RhiResourceHandle3D[] logicalHandles)
    {
        var barriers = new List<RhiResourceBarrier3D>[_passes.Count];
        for (var i = 0; i < barriers.Length; i++) barriers[i] = new List<RhiResourceBarrier3D>();
        var previous = new Dictionary<RhiResourceHandle3D, RenderGraphResourceUse3D>();
        for (var passIndex = 0; passIndex < _passes.Count; passIndex++)
        {
            foreach (var use in _passes[passIndex].Uses)
            {
                var physical = logicalHandles[use.Resource.Id];
                if (previous.TryGetValue(physical, out var prior) && (prior.Write || use.Write || prior.Access != use.Access || prior.Stage != use.Stage))
                {
                    barriers[passIndex].Add(new RhiResourceBarrier3D(
                        physical,
                        prior.Stage,
                        prior.Access,
                        use.Stage,
                        use.Access));
                }
                previous[physical] = use;
            }
        }
        return barriers;
    }

    private int RequireResourceIndex(RenderGraphResourceHandle3D handle)
    {
        if (!handle.IsValid || handle.Id > _resources.Count)
            throw new ArgumentOutOfRangeException(nameof(handle));
        return handle.Id - 1;
    }

    private static bool Compatible(RenderGraphResourceDescriptor3D a, RenderGraphResourceDescriptor3D b)
        => !a.IsImported && !b.IsImported && a.Kind == b.Kind &&
           (a.Kind == RenderGraphResourceKind3D.Buffer ? a.Buffer.Equals(b.Buffer) : a.Texture.Equals(b.Texture));

    private sealed class PhysicalSlot
    {
        public PhysicalSlot(RenderGraphResourceDescriptor3D descriptor, int lastUse)
        {
            Descriptor = descriptor;
            LastUse = lastUse;
        }

        public RenderGraphResourceDescriptor3D Descriptor { get; }
        public int LastUse { get; set; }
    }

    private sealed record CompiledGraph(
        RhiResourceHandle3D[] LogicalHandles,
        List<RhiResourceBarrier3D>[] BarriersBeforePass,
        RenderGraphCompilationStatistics3D Statistics);
}
