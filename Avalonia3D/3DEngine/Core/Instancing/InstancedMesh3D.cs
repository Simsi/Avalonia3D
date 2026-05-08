using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Instancing;

/// <summary>
/// Thin high-scale facade for repeated static mesh rendering. It reuses HighScaleInstanceLayer3D so the
/// existing OpenGL/WebGL high-scale renderers can handle the actual GPU buffers and LOD policies.
/// </summary>
public sealed class InstancedMesh3D : HighScaleInstanceLayer3D
{
    private static int _nextTemplateId = 700000;

    public InstancedMesh3D(
        string name,
        Mesh3D mesh,
        Material3D? material = null,
        int initialCapacity = 1024,
        float chunkCellSize = 24f)
        : base(CreateTemplate(name, mesh, material ?? Material3D.CreateLambert(ColorRgba.White)), initialCapacity, chunkCellSize)
    {
        Name = name;
        SourceMesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        SourceMaterial = material ?? Material3D.CreateLambert(ColorRgba.White);
    }

    public Mesh3D SourceMesh { get; }
    public Material3D SourceMaterial { get; }

    public int AddInstance(Vector3 position, Vector3? scale = null, int materialVariantId = 0, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        var s = scale ?? Vector3.One;
        var transform = Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(position);
        return AddInstance(transform, materialVariantId, -1, flags);
    }

    public void AddInstances(IEnumerable<Vector3> positions, Vector3? scale = null, int materialVariantId = 0, InstanceFlags3D flags = InstanceFlags3D.Visible | InstanceFlags3D.Pickable)
    {
        if (positions is null) return;
        foreach (var position in positions)
        {
            AddInstance(position, scale, materialVariantId, flags);
        }
    }

    private static CompositeTemplate3D CreateTemplate(string name, Mesh3D mesh, Material3D material)
    {
        var id = System.Threading.Interlocked.Increment(ref _nextTemplateId);
        var part = new CompositePartTemplate3D(
            name + " Part",
            mesh ?? Mesh3D.Empty,
            new MeshResourceKey((mesh ?? Mesh3D.Empty).ResourceKey),
            materialSlot: 0,
            localTransform: Matrix4x4.Identity,
            baseColor: material.EffectiveColor,
            lightingMode: material.Lighting);
        return new CompositeTemplate3D(id, name, new[] { part });
    }
}
