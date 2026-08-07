using System.Numerics;
using System.Runtime.InteropServices;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuDrivenVertex3D
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
    public Vector4 Tangent;
    public Vector4 Color;
    public float MaterialSlot;
    public Vector4 BoneIndices;
    public Vector4 BoneWeights;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSceneObjectRecord3D
{
    public Matrix4x4 Model;
    public Vector4 BoundingSphere;
    public uint MeshIndex;
    public uint MaterialIndex;
    public uint Flags;
    public uint SkinPaletteOffset;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuMeshRecord3D
{
    public uint VertexCount;
    public uint IndexCount;
    public uint MeshletOffset;
    public uint MeshletCount;
    public uint IndexElementSize;
    public uint VertexStride;
    public uint Reserved0;
    public uint Reserved1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuMeshletRecord3D
{
    public Vector4 BoundingSphere;
    public Vector4 NormalCone;
    public uint VertexOffset;
    public uint VertexCount;
    public uint TriangleOffset;
    public uint TriangleCount;
    public uint MeshIndex;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuMaterialRecord3D
{
    public Vector4 BaseColor;
    public Vector4 EmissiveMetallic;
    public Vector4 SurfaceParameters;
    public Vector4 TextureIndices;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuDirectionalLightRecord3D
{
    public Vector4 DirectionIntensity;
    public Vector4 ColorEnabled;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPointLightRecord3D
{
    public Vector4 PositionRange;
    public Vector4 ColorIntensity;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSpotLightRecord3D
{
    public Vector4 PositionRange;
    public Vector4 DirectionInnerCos;
    public Vector4 ColorIntensity;
    public Vector4 OuterCosEnabled;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuFrameConstants3D
{
    public Matrix4x4 View;
    public Matrix4x4 Projection;
    public Matrix4x4 ViewProjection;
    public Vector4 CameraPositionTime;
    public Vector4 ViewportAndInverse;
    public Vector4 Counts;
    public Vector4 LightCounts;
    public Vector4 ClusterDimensions;
    public Vector4 FeatureFlags;
    public Vector4 Timing;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuDrawIndexedIndirectCommand3D
{
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public int BaseVertex;
    public uint FirstInstance;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleEmitterRecord3D
{
    public Matrix4x4 Model;
    public Vector4 DirectionEmissionRate;
    public Vector4 GravityLifetime;
    public Vector4 SizeSpeedSpread;
    public Vector4 StartColor;
    public Vector4 EndColor;
    public uint StateOffset;
    public uint Capacity;
    public uint Flags;
    public uint RandomSeed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleStateRecord3D
{
    public Vector4 PositionAge;
    public Vector4 VelocityLifetime;
    public Vector4 Color;
    public Vector4 SizeRotationFlags;
}
