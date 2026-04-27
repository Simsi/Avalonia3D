using System.Numerics;

namespace ThreeDEngine.Core.HighScale;

public struct InstanceRecord3D
{
    public int TemplateId;
    public Matrix4x4 Transform;
    public int MaterialVariantId;
    public int DataId;
    public InstanceFlags3D Flags;
    public int TransformVersion;
    public int MaterialVersion;
}
