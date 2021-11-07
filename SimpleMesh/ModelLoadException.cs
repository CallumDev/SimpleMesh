using System;

namespace SimpleMesh
{
    public class ModelLoadException : Exception
    {
        public ModelLoadException(string message) : base(message)
        {
        }
    }
}