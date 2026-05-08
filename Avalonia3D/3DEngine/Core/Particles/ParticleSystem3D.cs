using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Particles;

/// <summary>
/// Portable particle system rendered as one dynamic indexed mesh. It is intentionally backend-neutral:
/// OpenGL can batch it as a single mesh draw, and WebGL can use the existing packet path without a compute shader.
/// </summary>
public sealed class ParticleSystem3D : Object3D
{
    private readonly List<Particle3D> _particles;
    private readonly ParticleEmitter3D _emitter;
    private ParticleSystemSettings3D _settings;
    private float _emitAccumulator;
    private bool _isPlaying = true;
    private long _meshVersion;
    private Vector3 _billboardRight = Vector3.UnitX;
    private Vector3 _billboardUp = Vector3.UnitY;
    private Vector3 _billboardForward = Vector3.UnitZ;

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
            _settings = value?.Clone() ?? new ParticleSystemSettings3D();
            TrimToCapacity();
            MarkGeometryDirty();
        }
    }

    public ParticleEmitter3D Emitter => _emitter;
    public IReadOnlyList<Particle3D> Particles => _particles;
    public int AliveCount => _particles.Count;
    public bool IsPlaying => _isPlaying;
    public long ParticleMeshVersion => _meshVersion;

    public void SetBillboardBasis(Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward)
    {
        cameraRight = SafeNormalize(cameraRight, Vector3.UnitX);
        cameraUp = SafeNormalize(cameraUp, Vector3.UnitY);
        cameraForward = SafeNormalize(cameraForward, Vector3.UnitZ);
        if (Vector3.DistanceSquared(_billboardRight, cameraRight) < 0.000001f &&
            Vector3.DistanceSquared(_billboardUp, cameraUp) < 0.000001f &&
            Vector3.DistanceSquared(_billboardForward, cameraForward) < 0.000001f)
        {
            return;
        }

        _billboardRight = cameraRight;
        _billboardUp = cameraUp;
        _billboardForward = cameraForward;
        if (_settings.RenderMode == ParticleRenderMode3D.CameraFacingQuad)
        {
            _meshVersion++;
            MarkGeometryDirty();
        }
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
            _meshVersion++;
            MarkGeometryDirty();
        }
    }

    public void Clear()
    {
        _particles.Clear();
        _emitAccumulator = 0f;
        _meshVersion++;
        MarkGeometryDirty();
    }

    public void Emit(int count)
    {
        for (var i = 0; i < count; i++) SpawnParticle();
        _meshVersion++;
        MarkGeometryDirty();
    }

    public void Advance(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;

        var changed = false;
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            _emitter.Integrate(ref particle, deltaSeconds);
            if (!particle.Alive)
            {
                _particles.RemoveAt(i);
            }
            else
            {
                _particles[i] = particle;
            }
            changed = true;
        }

        if (_isPlaying && _settings.Looping && _settings.EmissionRatePerSecond > 0f)
        {
            _emitAccumulator += _settings.EmissionRatePerSecond * deltaSeconds;
            var emitCount = (int)_emitAccumulator;
            if (emitCount > 0)
            {
                _emitAccumulator -= emitCount;
                for (var i = 0; i < emitCount; i++) SpawnParticle();
                changed = true;
            }
        }

        if (changed)
        {
            _meshVersion++;
            MarkGeometryDirty();
        }
    }

    protected override Mesh3D BuildMesh()
    {
        if (_particles.Count == 0)
        {
            return Mesh3D.Empty;
        }

        return _settings.RenderMode == ParticleRenderMode3D.Cube3D
            ? BuildCubeParticleMesh()
            : BuildQuadParticleMesh();
    }

    private Mesh3D BuildQuadParticleMesh()
    {
        var vertexCount = _particles.Count * 4;
        var indexCount = _particles.Count * 6;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uv = new Vector2[vertexCount];
        var colors = new ColorRgba[vertexCount];
        var indices = new int[indexCount];

        var vertex = 0;
        var index = 0;
        for (var i = 0; i < _particles.Count; i++)
        {
            var particle = _particles[i];
            var t = particle.NormalizedAge;
            var size = Lerp(particle.StartSize, particle.EndSize, t);
            var half = size * 0.5f;
            var color = Lerp(particle.StartColor, particle.EndColor, t);
            var center = particle.Position;

            var right = _billboardRight * half;
            var up = _billboardUp * half;
            positions[vertex + 0] = center - right - up;
            positions[vertex + 1] = center + right - up;
            positions[vertex + 2] = center + right + up;
            positions[vertex + 3] = center - right + up;
            normals[vertex + 0] = _billboardForward;
            normals[vertex + 1] = _billboardForward;
            normals[vertex + 2] = _billboardForward;
            normals[vertex + 3] = _billboardForward;
            uv[vertex + 0] = new Vector2(0f, 1f);
            uv[vertex + 1] = new Vector2(1f, 1f);
            uv[vertex + 2] = new Vector2(1f, 0f);
            uv[vertex + 3] = new Vector2(0f, 0f);
            colors[vertex + 0] = color;
            colors[vertex + 1] = color;
            colors[vertex + 2] = color;
            colors[vertex + 3] = color;

            indices[index + 0] = vertex + 0;
            indices[index + 1] = vertex + 1;
            indices[index + 2] = vertex + 2;
            indices[index + 3] = vertex + 0;
            indices[index + 4] = vertex + 2;
            indices[index + 5] = vertex + 3;
            vertex += 4;
            index += 6;
        }

        var key = $"particles:{Id}:{_settings.RenderMode}:{_meshVersion}:{_particles.Count}";
        return new Mesh3D(positions, normals, indices, key, texCoords0: uv, vertexColors0: colors);
    }

    private Mesh3D BuildCubeParticleMesh()
    {
        var vertexCount = _particles.Count * 24;
        var indexCount = _particles.Count * 36;
        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var colors = new ColorRgba[vertexCount];
        var indices = new int[indexCount];

        var vertex = 0;
        var index = 0;
        for (var i = 0; i < _particles.Count; i++)
        {
            var particle = _particles[i];
            var t = particle.NormalizedAge;
            var size = Lerp(particle.StartSize, particle.EndSize, t);
            var half = size * 0.5f;
            var color = Lerp(particle.StartColor, particle.EndColor, t);
            WriteCubeParticle(particle.Position, half, color, positions, normals, colors, indices, ref vertex, ref index);
        }

        var key = $"particles:{Id}:{_settings.RenderMode}:{_meshVersion}:{_particles.Count}";
        return new Mesh3D(positions, normals, indices, key, vertexColors0: colors);
    }

    private static void WriteCubeParticle(
        Vector3 center,
        float half,
        ColorRgba color,
        Vector3[] positions,
        Vector3[] normals,
        ColorRgba[] colors,
        int[] indices,
        ref int vertex,
        ref int index)
    {
        var corners = new[]
        {
            center + new Vector3(-half, -half, -half),
            center + new Vector3( half, -half, -half),
            center + new Vector3( half,  half, -half),
            center + new Vector3(-half,  half, -half),
            center + new Vector3(-half, -half,  half),
            center + new Vector3( half, -half,  half),
            center + new Vector3( half,  half,  half),
            center + new Vector3(-half,  half,  half)
        };

        WriteFace(corners[4], corners[5], corners[6], corners[7], Vector3.UnitZ, color, positions, normals, colors, indices, ref vertex, ref index);
        WriteFace(corners[1], corners[0], corners[3], corners[2], -Vector3.UnitZ, color, positions, normals, colors, indices, ref vertex, ref index);
        WriteFace(corners[0], corners[4], corners[7], corners[3], -Vector3.UnitX, color, positions, normals, colors, indices, ref vertex, ref index);
        WriteFace(corners[5], corners[1], corners[2], corners[6], Vector3.UnitX, color, positions, normals, colors, indices, ref vertex, ref index);
        WriteFace(corners[3], corners[7], corners[6], corners[2], Vector3.UnitY, color, positions, normals, colors, indices, ref vertex, ref index);
        WriteFace(corners[0], corners[1], corners[5], corners[4], -Vector3.UnitY, color, positions, normals, colors, indices, ref vertex, ref index);
    }

    private static void WriteFace(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        ColorRgba color,
        Vector3[] positions,
        Vector3[] normals,
        ColorRgba[] colors,
        int[] indices,
        ref int vertex,
        ref int index)
    {
        positions[vertex + 0] = a;
        positions[vertex + 1] = b;
        positions[vertex + 2] = c;
        positions[vertex + 3] = d;
        normals[vertex + 0] = normal;
        normals[vertex + 1] = normal;
        normals[vertex + 2] = normal;
        normals[vertex + 3] = normal;
        colors[vertex + 0] = color;
        colors[vertex + 1] = color;
        colors[vertex + 2] = color;
        colors[vertex + 3] = color;
        indices[index + 0] = vertex + 0;
        indices[index + 1] = vertex + 1;
        indices[index + 2] = vertex + 2;
        indices[index + 3] = vertex + 0;
        indices[index + 4] = vertex + 2;
        indices[index + 5] = vertex + 3;
        vertex += 4;
        index += 6;
    }

    private void Prewarm()
    {
        var steps = global::System.Math.Max(1, (int)MathF.Ceiling(_settings.ParticleLifetimeSeconds * 12f));
        var dt = _settings.ParticleLifetimeSeconds / steps;
        for (var i = 0; i < steps; i++) Advance(dt);
    }

    private void SpawnParticle()
    {
        if (_particles.Count >= global::System.Math.Max(1, _settings.Capacity)) return;
        _particles.Add(_emitter.Create(_settings));
    }

    private void TrimToCapacity()
    {
        var capacity = global::System.Math.Max(1, _settings.Capacity);
        while (_particles.Count > capacity)
        {
            _particles.RemoveAt(_particles.Count - 1);
        }
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        => value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static ColorRgba Lerp(ColorRgba a, ColorRgba b, float t)
        => new(
            Lerp(a.R, b.R, t),
            Lerp(a.G, b.G, t),
            Lerp(a.B, b.B, t),
            Lerp(a.A, b.A, t));
}
