using System.IO;

namespace SimpleMesh;

public class FileResources(string filename) : IExternalResources
{
    public bool CanLoadResources => true;
    
    private readonly string directory = Path.GetDirectoryName(filename) ?? "";

    public Stream? OpenStream(string filename)
    {
        try
        {
            filename = filename.Replace('\\', Path.DirectorySeparatorChar);
            return File.OpenRead(Path.Combine(directory, filename));
        }
        catch
        {
            return null;
        }
    }
}