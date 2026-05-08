using Avalonia.OpenGL;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed partial class OpenGlSceneRenderer
{
    private void UploadSkyboxTexture(GlInterface gl, Skybox3D skybox, RenderStats stats)
    {
        if (skybox.Mode != SkyboxMode3D.Equirectangular || !skybox.HasEquirectangularTexture || string.IsNullOrWhiteSpace(skybox.EquirectangularTextureKey) || skybox.EquirectangularTextureData is not { Length: > 0 })
        {
            UploadFloat(_uniform1f, _skyboxTextureEnabledLocation, 0f);
            return;
        }

        var resource = EnsureMaterialTexture(gl, skybox.EquirectangularTextureKey, skybox.EquirectangularTextureData, skybox.EnvironmentTextureVersion, GlTexture6, stats);
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

        var samplerLocations = new[] { _skyboxPXLocation, _skyboxNXLocation, _skyboxPYLocation, _skyboxNYLocation, _skyboxPZLocation, _skyboxNZLocation };
        var textureUnits = new[] { GlTexture0, GlTexture1, GlTexture2, GlTexture3, GlTexture4, GlTexture5 };
        for (var i = 0; i < 6; i++)
        {
            var key = skybox.CubemapTextureKeys[i];
            var data = skybox.CubemapTextureData[i];
            if (string.IsNullOrWhiteSpace(key) || data is not { Length: > 0 })
            {
                UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 0f);
                return;
            }

            var resource = EnsureMaterialTexture(gl, key, data, skybox.EnvironmentTextureVersion, textureUnits[i], stats);
            if (resource is null)
            {
                UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 0f);
                return;
            }

            gl.ActiveTexture(textureUnits[i]);
            gl.BindTexture(GlTexture2D, resource.TextureId);
            if (samplerLocations[i] >= 0) _uniform1i?.Invoke(samplerLocations[i], i);
        }

        UploadFloat(_uniform1f, _skyboxCubemapEnabledLocation, 1f);
        gl.ActiveTexture(GlTexture0);
    }

}
