using System;

/// <summary>
/// Struktury danych "przepisu" mapy (Assets/Mission/Recipe/*.json) generowanego przez Tools/GtaExtract.
/// Współrzędne są już w układzie Unity (x, wysokość, z = północ).
/// </summary>
[Serializable]
public class RouteData
{
    public float spacing;       // odstęp punktów (m)
    public float lengthM;
    public float[] pts;         // x, y, z, x, y, z, ...
    public int[] kinds;         // 0 zwykły, 1 most, 2 tunel – po jednym na punkt
}

[Serializable]
public class MapObjectList
{
    public MapObject[] objects;
}

[Serializable]
public class MapObject
{
    public string n;            // nazwa modelu w grze (do podglądu)
    public string c;            // kategoria: building / fence / station / bridge / tunnel
    public float[] p;           // środek bryły (Unity)
    public float[] s;           // rozmiary (Unity: x, y = wysokość, z)
    public float[] q;           // kwaternion Unity (x, y, z, w)
    public float[] o;           // (opcjonalnie) początek modelu – wtedy bryła składa się z części b
    public float[] b;           // (opcjonalnie) części: cx,cy,cz,sx,sy,sz w układzie lokalnym modelu (Unity), po 6 liczb
}

[Serializable]
public class TerrainMeta
{
    public float minX, minY, cell;
    public int tile, samplesX, samplesY;
    public float zMin, zMax;
}

[Serializable]
public class PlantList
{
    public float[] d;           // po 5 liczb: typ (0 liściaste, 1 iglaste, 2 palma, 3 krzak), x, z, wysokość, promień korony
}
