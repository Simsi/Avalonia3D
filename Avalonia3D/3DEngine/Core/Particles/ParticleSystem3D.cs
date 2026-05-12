using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Particles;

/// <summary>
/// Portable particle simulation object. Rendering backends consume particles as static
/// quad/cube meshes plus per-particle instance data; particle movement no longer rebuilds Mesh3D.
/// </summary>
public sealed class ParticleSystem3D : Object3D
{
    private static readonly Mesh3D StaticQuadMesh = CreateStaticQuadMesh();
    private static readonly Mesh3D StaticCubeMesh = CreateStaticCubeMesh();

    private readonly List<Particle3D> _particles;
    private readonly ParticleEmitter3D _emitter;
    private ParticleSystemSettings3D _settings;
    private float _emitAccumulator;
    private bool _isPlaying = true;
    private long _particleVersion;
    private Bounds3D _localParticleBounds = Bounds3D.Empty;
    private bool _particleBoundsDirty = true;

    public ParticleSystem3D(ParticleSystemSettings3D? settings = null, ParticleEmitter3D? emitter = null)
    {
        _settings = settings?.Clone() ?? new ParticleSystemSettings3D();
        _particles = new List<Particle3D>(global::System.Math.Max(1, _settings.Capacity));
        _emitter = emitter ?? new ParticleEmitter3D();
        Name = "Particle System";
        Material = Material3D.CreateUnlit(_settings.StartColor);
        IsPickable = false;
        if (_settings.Prewarm)
        {
            Prewarm();
        }
    }

    public ParticleSystemSettings3D Settings
    {
        get => _settings;
        set
        {
            var oldMode = _settings.RenderMode;
            _settings = value?.Clone() ?? new ParticleSystemSettings3D();
            TrimToCapacity();
            MarkParticlesDirty(markGeometryDirty: oldMode != _settings.RenderMode);
        }
    }

    public ParticleEmitter3D Emitter => _emitter;
    public IReadOnlyList<Particle3D> Particles => _particles;
    public int AliveCount => _particles.Count;
    public bool IsPlaying => _isPlaying;
    public long ParticleMeshVersion => _particleVersion;
    public long ParticleVersion => _particleVersion;

    public static Mesh3D GetStaticRenderMesh(ParticleRenderMode3D mode)
        => mode == ParticleRenderMode3D.Cube3D ? StaticCubeMesh : StaticQuadMesh;

    /// <summary>
    /// Kept for source compatibility. Billboard basis is now a renderer uniform, so camera motion
    /// does not dirty particle geometry.
    /// </summary>
    public void SetBillboardBasis(Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward)
    {
    }

    public void Play() => _isPlaying = true;
    public void Pause() => _isPlaying = false;

    public void Stop(bool clear = false)
    {
        _isPlaying = false;
        _emitAccumulator = 0f;
        if (clear)
        {
            _particles.Clear();
            MarkParticlesDirty(markGeometryDirty: false);
        }
    }

    public void Clear()
    {
        _particles.Clear();
        _emitAccumulator = 0f;
        MarkParticlesDirty(markGeometryDirty: false);
    }

    public void Emit(int count)
    {
        var emitted = false;
        for (var i = 0; i < count; i++) emitted |= SpawnParticle();
        if (emitted) MarkParticlesDirty(markGeometryDirty: false);
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;

        var changed = false;
        var write = 0;
        var count = _particles.Count;
        for (var read = 0; read < count; read++)
        {
            var particle = _particles[read];
            _emitter.Integrate(ref particle, deltaSeconds);
            if (!particle.Alive)
            {
                changed = true;
                continue;
            }

            if (write != read) changed = true;
            _particles[write++] = particle;
            changed = true;
        }

        if (write < count)
        {
            _particles.RemoveRange(write, count - write);
        }

        if (_isPlaying && _settings.Looping && _settings.EmissionRatePerSecond > 0f)
        {
            _emitAccumulator += _settings.EmissionRatePerSecond * deltaSeconds;
            var emitCount = (int)_emitAccumulator;
            if (emitCount > 0)
            {
                _emitAccumulator -= emitCount;
                for (var i = 0; i < emitCount; i++) changed |= SpawnParticle();
            }
        }

        if (changed)
        {
            MarkParticlesDirty(markGeometryDirty: false);
        }
    }

