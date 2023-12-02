using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    class MainWindow : GameWindow
    {
        private Render2D text;
        public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        private const string VERTEX_SHADER = @"
#version 120
uniform mat4 world;
uniform mat4 viewprojection;
uniform mat4 normalmat;

attribute vec3 v_position;
attribute vec3 v_normal;
attribute vec4 v_diffuse;

varying vec4 diffuse;
varying vec3 fragpos;
varying vec3 normal;
void main()
{
    diffuse = v_diffuse;
    mat4 mvp = (viewprojection * world);
    fragpos = (mvp * vec4(v_position, 1.0)).xyz;
    normal = (normalmat * vec4(v_normal, 0.0)).xyz;
    gl_Position = mvp * vec4(v_position, 1.0);
}
";

        private const string FRAGMENT_SHADER = @"
#version 120
varying vec4 diffuse;
varying vec3 fragpos;
varying vec3 normal;

uniform vec4 mat_diffuse;

void main()
{
    vec3 norm = normalize(normal);
    vec3 lightDir = normalize(vec3(0,-10,-1000) - fragpos);  
    float diff = max(dot(norm, lightDir), 0.0);
    gl_FragColor = diff * (mat_diffuse * diffuse);
}
";

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
        
        private Shader shader;
        private int uniform_world;
        private int uniform_normal;
        private int uniform_vp;
        private int uniform_mat_diffuse;
        private AnimationHandler animations;

        private Button openButton;
        private Button saveButton;
        private Button saveGltfButton;
        protected override void OnLoad()
        {
            base.OnLoad();
            shader = new Shader(VERTEX_SHADER, FRAGMENT_SHADER);
            uniform_vp = shader.GetLocation("viewprojection");
            uniform_normal = shader.GetLocation("normalmat");
            uniform_world = shader.GetLocation("world");
            uniform_mat_diffuse = shader.GetLocation("mat_diffuse");
            openButton = new Button("Open...", 5, 5, () =>
            {
                openfile = FilePicker.OpenFile();
                if (openfile != null)
                    LoadModel(openfile);
            });
            saveButton = new Button("Save To Binary", 5, 40, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, false);
            });
            saveGltfButton = new Button("Save To GLTF", 5, 75, () =>
            {
                var savefile = FilePicker.SaveFile();
                if (savefile != null)
                    SaveModel(savefile, true);
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
            model.SaveTo(stream, gltf ? ModelSaveFormat.GLTF2 : ModelSaveFormat.SMesh);
        }

        void LoadModel(string filename)
        {
            animations = new AnimationHandler();
            var sw = Stopwatch.StartNew();
            using var stream = File.OpenRead(filename);
            model = Model.FromStream(stream)
                .AutoselectRoot(out _) //try discard empty nodes at root (think blender cameras etc.)
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
                            idx32[iCount + i] = g.Indices.Indices16[iCount];
                    }
                    else {
                        Array.Copy(g.Indices.Indices32, 0, idx32, iCount, g.Indices.Indices32.Length);
                    }
                }
                vCount += g.Vertices.Length;
                iCount += g.Indices.Length;
            }

            //Upload data to OpenGL
            vao = GL.GenVertexArray();
            ebo = GL.GenBuffer();
            vbo = GL.GenBuffer();
            
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, VERTEX_SIZE * vertices.Length, vertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VERTEX_SIZE, 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, VERTEX_SIZE, 3 * sizeof(float));
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, VERTEX_SIZE, 6 * sizeof(float));
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            if (isIdx32)
                GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(uint) * idx32.Length, idx32,
                    BufferUsageHint.StaticDraw);
            else
                GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(ushort) * idx16.Length, idx16, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);
            
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
        }

        class BufferOffset
        {
            public int BaseVertex;
            public int StartIndex;
            public bool Index32;
        }
        
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
            var mymat = selfTransform * parent;
            if (node.Geometry != null) //some models will have empty nodes purely for transforms
            {
                GL.UniformMatrix4(uniform_world, 1, false, (float*) &mymat);
                Matrix4x4.Invert(mymat, out var normalmat);
                normalmat = Matrix4x4.Transpose(normalmat);
                GL.UniformMatrix4(uniform_normal, 1, false, (float*) &normalmat);
                var off = (BufferOffset) node.Geometry.UserTag;
                foreach (var tg in node.Geometry.Groups)
                {
                    GL.Uniform4(uniform_mat_diffuse, tg.Material.DiffuseColor.X, tg.Material.DiffuseColor.Y,
                        tg.Material.DiffuseColor.Z, 1);
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

        private string openfile = null;
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
                GL.UseProgram(shader.ID);
                GL.BindVertexArray(vao);
                //generate camera matrix based off model dimensions
                float dist = 0;
                for (int i = 0; i < model.Roots.Length; i++) {
                    if (model.Roots[i].Geometry != null)
                        dist = Math.Max(model.Roots[i].Geometry.Radius, dist);
                }
                var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), (float) Size.X / Size.Y,
                    0.02f, 10000);
                var view = Matrix4.LookAt(new Vector3(0, 0, (-dist * 4) * zoom), Vector3.Zero, Vector3.UnitY);
                var vp = view * projection;
                GL.UniformMatrix4(uniform_vp, false, ref vp);
                //draw starting at first root node.
                for (int i = 0; i < model.Roots.Length; i++)
                {
                    DrawNode(model.Roots[i], Matrix4x4.CreateRotationY(rotateY) * Matrix4x4.CreateRotationX(rotateX));
                }
            }
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
            settings.Size = new Vector2i(1024, 768);
            new MainWindow(GameWindowSettings.Default, settings).Run();
        }
    }
}