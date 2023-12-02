using System.Linq;

namespace SimpleMesh;

public class Animation
{
    public string Name;
    public RotationChannel[] Rotations;
    public TranslationChannel[] Translations;

    public Animation Clone()
    {
        var anm = new Animation();
        anm.Name = Name;
        anm.Rotations = Rotations.Select(x => x.Clone()).ToArray();
        anm.Translations = Translations.Select(x => x.Clone()).ToArray();
        return anm;
    }
}