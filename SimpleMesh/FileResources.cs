using System.IO;

namespace SimpleMesh;

public class FileResources : IExternalResources
{
    public bool CanLoadResources => true;
    
    private string directory;
    public FileResources(string filename)
    {
        directory = Path.GetDirectoryName(filename);
    }

    public Stream OpenStream(string filename)
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