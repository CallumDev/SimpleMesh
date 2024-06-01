using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

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
            GL.BindAttribLocation(ID, 3, "v_tangent");
            GL.BindAttribLocation(ID, 4, "v_texture1");
            GL.BindAttribLocation(ID, 5, "v_texture2");
            GL.BindAttribLocation(ID, 6, "v_texture3");
            GL.BindAttribLocation(ID, 7, "v_texture4");

            GL.LinkProgram(ID);
            Console.WriteLine(GL.GetProgramInfoLog(ID));
            GL.GetProgram(ID, ProgramParameter.LinkStatus, out status);
            if (status == 0) throw new Exception("Link failed");
        }
        
        private Dictionary<string, int> locs = new Dictionary<string, int>();


        private static int usedProgram = -1;
        
        public int GetLocation(string s)
        {
            if (!locs.TryGetValue(s, out int l))
            {
                l = GL.GetUniformLocation(ID, s);
                locs[s] = l;
            }
            return l;
        }

        public void SetI(string loc, int v)
        {
            Use();
            var x = GetLocation(loc);
            if(x != -1) GL.Uniform1(x, v);
        }
        
        public void SetF(string loc, float v)
        {
            Use();
            var x = GetLocation(loc);
            if(x != -1) GL.Uniform1(x, v);
        }
        
        public void Set(string loc, Vector2 v)
        {
            Use();
            var x = GetLocation(loc);
            if(x != -1) GL.Uniform2(x, v);
        }
        
        public void Set(string loc, float x, float y, float z)
        {
            Use();
            var l = GetLocation(loc);
            if (l != -1) GL.Uniform3(l, x, y, z);
        }
        
        public void Set(string loc, float x, float y, float z, float w)
        {
            Use();
            var l = GetLocation(loc);
            if(l != -1) GL.Uniform4(l, x,y,z,w);
        }

        public unsafe void Set(string loc, System.Numerics.Matrix4x4 mat)
        {
            Use();
            var x = GetLocation(loc);
            if (x == -1) return;
            GL.UniformMatrix4(x, 1, false, (float*) &mat);
        }
        
        public unsafe void Set(string loc, Matrix4 mat)
        {
            Use();
            var x = GetLocation(loc);
            if (x == -1) return;
            GL.UniformMatrix4(x,  false, ref mat);
        }

        public void Use()
        {
            if (usedProgram != ID)
            {
                GL.UseProgram(ID);
                usedProgram = ID;
            }
        }
    }
}