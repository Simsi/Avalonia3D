using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace ThreeDEngine.Avalonia.Rendering;

internal readonly record struct DecodedTexture3D(int Width, int Height, byte[] RgbaPixels)
{
    public int ByteLength => RgbaPixels?.Length ?? 0;
}

internal static class TextureDecodeHelper3D
{
    private const int MaxTextureDimension = 4096;

    public static bool TryDecodeRgba(byte[]? encoded, out DecodedTexture3D decoded, out string error)
    {
        decoded = default;
        error = string.Empty;
        if (encoded is null || encoded.Length == 0)
        {
            error = "Texture payload is empty.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            using var bitmap = new Bitmap(stream);
            var width = bitmap.PixelSize.Width;
            var height = bitmap.PixelSize.Height;
            if (width <= 0 || height <= 0)
            {
                error = "Decoded texture has invalid dimensions.";
                return false;
            }
            if (width > MaxTextureDimension || height > MaxTextureDimension)
            {
                error = $"Decoded texture {width}x{height} exceeds {MaxTextureDimension}x{MaxTextureDimension}.";
                return false;
            }

            var stride = width * 4;
            var bufferSize = stride * height;
            var bgraPixels = new byte[bufferSize];
            unsafe
            {
                fixed (byte* ptr = bgraPixels)
                {
                    bitmap.CopyPixels(new PixelRect(0, 0, width, height), (IntPtr)ptr, bufferSize, stride);
                }
            }

            byte[] rgbaPixels;
            if (OperatingSystem.IsBrowser())
            {
                // Avalonia Browser already exposes Bitmap.CopyPixels data in RGBA order for WebAssembly-backed
                // bitmaps. Applying the desktop BGRA->RGBA conversion again swaps red/blue channels, which made
                // blue desktop materials/textures appear orange in WebGL.
                rgbaPixels = bgraPixels;
            }
            else
            {
                rgbaPixels = new byte[bufferSize];
                for (var i = 0; i < bufferSize; i += 4)
                {
                    rgbaPixels[i + 0] = bgraPixels[i + 2];
                    rgbaPixels[i + 1] = bgraPixels[i + 1];
                    rgbaPixels[i + 2] = bgraPixels[i + 0];
                    rgbaPixels[i + 3] = bgraPixels[i + 3];
                }
            }

            decoded = new DecodedTexture3D(width, height, rgbaPixels);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
