using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Vector2i = OpenTK.Mathematics.Vector2i;
using Color4 = OpenTK.Mathematics.Color4;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SampleRenderer;
using SimpleMesh;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace SampleOpenTK
{
    partial class MainWindow : GameWindow
    {
        private Shader diffuseShader = null!;
        private Shader pbrShader = null!;
        private Render2D text = null!;

        private List<Button> buttons;
        private Button openButton;
        private Button saveButton;
        private Button saveGltfButton;
        private Button saveColladaButton;
        private Button pbrButton;
        private Button removeScale;

        private Dictionary<string, int> textures = new Dictionary<string, int>();
        private int nullTexture;

        private string? openfile = null;
        private Model? model;
        private ModelInstance? instance;

        private float rotateY = 0;
        private float rotateX = 0;

        private float zoom = 1;
        private float modelRadius = 1;
        private Dictionary<VertexAttributes, VertexBuffer> buffers = new();

        private bool pbrEnabled = true;
        private bool hasPbr = false;

        public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(
            gameWindowSettings, nativeWindowSettings)
        {

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
                    SaveModel(savefile, ModelSaveFormat.SMesh);
            });
            saveGltfButton = new Button("Save To GLB", 5, 75, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, ModelSaveFormat.GLB);
            });
            saveColladaButton = new Button("Save To Collada", 5, 110, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, ModelSaveFormat.Collada);
            });

            pbrButton = new Button("PBR Enabled", 5, 145, () =>
            {
                pbrEnabled = !pbrEnabled;
                pbrButton!.Text = pbrEnabled ? "PBR Enabled" : "PBR Disabled";
            });

            removeScale = new Button("Remove Scale", 350, 5, () =>
            {
                model = model!.ApplyScale(out var success);
                Console.WriteLine($"Remove Scale result = {success}");
                UseModel(model!);
            });

            buttons = [openButton];
        }


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
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)(&white));
            //text renderer
            text = new Render2D();
        }


        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            foreach (var b in buttons)
                b.OnMouseDown(e, MousePosition, text);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            foreach (var b in buttons)
                b.OnMouseUp(e, MousePosition, text);
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            zoom += e.OffsetY / 8.0f;
        }



        void SaveModel(string filename, ModelSaveFormat format)
        {
            if (model == null) return;
            using var stream = File.Create(filename);
            model!.Generator = "SimpleMesh Demo";
            model!.SaveTo(stream, format);
        }

        void UseModel(Model m)
        {
            foreach (var tex in textures)
                GL.DeleteTexture(tex.Value);
            textures = new(StringComparer.OrdinalIgnoreCase);

            if (m.Images != null)
            {
                foreach (var kv in m.Images)
                {
                    if (!textures.ContainsKey(kv.Key))
                    {
                        var tex = Texture.Load(new MemoryStream(kv.Value.Data.ToArray()));
                        textures[kv.Key] = tex;
                    }
                }
            }

            zoom = 1;

            //Unbind vao so we don't delete the active one
            GL.BindVertexArray(0);
            foreach (var v in buffers.Values)
                v.Dispose();
            buffers = new();
            foreach (var g in m.Geometries)
            {
                if (!buffers.TryGetValue(g.Vertices.Descriptor.Attributes, out var vb))
                {
                    vb = new VertexBuffer();
                    buffers[g.Vertices.Descriptor.Attributes] = vb;
                }
                g.UserTag = vb.Add(g);
            }

            foreach (var v in buffers.Values)
                v.Create();

            hasPbr = pbrEnabled = m.Materials.Any(x => x.Value.MetallicRoughness);
            if (hasPbr)
            {
                pbrButton.Text = "PBR Enabled";
                buttons = new List<Button>(new[]
                    { openButton, saveButton, saveGltfButton, saveColladaButton, pbrButton });
            }
            else
                buttons = new List<Button>(new[] { openButton, saveButton, saveColladaButton, saveGltfButton });

            int y = 5;
            if (m.Animations != null)
            {
                foreach (var anim in m.Animations)
                {
                    buttons.Add(new Button(anim.Name ?? "ANIMATION", 200, y,
                        () => { instance!.Animator.Instances.Add(new AnimationInstance(anim)); }));
                    y += 35;
                }
            }

            modelRadius = 0;
            for (int i = 0; i < m.Roots.Length; i++)
            {
                GetRadius(m.Roots[i], Matrix4x4.Identity, ref modelRadius);
            }

            if (modelRadius <= 0)
                modelRadius = 1;

            model = m;
            instance = new ModelInstance(m);

            if(instance.HasScale)
                buttons.Add(removeScale);
        }

        void LoadModel(string filename)
        {
            var sw = Stopwatch.StartNew();
            List<string> warnings = [];
            var m = Model.FromFile(filename, warnings)
                .AutoselectRoot(out _) //try discard empty nodes at root (think blender cameras etc.)
                .MergeTriangleGroups() //merge drawcalls of same material in a Geometry
                .CalculateNormals() // Calculate missing normals
                .CalculateTangents(false, true) // Calculate missing tangents
                .CalculateBounds(); //required for viewing purposes
            sw.Stop();
            foreach(var w in warnings)
                Console.WriteLine(w);
            openfile = $"{filename} ({sw.Elapsed.TotalMilliseconds:F2}ms)";
            if (m.Generator != null)
            {
                openfile += $"\nGenerator: {m.Generator}";
            }

            if (m.Copyright != null)
            {
                openfile += $"\nCopyright: {m.Copyright}";
            }
            UseModel(m);
        }



        void BindTexture(TextureInfo? tex, TextureUnit unit)
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

        //Unsafe required to use System.Numerics under OpenTK
        unsafe void DrawNode(InstanceNode node, Matrix4x4 world, ref SkinInstance? boundSkin, ref int boundVao)
        {
            var mymat = node.Transform * world;
            var geo = node.Node.Geometry;
            if (geo != null) //some models will have empty nodes purely for transforms
            {
                Matrix4x4.Invert(mymat, out var normalmat);
                normalmat = Matrix4x4.Transpose(normalmat);

                diffuseShader.Set("world", mymat);
                diffuseShader.Set("normalmat", normalmat);

                pbrShader.Set("world", mymat);
                pbrShader.Set("normalmat", normalmat);
                var off = (BufferOffset)geo.UserTag!;
                if (boundVao != off.Buffer.VAO)
                {
                    GL.BindVertexArray(off.Buffer.VAO);
                    boundVao = off.Buffer.VAO;
                    diffuseShader.Set("UseDiffuse", off.Buffer.VertexArrays[0].Descriptor.Diffuse > 0);
                    pbrShader.Set("UseDiffuse", off.Buffer.VertexArrays[0].Descriptor.Diffuse > 0);
                }
                diffuseShader.Set("UseSkinning", node.Skin != null);
                pbrShader.Set("UseSkinning", node.Skin != null);
                if (node.Skin != null && node.Skin != boundSkin)
                {
                    boundSkin = node.Skin;
                    diffuseShader.SetSpan("Bones", node.Skin.Matrices);
                    pbrShader.SetSpan("Bones", node.Skin.Matrices);
                }


                foreach (var tg in geo.Groups)
                {
                    var isPbr = (tg.Material.MetallicRoughness && pbrEnabled);
                    Shader sh = isPbr
                        ? pbrShader
                        : diffuseShader;
                    sh.Use();
                    sh.Set("mat_diffuse", tg.Material.DiffuseColor.R, tg.Material.DiffuseColor.G,
                        tg.Material.DiffuseColor.B, tg.Material.DiffuseColor.A);
                    sh.Set("mat_emissive", tg.Material.EmissiveColor.X, tg.Material.EmissiveColor.Y,
                        tg.Material.EmissiveColor.Z);
                    if (isPbr)
                    {
                        sh.SetF("mat_roughness", tg.Material.RoughnessFactor);
                        sh.SetF("mat_metallic", tg.Material.MetallicFactor);
                        sh.SetI("texcoord_metallicRoughness",
                            tg.Material.MetallicRoughnessTexture?.CoordinateIndex ?? 0);
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
                        geo.Kind == GeometryKind.Lines ? PrimitiveType.Lines : PrimitiveType.Triangles,
                        tg.IndexCount, off.Buffer.IsIndex32 ? DrawElementsType.UnsignedInt : DrawElementsType.UnsignedShort,
                        (IntPtr)((tg.StartIndex + off.StartIndex) * (off.Buffer.IsIndex32 ? 4 : 2)),
                        off.BaseVertex + tg.BaseVertex
                    );
                }
            }

            foreach (var child in node.Children)
            {
                DrawNode(child, world, ref boundSkin, ref boundVao);
            }
        }

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
            var sz = this.ClientSize;
            GL.Viewport(0, 0, sz.X, sz.Y);
            GL.ClearColor(Color4.Blue);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);

            if (IsKeyDown(Keys.Right))
            {
                rotateY += (float)args.Time * 2;
            }

            if (IsKeyDown(Keys.Left))
            {
                rotateY -= (float)args.Time * 2;
            }

            if (IsKeyDown(Keys.Up))
            {
                rotateX += (float)args.Time * 2;
            }

            if (IsKeyDown(Keys.Down))
            {
                rotateX -= (float)args.Time * 2;
            }

            if (instance != null)
            {
                //generate camera matrix based off model dimensions
                var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60),
                    (float)Size.X / Size.Y,
                    0.02f, Math.Min(10000, modelRadius * 60));
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
                //update all transform matrices and animations
                instance.Animator.Update((float)args.Time);
                instance.UpdateTransforms();
                //draw starting at first root node.
                int boundVao = 0;
                SkinInstance? boundSkin = null;
                for (int i = 0; i < instance.Roots.Length; i++)
                {
                    DrawNode(instance.Roots[i], Matrix4x4.CreateRotationY(rotateY) * Matrix4x4.CreateRotationX(rotateX),
                        ref boundSkin,
                        ref boundVao);
                }

                //reset texture state
                GL.ActiveTexture(TextureUnit.Texture0);
            }

            //Draw UI
            text.Start(sz.X, sz.Y);
            foreach (var b in buttons)
            {
                b.Render(text);
            }

            text.DrawString("Mouse Wheel - Zoom, Keyboard Up/Down/Left/Right - Rotate", 5, sz.Y - 60);
            if (openfile != null)
                text.DrawString(openfile, 5, sz.Y - text.MeasureString(openfile).Y - 90);

            text.Finish();
            SwapBuffers();
        }

        static void Main()
        {
            var settings = NativeWindowSettings.Default;
            settings.ClientSize = new Vector2i(1024, 768);
            settings.Vsync = VSyncMode.On;
            new MainWindow(GameWindowSettings.Default, settings).Run();
        }
    }
}
