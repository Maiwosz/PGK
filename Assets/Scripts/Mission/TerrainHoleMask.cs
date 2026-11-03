using UnityEngine;

/// <summary>
/// Wycina z terenu (heightfield) komórki, w których powierzchnię przejmują siatki gruntu GTA (drogi, chodniki, trawa…).
/// Maska to jeden bit na komórkę (wiersz globalny = kafelek * tile + y). Wersja bez wycięć przywraca pierwotne dziury terenu
/// (np. przy tunelu), więc przełączanie jest odwracalne.
/// </summary>
public class TerrainHoleMask : MonoBehaviour
{
    public Terrain[] terrains;
    public int tile = 1024;
    public byte[] mask;        // komórki zastępowane siatkami GTA
    public byte[] baseOpen;    // pierwotny stan dziur (bit = 1: komórka pełna, bez dziury)

    static bool Bit(byte[] bits, int i) => (bits[i >> 3] & (1 << (i & 7))) != 0;

    /// <summary>Ustawia dziury: przy useGta komórki z maski są wycięte (poza tymi, które już były dziurą).</summary>
    public void Apply(bool useGta)
    {
        if (terrains == null || mask == null || baseOpen == null) return;
        for (int t = 0; t < terrains.Length; t++)
        {
            var td = terrains[t] != null ? terrains[t].terrainData : null;
            if (td == null) continue;
            int res = td.holesResolution;
            var holes = new bool[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int cy = y * tile / res + t * tile, cx = x * tile / res;
                    int i = cy * tile + cx;
                    holes[y, x] = Bit(baseOpen, i) && !(useGta && Bit(mask, i));
                }
            td.SetHoles(0, 0, holes);
        }
    }
}