    public override Bounds3D GetWorldBounds()
    {
        if (_particles.Count == 0)
        {
            return Bounds3D.Empty;
        }

        var bounds = GetLocalParticleBounds();
        return bounds.IsValid ? bounds.Transform(GetModelMatrix()) : Bounds3D.Empty;
    }

    protected override Mesh3D BuildMesh()
        => GetStaticRenderMesh(_settings.RenderMode);

    public Bounds3D GetLocalParticleBounds()
    {
        if (!_particleBoundsDirty) return _localParticleBounds;
        if (_particles.Count == 0)
        {
            _localParticleBounds = Bounds3D.Empty;
            _particleBoundsDirty = false;
            return _localParticleBounds;
        }

        var first = true;
        var min = Vector3.Zero;
        var max = Vector3.Zero;
        for (var i = 0; i < _particles.Count; i++)
        {
            var particle = _particles[i];
            var size = global::System.MathF.Max(particle.StartSize, particle.EndSize) * 0.5f;
            var extent = new Vector3(size, size, size);
            var pMin = particle.Position - extent;
            var pMax = particle.Position + extent;
            if (first)
            {
                min = pMin;
                max = pMax;
                first = false;
            }
            else
            {
                min = Vector3.Min(min, pMin);
                max = Vector3.Max(max, pMax);
            }
        }

        _localParticleBounds = new Bounds3D(min, max);
        _particleBoundsDirty = false;
        return _localParticleBounds;
    }

    private void Prewarm()
    {
        var steps = global::System.Math.Max(1, (int)MathF.Ceiling(_settings.ParticleLifetimeSeconds * 12f));
        var dt = _settings.ParticleLifetimeSeconds / steps;
        for (var i = 0; i < steps; i++) Advance(dt);
    }

    private bool SpawnParticle()
    {
        if (_particles.Count >= global::System.Math.Max(1, _settings.Capacity)) return false;
        _particles.Add(_emitter.Create(_settings));
        return true;
    }

    private void TrimToCapacity()
    {
        var capacity = global::System.Math.Max(1, _settings.Capacity);
        if (_particles.Count <= capacity) return;
        _particles.RemoveRange(capacity, _particles.Count - capacity);
    }

    private void MarkParticlesDirty(bool markGeometryDirty)
    {
        _particleVersion++;
        _particleBoundsDirty = true;
        MarkWorldBoundsDirtyRecursive();
        if (markGeometryDirty)
        {
            MarkGeometryDirty();
        }
        else
        {
            RaiseChanged(SceneChangeKind.Transform);
        }
    }

    private static Mesh3D CreateStaticQuadMesh()
    {
        var positions = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        var normals = new[] { Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ };
        var uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };
        var indices = new[] { 0, 1, 2, 0, 2, 3 };
        return new Mesh3D(positions, normals, indices, "__particle_static_quad", texCoords0: uv);
    }

    private static Mesh3D CreateStaticCubeMesh()
    {
        var positions = new List<Vector3>(24);
        var normals = new List<Vector3>(24);
        var indices = new List<int>(36);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            var offset = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            indices.Add(offset + 0); indices.Add(offset + 1); indices.Add(offset + 2);
            indices.Add(offset + 0); indices.Add(offset + 2); indices.Add(offset + 3);
        }

        const float h = 0.5f;
        var p000 = new Vector3(-h, -h, -h); var p100 = new Vector3(h, -h, -h);
        var p110 = new Vector3(h, h, -h); var p010 = new Vector3(-h, h, -h);
        var p001 = new Vector3(-h, -h, h); var p101 = new Vector3(h, -h, h);
        var p111 = new Vector3(h, h, h); var p011 = new Vector3(-h, h, h);
        Face(p001, p101, p111, p011, Vector3.UnitZ);
        Face(p100, p000, p010, p110, -Vector3.UnitZ);
        Face(p000, p001, p011, p010, -Vector3.UnitX);
        Face(p101, p100, p110, p111, Vector3.UnitX);
        Face(p010, p011, p111, p110, Vector3.UnitY);
        Face(p000, p100, p101, p001, -Vector3.UnitY);
        return new Mesh3D(positions.ToArray(), normals.ToArray(), indices.ToArray(), "__particle_static_cube");
    }
}
