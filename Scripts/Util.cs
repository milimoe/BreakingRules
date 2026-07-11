using Godot;

namespace BreakingRules;

public static class Util
{
    public static ImageTexture Square(int size, Color color)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(color);
        return ImageTexture.CreateFromImage(img);
    }

    public static ImageTexture Square(int w, int h, Color color)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(color);
        return ImageTexture.CreateFromImage(img);
    }
}
