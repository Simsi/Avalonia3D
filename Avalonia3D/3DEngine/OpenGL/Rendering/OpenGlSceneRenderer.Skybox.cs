using Avalonia.OpenGL;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed partial class OpenGlSceneRenderer
{
    private void UploadSkyboxTexture(GlInterface gl, Skybox3D skybox, RenderStats stats)
    {
        var texture = skybox.Mode == SkyboxMode3D.Equirectangular ? skybox.EquirectangularTextureInternal : null;
        if (texture is null)
        {
            UploadFloat(_uniform1f, _skyboxTextureEnabledLocation, 0f);
            return;
        }

        var resource = EnsureMaterialTexture(gl, texture, GlTexture6, stats);
        if (resource is null)
        {
            UploadFloat(_uniform1f, _skyboxTextureEnabledLocation, 0f);
            return;
        }

        gl.ActiveTexture(GlTexture6);
        gl.BindTexture(GlTexture2D, resource.TextureId);
        if (_skyboxTextureLocation >= 0) _uniform1i?.Invoke(_skyboxTextureLocation, 6);
        UploadFloat(_uniform1f, _skyboxTextureEnabledLocation, 1f);
        gl.ActiveTexture(GlTexture0);
    }

    private void UploadSkyboxCubemapTextures(GlInterface gl, Skybox3D skybox, RenderStats stats)
    {
        if (skybox.Mode != SkyboxMode3D.Cubemap || !skybox.HasCubemapTextures)
        {
            UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 0f);
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            var texture = skybox.CubemapTexturesInternal[i];
            if (texture is null)
            {
                UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 0f);
                return;
            }

            var textureUnit = GlTexture0 + i;
            var samplerLocation = i switch
            {
                0 => _skyboxPXLocation,
                1 => _skyboxNXLocation,
                2 => _skyboxPYLocation,
                3 => _skyboxNYLocation,
                4 => _skyboxPZLocation,
                _ => _skyboxNZLocation
            };
            var resource = EnsureMaterialTexture(gl, texture, textureUnit, stats);
            if (resource is null)
            {
                UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 0f);
                return;
            }

            gl.ActiveTexture(textureUnit);
            gl.BindTexture(GlTexture2D, resource.TextureId);
            if (samplerLocation >= 0) _uniform1i?.Invoke(samplerLocation, i);
        }

        UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 1f);
        gl.ActiveTexture(GlTexture0);
    }
}
