using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Wspólny kontrakt trybów pedałowania. Tryb to osobny komponent na obiekcie roweru (obok PedalDrive), który:
///  - co klatkę czyta klawisze w <see cref="Tick"/> i zapisuje wynik we wspólnym stanie skoku PedalDrive
///    (noga, postęp, siła, oceny), a nic poza tym nie dotyka,
///  - podaje tempo skoku (<see cref="TempoScale"/>) i opisy do okna pomocy.
/// Własny HUD trybu to osobny komponent (np. RhythmHud dla RhythmPedaling), który czyta tylko ten tryb i PedalDrive.
/// Dzięki temu tryb można usunąć jednym ruchem: skasuj komponent trybu, jego HUD i plik – nic innego się nie zmienia.
/// </summary>
[RequireComponent(typeof(PedalDrive))]
public abstract class PedalingMode : MonoBehaviour
{
    protected PedalDrive drive;

    protected virtual void Awake() => drive = GetComponent<PedalDrive>();

    /// <summary>Mnożnik tempa skoku względem tempa wynikającego z prędkości i biegu (1 = bez zmian).</summary>
    public abstract float TempoScale { get; }

    /// <summary>Wywoływane przez PedalDrive co klatkę (tylko dla aktywnego trybu, nie podczas pauzy).</summary>
    public abstract void Tick(float dt, Keyboard kb);

    /// <summary>Czyści stan własny trybu (restart, upadek, zmiana trybu).</summary>
    public virtual void ResetMode() { }

    /// <summary>Czy ten tryb jest teraz aktywny na PedalDrive.</summary>
    public bool IsActive => drive != null && drive.Mode == this;

    /// <summary>Wiersz tabeli sterowania w oknie pomocy (opis klawiszy A / D).</summary>
    public abstract string ControlsHelp { get; }

    /// <summary>Akapit o mechanice pedałowania w oknie pomocy (może zawierać znaczniki rich text).</summary>
    public abstract string MechanicsHelp { get; }
}
