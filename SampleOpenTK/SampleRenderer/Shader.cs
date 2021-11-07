using System;
using OpenTK.Graphics.OpenGL;

namespace SampleOpenTK
{
    public class Shader
    {
        public int ID;
        public Shader(string vsource, string fsource)
        {
            int status;
            var vert = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vert, vsource);
            GL.CompileShader(vert);
            Console.WriteLine(GL.GetShaderInfoLog(vert));
            GL.GetShader(vert, ShaderParameter.CompileStatus, out status);
            if (status == 0) throw new Exception("Compile failed");
            var frag = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(frag, fsource);
            GL.CompileShader(frag);
            Console.WriteLine(GL.GetShaderInfoLog(frag));
            GL.GetShader(frag, ShaderParameter.CompileStatus, out status);
            if (status == 0) throw new Exception("Compile failed");
            ID = GL.CreateProgram();
            GL.AttachShader(ID, vert);
            GL.AttachShader(ID, frag);
            GL.BindAttribLocation(ID, 0, "v_position");
            GL.BindAttribLocation(ID, 1, "v_normal");
            GL.BindAttribLocation(ID, 2, "v_diffuse");
            GL.BindAttribLocation(ID, 3, "v_texture1");
            GL.BindAttribLocation(ID, 4, "v_texture2");
            GL.LinkProgram(ID);
            Console.WriteLine(GL.GetProgramInfoLog(ID));
            GL.GetProgram(ID, ProgramParameter.LinkStatus, out status);
            if (status == 0) throw new Exception("Link failed");
        }
        
        public int GetLocation(string s)
        {
            return GL.GetUniformLocation(ID, s);
        }
    }
}