using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SampleRenderer;
using SimpleMesh;
using SimpleMesh.Convex;
using Vector2i = OpenTK.Mathematics.Vector2i;
using Color4 = OpenTK.Mathematics.Color4;
using Vector3 = OpenTK.Mathematics.Vector3;

namespace ConvexHullSample
{
    partial class MainWindow : GameWindow
    {
        private Render2D text;
        public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        //Handle Input

        private List<Button> buttons = new List<Button>();
        
        private Shader diffuseShader;
        
        private Button openButton;
        private Button quickhullButton;
        
        protected override unsafe void OnLoad()
        {
            base.OnLoad();
            diffuseShader = new Shader(MeshVertexShader, MeshFragmentShader);
            openButton = new Button("Open...", 5, 5, () =>
            {
                var openfile = FilePicker.OpenFile();
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
            quickhullButton = new Button("Quickhull", 5, 40, QuickhullModel);
            buttons = [openButton];
            text = new Render2D();
        }

        private Button down = null;
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            foreach(var b in buttons)
                b.OnMouseDown(e, MousePosition, text);
        }
        
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            foreach(var b in buttons)
                b.OnMouseUp(e, MousePosition, text);
            base.OnMouseUp(e);
        }

        private float zoom = 1;

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            zoom += e.OffsetY / 8.0f;
        }

        private float rotateY = 0;
        private float rotateX = 0;
        

        private Model model;

        private int meshVao;
        private int meshVbo;
        private int meshEbo;
        
        private string statusText = "";
        private double statusTimer = 5;

        void QuickhullModel()
        {
            if (!displayed.MakeConvex(true))
            {
                statusText = "Error running quickhull";
                statusTimer = 5;
            }
            SetupHull(displayed);
        }
        
        private Hull displayed;
        private float modelRadius = 1;
        unsafe void SetupHull(Hull h)
        {
            displayed = h;
            //Unbind vao so we don't delete the active one
            GL.BindVertexArray(0);
            if (meshVbo != 0)
            {
                GL.DeleteVertexArray(meshVao);
                GL.DeleteBuffer(meshEbo);
                GL.DeleteBuffer(meshVbo);
            }
            zoom = 1;
            modelRadius = (h.Max - h.Min).Length();
            //Copy buffers
            var (vertices, indices) = DisplayVertex.FromHull(h);
            //Upload data to OpenGL
            meshVao = GL.GenVertexArray();
            meshEbo = GL.GenBuffer();
            meshVbo = GL.GenBuffer();
            
            GL.BindVertexArray(meshVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, meshVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(DisplayVertex) * vertices.Length, vertices, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(DisplayVertex), 0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeof(DisplayVertex), sizeof(Vector3));
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, meshEbo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, sizeof(ushort) * indices.Length, indices, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);
            buttons = [openButton, quickhullButton];
        }
        void LoadModel(string filename)
        {
            var sw = Stopwatch.StartNew();
            model = Model.FromFile(filename)
                .AutoselectRoot(out _); //try discard empty nodes at root (think blender cameras etc.)
            sw.Stop();
            SetupHull(Hull.FromGeometry(model.Roots[0].Geometry));
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
            
            var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60), (float) Size.X / Size.Y,
                0.02f, 10000);
            var campos = new Vector3(0, 0, (-2 * modelRadius) * zoom);
            var view = Matrix4.LookAt(campos, Vector3.Zero, Vector3.UnitY);
            var vp = view * projection;
            var world = Matrix4x4.CreateRotationY(rotateY) * Matrix4x4.CreateRotationX(rotateX);
            Matrix4x4.Invert(world, out var normalmat);
            
            if (displayed != null)
            {
                GL.BindVertexArray(meshVao);
                //generate camera matrix based off hull dimensions
                
                diffuseShader.Set("viewprojection", vp);
                diffuseShader.Set("light_direction", -0.49999f, 0.707107f, 0.5f);
                diffuseShader.Set("world", world);
                diffuseShader.Set("normalmat", normalmat);
                diffuseShader.Use();
                GL.DrawElements(PrimitiveType.Triangles, displayed.Indices.Length, DrawElementsType.UnsignedShort,
                    IntPtr.Zero);
            }
            
            //Draw UI
            text.Start(sz.X, sz.Y);
            foreach (var b in buttons)
            {
                b.Render(text);
            }

            if (displayed != null)
            {
                text.DrawString(
                    @$"Kind: {displayed.Kind}
Min: {displayed.Min}
Max: {displayed.Max}
Volume: {displayed.Volume}",
                    5,
                    200
                );
            }

            if (statusTimer > 0 && !string.IsNullOrWhiteSpace(statusText))
            {
                text.DrawString(statusText, 5, 320);
                statusTimer -= args.Time;
            }
            
           
            
            text.DrawString("Mouse Wheel - Zoom, Keyboard Up/Down/Left/Right - Rotate", 5, sz.Y - 60);
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