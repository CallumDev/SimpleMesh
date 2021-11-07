using System;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SampleOpenTK
{
    public class Render2D
    {
        private const string VERTEX = @"
#version 120
uniform mat4 viewprojection;

attribute vec2 v_position;
attribute vec2 v_texture1;

varying vec2 texcoord;
void main()
{
    gl_Position = viewprojection * vec4(v_position, 0.0, 1.0);
    texcoord = vec2(v_texture1.x, v_texture1.y);
}
";

        private const string FRAGMENT = @"
#version 120
uniform sampler2D texture;
varying vec2 texcoord;
void main()
{
    gl_FragColor = texture2D(texture, texcoord);
}

";
        private Shader shader;
        private int vp;
        private int vao;
        private int vbo;

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector2 Position;
            public Vector2 Texture;

            public Vertex(Vector2 pos, Vector2 tex)
            {
                Position = pos;
                Texture = tex;
            }
        }

        private Vertex[] vertices = new Vertex[1024];
        private int vCount = 0;
        private int texture;
        public unsafe Render2D()
        {
            //load texture
            using var stream = typeof(Render2D).Assembly.GetManifestResourceStream("SampleOpenTK.SampleRenderer.LiberationSans_0.png");
            using (var image = Image.Load(stream))
            {
                texture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, texture);
                using var rgba32 = image.CloneAs<Rgba32>();
                var contiguous = rgba32.TryGetSinglePixelSpan(out var pixels);
                fixed (Rgba32* ptr = &pixels.GetPinnableReference())
                {
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
                }
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);

            }
            shader = new Shader(VERTEX, FRAGMENT);
            GL.UseProgram(shader.ID);
            vp = shader.GetLocation("viewprojection");
            GL.Uniform1(shader.GetLocation("texture"), 0);
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(4 * sizeof(float) * 1024), IntPtr.Zero, BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2,  VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        }
        
        public void Start(int width, int height)
        {
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.UseProgram(shader.ID);
            var m = Matrix4.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);
            GL.UniformMatrix4(vp, false, ref m);
        }

        public Vector2i MeasureString(string text)
        {
            int dX = 0, dY = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    dX += LiberationSans.Glyphs[' '].XAdvance;
                }
                else if (text[i] == '\t')
                {
                    dX += LiberationSans.Glyphs[' '].XAdvance * 4;
                }
                else if (text[i] == '\n')
                {
                    dX = 0;
                    dY += LiberationSans.LineHeight;
                }
                else
                {
                    LiberationSans.Glyph glyph;
                    if (!LiberationSans.Glyphs.TryGetValue(text[i], out glyph))
                        glyph = LiberationSans.Glyphs['?'];
                    dX += glyph.XAdvance;
                }
            }
            if (dX != 0) dY += LiberationSans.LineHeight;
            return new Vector2i(dX, dY);
        }
        public void DrawString(string text, int x, int y)
        {
            int dX = x, dY = y;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    dX += LiberationSans.Glyphs[' '].XAdvance;
                } else if (text[i] == '\t')
                {
                    dX += LiberationSans.Glyphs[' '].XAdvance * 4;
                } 
                else if (text[i] == '\n')
                {
                    dX = (int) x;
                    dY += LiberationSans.LineHeight;
                }
                else
                {
                    LiberationSans.Glyph glyph;
                    if (!LiberationSans.Glyphs.TryGetValue(text[i], out glyph))
                        glyph = LiberationSans.Glyphs['?'];
                    AddQuad(glyph.Source, dX + glyph.XOffset, dY + glyph.YOffset);
                    dX += glyph.XAdvance;
                }
            }
        }

        public void FillBackground(int x, int y, int width, int height)
        {
            var tl = new Vertex(
                new Vector2(x, y),
                new Vector2(1,0)
            );
            var tr = new Vertex(
                new Vector2(x + width, y),
                new Vector2(1,0)
            );
            var bl = new Vertex(
                new Vector2(x, y + height),
                new Vector2(1,0)
            );
            var br = new Vertex(
                new Vector2(x + width, y + height),
                new Vector2(1,0)
            );
            vertices[vCount++] = tl;
            vertices[vCount++] = tr;
            vertices[vCount++] = bl;
            vertices[vCount++] = bl;
            vertices[vCount++] = tr;
            vertices[vCount++] = br;
        }

        void AddQuad(Rectangle source, int x, int y)
        {
            var tl = new Vertex(
                new Vector2(x, y),
                new Vector2(source.X / LiberationSans.SIZE, source.Y / LiberationSans.SIZE)
            );
            var tr = new Vertex(
                new Vector2(x + source.Width, y),
                new Vector2((source.X + source.Width) / LiberationSans.SIZE, source.Y / LiberationSans.SIZE)
            );
            var bl = new Vertex(
                new Vector2(x, y + source.Height),
                new Vector2(source.X / LiberationSans.SIZE, (source.Y + source.Height) / LiberationSans.SIZE)
            );
            var br = new Vertex(
                new Vector2(x + source.Width, y + source.Height),
                new Vector2((source.X + source.Width) / LiberationSans.SIZE, (source.Y + source.Height) / LiberationSans.SIZE)
            );
            vertices[vCount++] = tl;
            vertices[vCount++] = tr;
            vertices[vCount++] = bl;
            vertices[vCount++] = bl;
            vertices[vCount++] = tr;
            vertices[vCount++] = br;
        }

        public void Finish()
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vCount * sizeof(float) * 4, vertices);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.DrawArrays(PrimitiveType.Triangles, 0, vCount);
            vCount = 0;
        }
        
    }
}