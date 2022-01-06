using Avalonia.Media.Imaging;

namespace Cpp2IL.Gui.Images;

public class ImageResources
{
    static IBitmap LoadBitmap(string name) => new Bitmap("Images/" + name + ".png");
    
    public static readonly IBitmap Assembly = LoadBitmap("Assembly");
    public static readonly IBitmap Namespace = LoadBitmap("NameSpace");
    public static readonly IBitmap Class = LoadBitmap("Class");
    public static readonly IBitmap Method = LoadBitmap("Method");
}