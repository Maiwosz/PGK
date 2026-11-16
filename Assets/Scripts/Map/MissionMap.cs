using UnityEngine;

/// <summary>
/// Obiekt mapy misji w scenie gry. Sama mapa (teren, siatki GTA, tor, roślinność) NIE jest zapisana w scenie –
/// powstaje w edytorze z przepisu (Assets/Mission/Recipe) jako prefab Resources/MissionMap (Assets/Mission/Generated,
/// poza gitem), a ten komponent wstawia jej kopię pod siebie:
///  - w grze (Awake) – zawsze, także w buildzie,
///  - w edytorze – podgląd, który nie zapisuje się do sceny (zajmuje się tym MissionMapEditorHooks).
/// Dzięki temu scena gry jest zwykłym plikiem w repozytorium, bez odwołań do lokalnych, generowanych zasobów.
/// Reszta gry (rower, broń, HUD, pociąg, misja) o mapie wie tylko tyle, co daje <see cref="Track"/>.
/// </summary>
[DefaultExecutionOrder(-1000)]   // przed Start/Awake reszty gry – ścieżka toru ma być gotowa
[DisallowMultipleComponent]
public class MissionMap : MonoBehaviour
{
    public const string ResourceName = "MissionMap";
    public const string InstanceName = "MapInstance";

    public static MissionMap Instance { get; private set; }

    /// <summary>Wstawiona kopia mapy (null, gdy mapa nie została jeszcze wygenerowana).</summary>
    public Transform Content { get; private set; }
    /// <summary>Ścieżka toru z mapy – używają jej pociąg i warunki misji. Null bez mapy.</summary>
    public TrackPath Track { get; private set; }
    public bool HasContent => Content != null;

    void Awake()
    {
        Instance = this;
        if (Application.isPlaying) Load();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Wstawia mapę pod ten obiekt (jeśli już jest – używa istniejącej). Zwraca false, gdy brak wygenerowanego prefabu.</summary>
    public bool Load()
    {
        if (Content == null)
        {
            var existing = transform.Find(InstanceName);
            if (existing != null) Content = existing;
        }
        if (Content == null)
        {
            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                if (Application.isPlaying)
                    Debug.LogWarning("[MissionMap] Brak wygenerowanej mapy (Resources/MissionMap). W edytorze: Tools/Mission/Rebuild Map albo przycisk na obiekcie MissionMap.");
                return false;
            }
            var go = Instantiate(prefab, transform);
            go.name = InstanceName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Content = go.transform;
        }
        Track = Content.GetComponentInChildren<TrackPath>(true);
        return true;
    }

    /// <summary>Usuwa wstawioną kopię mapy (np. przed przebudową).</summary>
    public void Unload()
    {
        var existing = Content != null ? Content : transform.Find(InstanceName);
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
        Content = null;
        Track = null;
    }
}
