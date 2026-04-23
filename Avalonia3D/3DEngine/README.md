# 3DEngine

Source-only mini-framework for simple 3D scenes inside Avalonia.

## What is implemented

- `Scene3DControl` host control.
- `Rectangle3D` and `Ellipse3D` with actual extrusion via `Depth`.
- Camera orbit / pan / zoom.
- Picking and click/hover/drag for mesh objects.
- Scene background color via `Scene3D.BackgroundColor`.
- Interactive live controls projected into the scene as billboard-style sprites.

## Basic usage

```csharp
var sceneControl = new Scene3DControl();
sceneControl.Scene.BackgroundColor = ColorRgba.White;
sceneControl.Scene.Camera.Position = new Vector3(0, 0, -8);
sceneControl.Scene.Camera.Target = Vector3.Zero;

var rect = new Rectangle3D
{
    Width = 2.2f,
    Height = 1.2f,
    Depth = 0.4f,
    Position = new Vector3(-1.5f, 0f, 0f),
    Color = new ColorRgba(0.9f, 0.25f, 0.25f, 1f)
};

sceneControl.Add(rect);
```

## Interactive live control sprite

```csharp
var panel = new MyUserControl();
var sprite = sceneControl.AddLiveControl(panel);
sprite.Position = new Vector3(0f, 1.5f, 1.5f);
sprite.Width = 3.0f;
sprite.Height = 1.8f;
sprite.AlwaysFaceCamera = true;
```

The live control is hosted off-screen, rendered to a bitmap, and drawn as a plane inside the scene. Pointer and keyboard input are routed back to the hidden Avalonia control so its normal handlers continue to run.


## Browser static asset

For Avalonia Browser/WASM, ensure `3DEngine/WebGL/mini3d.webgl.js` is copied to your web output, for example under `wwwroot/3DEngine/mini3d.webgl.js`.
