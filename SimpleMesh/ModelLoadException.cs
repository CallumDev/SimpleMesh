using System;

namespace SimpleMesh
{
    public class ModelLoadException : Exception
    {
        public ModelLoadException(string message) : base(message)
        {
        }
        
        public ModelLoadException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}