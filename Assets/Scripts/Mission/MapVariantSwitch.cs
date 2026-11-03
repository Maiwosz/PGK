using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Przełącza między wariantami elementów mapy: "nasze" (proste, generowane) i "GTA" (siatki kolizji z gry).
/// Każdy slot ma swój klawisz; w grze widać, który wariant jest aktywny.
/// </summary>
public class MapVariantSwitch : MonoBehaviour
{
    [Serializable]
    public class Slot
    {
        public string label;
        public Key key = Key.F5;
        public GameObject[] ours;
        public GameObject[] gta;
        public bool useGta;
        public TerrainHoleMask holes;     // opcjonalnie: wycięcia terenu przy wersji GTA
    }

    public Slot[] slots;

    GUIStyle style;

    void Start()
    {
        foreach (var s in slots) Apply(s);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        foreach (var s in slots)
        {
            if (!kb[s.key].wasPressedThisFrame) continue;
            s.useGta = !s.useGta;
            Apply(s);
        }
    }

    /// <summary>Ustawia aktywność obiektów slotu zgodnie z wybranym wariantem.</summary>
    public static void Apply(Slot s)
    {
        foreach (var go in s.ours) if (go != null) go.SetActive(!s.useGta);
        foreach (var go in s.gta) if (go != null) go.SetActive(s.useGta);
        if (s.holes != null) s.holes.Apply(s.useGta);
    }

    void OnGUI()
    {
        if (slots == null) return;
        style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, wordWrap = false, clipping = TextClipping.Overflow };
        float u = Mathf.Max(14f, Screen.height / 40f);
        style.fontSize = Mathf.RoundToInt(u * 0.85f);
        float y = u;
        foreach (var s in slots)
        {
            style.normal.textColor = s.useGta ? new Color(1f, 0.85f, 0.2f) : new Color(0.75f, 0.9f, 1f);
            GUI.Label(new Rect(Screen.width - 420f - u, y, 420f, u * 1.3f), $"{s.key}  {s.label}: {(s.useGta ? "GTA (siatki)" : "nasze")}", style);
            y += u * 1.3f;
        }
    }
}
