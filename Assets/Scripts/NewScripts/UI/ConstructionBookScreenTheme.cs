using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Art, colours and font of the on-screen construction book, taken from the Figma "Construction Book v2" spreads
/// (Goblin Bridge Builders file, page "Blueprint book"). Assets live in Resources/ConstructionBookUI.
/// </summary>
public static class ConstructionBookScreenTheme
{
    private const string ResourceRoot = "ConstructionBookUI/";
    private const string FontPath = ResourceRoot + "Fonts/Ribeye-Regular";

    public static readonly Color Backdrop = Hex(0x242723);
    public static readonly Color Ink = Hex(0x3B2414);
    public static readonly Color InkLight = Hex(0x6B4A2E);
    public static readonly Color Rust = Hex(0xB5561C);
    public static readonly Color Hint = Hex(0xADACAC);
    public static readonly Color DashedRule = new Color(59f / 255f, 36f / 255f, 20f / 255f, 0.45f);

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private static TMP_FontAsset font;
    private static bool fontUnavailable;

    /// <summary>Ribeye as a dynamic TMP font asset, or null (TMP default font is used) if the font file is missing.</summary>
    public static TMP_FontAsset Font
    {
        get
        {
            if (font != null || fontUnavailable) return font;
            UnityEngine.Font source = Resources.Load<UnityEngine.Font>(FontPath);
            font = source != null ? TMP_FontAsset.CreateFontAsset(source) : null;
            if (font == null)
            {
                fontUnavailable = true;
                Debug.LogWarning($"ConstructionBook: font '{FontPath}' could not be loaded; using the default TMP font.");
                return null;
            }
            font.name = "Ribeye (Construction Book)";
            if (TMP_Settings.defaultFontAsset != null)
                font.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            return font;
        }
    }

    public static Sprite LoadSprite(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // A cached real null is a known miss; a destroyed sprite (Unity null) is loaded again.
        if (spriteCache.TryGetValue(name, out Sprite cached) && (cached != null || ReferenceEquals(cached, null)))
            return cached;
        Sprite sprite = Resources.Load<Sprite>(ResourceRoot + name);
        spriteCache[name] = sprite;
        return sprite;
    }

    /// <summary>Ink-sketch icon for a step requirement, matched by its display name (e.g. "Industrial Hammer").</summary>
    public static Sprite ToolIcon(string displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : LoadSprite("ToolIcons/" + displayName.Trim());

    /// <summary>Book illustration for a material, matched by the resource name (e.g. "Wooden Log").</summary>
    public static Sprite MaterialIcon(string resourceName) =>
        string.IsNullOrWhiteSpace(resourceName) ? null : LoadSprite("MaterialIcons/" + resourceName.Trim());

    /// <summary>
    /// False for missing art and for Unity's built-in "UISprite", which ConstructionBookSetup assigns as a placeholder
    /// until real part/tool art exists.
    /// </summary>
    public static bool HasArt(Sprite sprite) => sprite != null && sprite.name != "UISprite";

    private static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
