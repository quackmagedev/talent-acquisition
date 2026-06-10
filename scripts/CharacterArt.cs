using Godot;

// Builds the single placeholder character sprite shared by every entity:
// a vaguely humanoid stack of rectangles (16x24 px, drawn at 2x scale).
//
// Skin and clothes live on two separate texture layers so the sprint
// "glitch" can recolor only the skin. The skin layer is drawn in pure
// white and tinted via Sprite2D.Modulate (tan by default, green/blue
// while a player sprints).
public static class CharacterArt
{
    public static readonly Color SkinTone = new Color("d9a066"); // tan

    private static readonly Color Shirt = new Color("8a5a2b"); // brown shirt
    private static readonly Color Pants = new Color("4a2f17"); // dark brown pants

    private const int Width = 16;
    private const int Height = 24;

    // Built once and reused, so all 17 entities share the exact same textures.
    private static ImageTexture _skinTexture;
    private static ImageTexture _clothesTexture;

    public static ImageTexture SkinTexture => _skinTexture ??= BuildSkinTexture();
    public static ImageTexture ClothesTexture => _clothesTexture ??= BuildClothesTexture();

    private static ImageTexture BuildSkinTexture()
    {
        Image image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(4, 0, 8, 6), Colors.White);   // head
        image.FillRect(new Rect2I(1, 12, 2, 2), Colors.White);  // left hand
        image.FillRect(new Rect2I(13, 12, 2, 2), Colors.White); // right hand
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture BuildClothesTexture()
    {
        Image image = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        image.FillRect(new Rect2I(3, 6, 10, 8), Shirt);  // torso
        image.FillRect(new Rect2I(1, 6, 2, 6), Shirt);   // left sleeve
        image.FillRect(new Rect2I(13, 6, 2, 6), Shirt);  // right sleeve
        image.FillRect(new Rect2I(4, 14, 3, 10), Pants); // left leg
        image.FillRect(new Rect2I(9, 14, 3, 10), Pants); // right leg
        return ImageTexture.CreateFromImage(image);
    }
}
