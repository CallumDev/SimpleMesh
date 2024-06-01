using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Vector2i = OpenTK.Mathematics.Vector2i;
using Color4 = OpenTK.Mathematics.Color4;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SimpleMesh;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SampleOpenTK
{
    partial class MainWindow : GameWindow
    {
        private Render2D text;
        public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        //Handle Input
        class Button
        {
            public string Text;
            public int X;
            public int Y;
            public Action Clicked;

            public Button(string text, int x, int y, Action clicked)
            {
                Text = text;
                X = x;
                Y = y;
                Clicked = clicked;
            }
        }
        private List<Button> buttons = new List<Button>();
        
        private Shader diffuseShader;
        private Shader pbrShader;
        private AnimationHandler animations;

        private Button openButton;
        private Button saveButton;
        private Button saveGltfButton;
        private Button pbrButton;
        
        private Dictionary<string, int> textures = new Dictionary<string, int>();
        private int nullTexture;
        
        protected override unsafe void OnLoad()
        {
            base.OnLoad();
            diffuseShader = new Shader(VERTEX_SHADER, DIFFUSE_FRAGMENT_SHADER);
            diffuseShader.SetI("mat_texture", 0);
            diffuseShader.SetI("mat_emissiveTexture", 1);
            diffuseShader.SetI("mat_normalTexture", 2);
            pbrShader = new Shader(VERTEX_SHADER, PBR_FRAGMENT_SHADER);
            pbrShader.SetI("mat_texture", 0);
            pbrShader.SetI("mat_emissiveTexture", 1);
            pbrShader.SetI("mat_normalTexture", 2);
            pbrShader.SetI("mat_metallicRoughnessTexture", 3);
            //Setup null texture
            nullTexture = GL.GenTexture();
            uint white = 0xFFFFFFFF;
            GL.BindTexture(TextureTarget.Texture2D, nullTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1,1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)(&white));
            
            openButton = new Button("Open...", 5, 5, () =>
            {
                openfile = FilePicker.OpenFile();
                if (openfile != null)
                {
                    try
                    {
                        LoadModel(openfile);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                    
            });
            saveButton = new Button("Save To Binary", 5, 40, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, false);
            });
            saveGltfButton = new Button("Save To GLB", 5, 75, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, true);
            });

            pbrButton = new Button("PBR Enabled", 5, 110, () =>
            {
                pbrEnabled = !pbrEnabled;
                pbrButton.Text = pbrEnabled ? "PBR Enabled" : "PBR Disabled";
            });
            
            buttons = new List<Button>(new[] {openButton, saveButton, saveGltfButton});
            text = new Render2D();
        }

        private Button down = null;
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.Button != MouseButton.Left) return;
            down = null;
            foreach (var b in buttons)
            {
                var bSz = text.MeasureString(b.Text) + new Vector2i(4, 4);
                var rect = new Rectangle(b.X, b.Y, bSz.X, bSz.Y);
                if (rect.Contains((int) MousePosition.X, (int) MousePosition.Y))
                {
                    down = b;
                    break;
                }
            }
        }
        
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.Button != MouseButton.Left) return;
            foreach (var b in buttons)
            {
                var bSz = text.MeasureString(b.Text) + new Vector2i(4, 4);
                var rect = new Rectangle(b.X, b.Y, bSz.X, bSz.Y);
                if (rect.Contains((int) MousePosition.X, (int) MousePosition.Y))
                {
                    if (down == b) b.Clicked();
                    down = null;
                    break;
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            zoom += e.OffsetY / 8.0f;
        }

        private float rotateY = 0;
        private float rotateX = 0;
        

        private Model model;

        private int vao;
        private int vbo;
        private int ebo;

        private static readonly int VERTEX_SIZE = Marshal.SizeOf<Vertex>();

        void SaveModel(string filename, bool gltf)
        {
            if (model == null) return;
            using var stream = File.Create(filename);
            model.SaveTo(stream, gltf ? ModelSaveFormat.GLB : ModelSaveFormat.SMesh);
        }

        void LoadModel(string filename)
        {
            animations = new AnimationHandler();
            var sw = Stopwatch.StartNew();
            model = Model.FromFile(filename)
                .AutoselectRoot(out _) //try discard empty nodes at root (think blender cameras etc.)
                .CalculateTangents(false, true) //Calculate missing tangents
                .CalculateBounds(); //required for viewing purposes
            sw.Stop();
            openfile = $"{filename} ({sw.Elapsed.TotalMilliseconds:F2}ms)";
            //Unbind vao so we don't delete the active one
            GL.BindVertexArray(0);
            if (vbo != 0)
            {
                GL.DeleteVertexArray(vao);
                GL.DeleteBuffer(ebo);
                GL.DeleteBuffer(vbo);
            }
            foreach(var tex in textures)
                GL.DeleteTexture(tex.Value);
            textures = new(StringComparer.OrdinalIgnoreCase);

            if (model.Images != null)
            {
                foreach (var kv in model.Images)
                {
                    if (!textures.ContainsKey(kv.Key))
                    {
                        var tex = Texture.Load(new MemoryStream(kv.Value.Data.ToArray()));
                        textures[kv.Key] = tex;
                    }
                }
            }

            zoom = 1;
            //Calculate size of buffers
            int vCount = 0;
            int iCount = 0;
            bool isIdx32 = false;
            foreach (var g in model.Geometries) {
                vCount += g.Vertices.Length;
                iCount += g.Indices.Length;
                if (g.Indices.Indices32 != null) isIdx32 = true;
            }
            //Copy from Geometry objects into one large buffer to upload to OpenGL
            Vertex[] vertices = new Vertex[vCount];
            ushort[] idx16 = isIdx32 ? (ushort[]) null : new ushort[iCount];
            uint[] idx32 = isIdx32 ? new uint[iCount] : (uint[]) null;
            vCount = iCount = 0;
            foreach (var g in model.Geometries)
            {
                //Store offset into OpenGL buffers in the UserTag field, allows us to render from a Model reference
                g.UserTag = new BufferOffset() {BaseVertex = vCount, StartIndex = iCount, Index32 = isIdx32};
                Array.Copy(g.Vertices, 0, vertices, vCount, g.Vertices.Length);
                if(idx16 != null)
                    Array.Copy(g.Indices.Indices16, 0, idx16, iCount, g.Indices.Indices16.Length);
                else
                {
                    if (g.Indices.Indices16 != null) {
                        for (int i = 0; i < g.Indices.Indices16.Length; i++)
                            idx32[iCount + i] = g.Indices.Indices16[i];
                    }
                    else {
                        Array.Copy(g.Indices.Indices32, 0, idx32, iCount, g.Indices.Indices32.Length);
                    }
                }
                vCount += g.Vertices.Length;
                iCount += g.Indices.Length;
            }

            //Upload data to OpenGL
            //In a real application, you would pack these vertex buffers tightly after reading
            vao = GL.GenVertexArray();
            ebo = GL.GenBuffer();
            vbo = GL.GenBuffer();
            
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, VERTEX_SIZE * vertices.Length, vertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);
            GL.EnableVertexAttribArray(3);
            GL.EnableVertexAttribArray(4);
            GL.EnableVertexAttribArray(5);
            GL.EnableVertexAttribArray(6);
            GL.EnableVertexAttribArray(7);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VERTEX_SIZE, 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, VERTEX_SIZE, 3 * sizeof(float));
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, VERTEX_SIZE, 6 * sizeof(float));
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, VERTEX_SIZE, 10 * sizeof(float));
            GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE, 14 * sizeof(float));
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE, 16 * sizeof(float));
            GL.VertexAttribPointer(6, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE, 18 * sizeof(float));
            GL.VertexAttribPointer(7, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE, 20 * sizeof(float));
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            if (isIdx32)
                GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(uint) * idx32.Length, idx32,
                    BufferUsageHint.StaticDraw);
            else
                GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(ushort) * idx16.Length, idx16, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);

            hasPbr = pbrEnabled = model.Materials.Any(x => x.Value.MetallicRoughness);
            if (hasPbr)
            {
                pbrButton.Text = "PBR Enabled";
                buttons = new List<Button>(new[] { openButton, saveButton, saveGltfButton, pbrButton });
            }
            else
                buttons = new List<Button>(new[] {openButton, saveButton, saveGltfButton});
            int y = 5;
            if (model.Animations != null)
            {
                foreach (var anim in model.Animations)
                {
                    buttons.Add(new Button(anim.Name ?? "ANIMATION", 200, y,
                        () => { animations.Instances.Add(new AnimationInstance() {Animation = anim}); }));
                    y += 35;
                }
            }

            modelRadius = 0;
            for (int i = 0; i < model.Roots.Length; i++) {
                GetRadius(model.Roots[i], Matrix4x4.Identity, ref modelRadius);
            }

            if (modelRadius <= 0)
                modelRadius = 1;
        }

        class BufferOffset
        {
            public int BaseVertex;
            public int StartIndex;
            public bool Index32;
        }

        private bool pbrEnabled = true;
        private bool hasPbr = false;
        
        //Unsafe required to use System.Numerics under OpenTK
        unsafe void DrawNode(ModelNode node, Matrix4x4 parent)
        {
            var (animTranslate, animRotate) = animations.GetAnimated(node.Name, out var tr, out var rot);
            Matrix4x4 selfTransform;
            Matrix4x4.Decompose(node.Transform, out _, out var nodeRotate, out var nodeTranslate);
            if (animTranslate && !animRotate)
                selfTransform = Matrix4x4.CreateFromQuaternion(nodeRotate) * Matrix4x4.CreateTranslation(tr);
            else if (animRotate && !animTranslate)
                selfTransform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(nodeTranslate);
            else if (animTranslate && animRotate)
                selfTransform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(tr);
            else
                selfTransform = node.Transform;
            //
            void BindTexture(TextureInfo tex, TextureUnit unit)
            {
                GL.ActiveTexture(unit);
                if (tex == null ||
                    string.IsNullOrWhiteSpace(tex.Name) ||
                    !textures.TryGetValue(tex.Name, out var pbrTex))
                {
                    GL.BindTexture(TextureTarget.Texture2D, nullTexture);
                }
                else
                {
                    GL.BindTexture(TextureTarget.Texture2D, pbrTex);
                }
            }
            var mymat = selfTransform * parent;
            if (node.Geometry != null) //some models will have empty nodes purely for transforms
            {
                Matrix4x4.Invert(mymat, out var normalmat);
                normalmat = Matrix4x4.Transpose(normalmat);
                
                diffuseShader.Set("world", mymat);
                diffuseShader.Set("normalmat", normalmat);
                
                pbrShader.Set("world", mymat);
                pbrShader.Set("normalmat", normalmat);
                var off = (BufferOffset) node.Geometry.UserTag;
                
                foreach (var tg in node.Geometry.Groups)
                {
                    var isPbr = (tg.Material.MetallicRoughness && pbrEnabled);
                    Shader sh = isPbr
                        ? pbrShader
                        : diffuseShader;
                    sh.Use();
                    sh.Set("mat_diffuse", tg.Material.DiffuseColor.X, tg.Material.DiffuseColor.Y,
                        tg.Material.DiffuseColor.Z, 1);
                    sh.Set("mat_emissive", tg.Material.EmissiveColor.X, tg.Material.EmissiveColor.Y,
                        tg.Material.EmissiveColor.Z);
                    if (isPbr)
                    {
                        sh.SetF("mat_roughness", tg.Material.RoughnessFactor);
                        sh.SetF("mat_metallic", tg.Material.MetallicFactor);
                        sh.SetI("texcoord_metallicRoughness", tg.Material.MetallicRoughnessTexture?.CoordinateIndex ?? 0);
                        BindTexture(tg.Material.MetallicRoughnessTexture, TextureUnit.Texture3);
                    }
                    BindTexture(tg.Material.NormalTexture, TextureUnit.Texture2);
                    sh.SetI("texcoord_normal", tg.Material.NormalTexture?.CoordinateIndex ?? 0);
                    sh.SetI("mat_normalMap", tg.Material.NormalTexture != null ? 1 : 0);
                    BindTexture(tg.Material.EmissiveTexture, TextureUnit.Texture1);
                    sh.SetI("texcoord_diffuse", tg.Material.DiffuseTexture?.CoordinateIndex ?? 0);
                    sh.SetI("texcoord_emissive", tg.Material.EmissiveTexture?.CoordinateIndex ?? 0);
                    BindTexture(tg.Material.DiffuseTexture, TextureUnit.Texture0);
                    GL.DrawElementsBaseVertex(
                        node.Geometry.Kind == GeometryKind.Lines ? BeginMode.Lines : BeginMode.Triangles,
                        tg.IndexCount, off.Index32 ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort,
                        (IntPtr) ((tg.StartIndex + off.StartIndex) * (off.Index32 ? 4 : 2)),
                        off.BaseVertex + tg.BaseVertex
                    );
                }
            }
            foreach (var child in node.Children)
            {
                DrawNode(child, mymat);
            }
        }

        private float zoom = 1;
        private float modelRadius = 1;

        private string openfile = null;

        //Find first model node we can find. Used for zoom when empty parent object used
        void GetRadius(ModelNode m, Matrix4x4 matrix, ref float radius)
        {
            var tr = m.Transform * matrix;
            if (m.Geometry != null)
            {
                var d = System.Numerics.Vector3.Transform(System.Numerics.Vector3.Zero, tr).Length();
                if (d + m.Geometry.Radius > radius)
                    radius = (d + m.Geometry.Radius);
            }
            foreach (var child in m.Children)
            {
                GetRadius(child, tr, ref radius);
            }
        }
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            animations?.Update((float)args.Time);
            var sz = this.ClientSize;
            GL.Viewport(0, 0, sz.X, sz.Y);
            GL.ClearColor(Color4.Blue);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);

            if (IsKeyDown(Keys.Right))
            {
                rotateY += (float) args.Time * 2;
            }
            if (IsKeyDown(Keys.Left))
            {
                rotateY -= (float)args.Time * 2;
            }
            
            if (IsKeyDown(Keys.Up))
            {
                rotateX += (float) args.Time * 2;
            }
            if (IsKeyDown(Keys.Down))
            {
                rotateX -= (float)args.Time * 2;
            }
            
            if (model != null)
            {
                
                GL.BindVertexArray(vao);
                //generate camera matrix based off model dimensions
                
                var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), (float) Size.X / Size.Y,
                    0.02f, 10000);
                var campos = new Vector3(0, 0, (-modelRadius * 4) * zoom);
                var view = Matrix4.LookAt(campos, Vector3.Zero, Vector3.UnitY);
                var vp = view * projection;
                diffuseShader.Set("viewprojection", vp);
                var mscale = modelRadius / 500.0f;
                if (mscale < 1)
                    mscale = 1;
                var lp = new Vector3(0.0f, -10.0f, -1000.0f * mscale);
                diffuseShader.Set("viewprojection", vp);
                diffuseShader.Set("light_direction", -0.49999f, 0.707107f, 0.5f);
                pbrShader.Set("viewprojection", vp);
                pbrShader.Set("light_direction", 0.0f, 0.5f, -0.5f);
                pbrShader.Set("camera_pos", campos.X, campos.Y, campos.Z);
                //draw starting at first root node.
                for (int i = 0; i < model.Roots.Length; i++)
                {
                    DrawNode(model.Roots[i], Matrix4x4.CreateRotationY(rotateY) * Matrix4x4.CreateRotationX(rotateX));
                }
                //reset texture state
                GL.ActiveTexture(TextureUnit.Texture0);
            }
            //Draw UI
            text.Start(sz.X, sz.Y);
            foreach (var b in buttons)
            {
                var bSz = text.MeasureString(b.Text) + new Vector2i(4, 4);
                text.FillBackground(b.X, b.Y, bSz.X, bSz.Y);
                text.DrawString(b.Text, b.X + 2, b.Y + 2);
            }
            text.DrawString("Mouse Wheel - Zoom, Keyboard Up/Down/Left/Right - Rotate", 5, sz.Y - 60);
            if(openfile != null)
                text.DrawString(openfile, 5, sz.Y - 30);
            
            text.Finish();
            SwapBuffers();
        }

        static void Main(string[] args)
        {
            var settings = NativeWindowSettings.Default;
            settings.ClientSize = new Vector2i(1024, 768);
            settings.Vsync = VSyncMode.On;
            new MainWindow(GameWindowSettings.Default, settings).Run();
        }
    }
}