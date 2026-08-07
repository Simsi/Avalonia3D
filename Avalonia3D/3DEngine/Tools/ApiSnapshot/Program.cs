#if AVALONIA3D_API_SNAPSHOT_TOOL
using System;
using System.IO;
using System.Text;
using ThreeDEngine;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Avalonia.OpenGL;
using ThreeDEngine.Avalonia.Preview;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Importers.Gltf;
using ThreeDEngine.Core.Physics.Jitter2;

var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("Artifacts", "public-api.txt"));
var directory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

var assemblies = new[]
{
    typeof(ApiSurfaceSnapshot3D).Assembly,
    typeof(GltfModelImporter).Assembly,
    typeof(Jitter2PhysicsCore).Assembly,
    typeof(Scene3DControl).Assembly,
    typeof(OpenGlEngineBuilderExtensions3D).Assembly,
    typeof(Scene3DPreviewControl).Assembly,
    typeof(Engine3DApplication3D).Assembly
};
File.WriteAllText(outputPath, ApiSurfaceSnapshot3D.Capture(assemblies), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine(outputPath);

#endif
