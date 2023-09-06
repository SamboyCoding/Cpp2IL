using Avalonia.Media.Imaging;

namespace Cpp2IL.Gui.Images;

public class ImageResources
{
    static Bitmap LoadBitmap(string name) => new Bitmap(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream($"Cpp2IL.Gui.Images.{name}.png") ?? throw new("Could not find image resource"));
    
    public static readonly Bitmap Assembly = LoadBitmap("Assembly");
    public static readonly Bitmap Namespace = LoadBitmap("NameSpace");
    public static readonly Bitmap Class = LoadBitmap("Class");
    public static readonly Bitmap Method = LoadBitmap("Method");
}
