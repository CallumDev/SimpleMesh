using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SampleOpenTK;

public static class Texture
{
    public static unsafe int Load(Stream stream)
    {
        using (var image = Image.Load(stream))
        {
            var texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            using var rgba32 = image.CloneAs<Rgba32>();
            Span<Rgba32> pixels = new Rgba32[image.Width * image.Height];
            rgba32.CopyPixelDataTo(pixels);
            fixed (Rgba32* ptr = &pixels.GetPinnableReference())
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);
            return texture;
        }
    }
}