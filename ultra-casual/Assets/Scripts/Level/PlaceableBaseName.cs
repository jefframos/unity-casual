

using UnityEngine;

public enum PlaceableBaseName
{
    WoodBox = 1,
    MetalBox = 10,
    TNT = 20,
    Barrel = 30
}

public static class PlaceableBaseNameExtensions
{
    public static string ToId(this PlaceableBaseName baseName)
    {
        switch (baseName)
        {
            case PlaceableBaseName.WoodBox: return "WoodBox";
            case PlaceableBaseName.MetalBox: return "MetalBox";
            case PlaceableBaseName.TNT: return "TNT";
            case PlaceableBaseName.Barrel: return "Barrel";
        }
        return baseName.ToString();
    }

    public static Color ToColor(this PlaceableBaseName baseName)
    {
        switch (baseName)
        {
            case PlaceableBaseName.WoodBox:
                // Warm brown-ish (wood)
                return new Color(0.72f, 0.54f, 0.32f, 0.75f);

            case PlaceableBaseName.MetalBox:
                // Cool steel gray-blue
                return new Color(0.45f, 0.58f, 0.72f, 0.75f);

            case PlaceableBaseName.TNT:
                // Strong red with slightly dark tone
                return new Color(0.80f, 0.20f, 0.22f, 0.75f);

            case PlaceableBaseName.Barrel:
                // Orange-brown (oil barrel / hazardous)
                return new Color(0.83f, 0.48f, 0.20f, 0.75f);
        }

        // default: white-ish with slight transparency
        return new Color(1f, 1f, 1f, 0.75f);
    }

    public static float ToBaseMass(this PlaceableBaseName baseName)
    {
        switch (baseName)
        {
            case PlaceableBaseName.WoodBox:
                // Warm brown-ish (wood)
                return 3f;

            case PlaceableBaseName.MetalBox:
                // Cool steel gray-blue
                return 10f;

            case PlaceableBaseName.TNT:
                // Strong red with slightly dark tone
                return 8f;

            case PlaceableBaseName.Barrel:
                // Orange-brown (oil barrel / hazardous)
                return 8f;
        }

        // default: white-ish with slight transparency
        return 2f;
    }

}
