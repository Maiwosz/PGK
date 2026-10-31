using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ręczne sterowanie kierownicą: LPM na uchwycie "łapie" kierownicę, a ruch myszy
/// obraca ją wokół osi sterowania. Kąt jest liczony z położenia kursora rzutowanego
/// na poziomą płaszczyznę na wysokości chwytu, więc kierownica podąża za dłonią.
/// Po puszczeniu kierownica sama wraca do środka (szybciej przy większej prędkości).
/// Komponent powinien wisieć na obiekcie obracającym się razem z kierownicą (oś Y = oś sterowania).
/// </summary>
public class HandlebarSteering : MonoBehaviour
{
    [SerializeField] Camera viewCamera;
    [SerializeField] GripHandle[] grips;

    [Header("Sterowanie")]
    [Tooltip("Maksymalny wychył kierownicy w każdą stronę (stopnie).")]
    [SerializeField] float maxAngle = 55f;
    [Tooltip("Jak szybko kierownica dogania dłoń (większe = sztywniej).")]
    [SerializeField] float followSharpness = 30f;
    [Tooltip("Zasięg promienia do łapania uchwytów (m).")]
    [SerializeField] float reachDistance = 5f;

    [Header("Samocentrowanie po puszczeniu")]
    [SerializeField] float returnBase = 35f;
    [SerializeField] float returnPerSpeed = 25f;

    /// <summary>Obrót kierownicy w stopniach, dodatni = w prawo.</summary>
    public float Angle { get; private set; }
    public bool IsGrabbed => grabbed != null;
    public bool LeftHeld => grabbed != null && grabbed.transform.localPosition.x < 0f;
    public bool RightHeld => grabbed != null && grabbed.transform.localPosition.x > 0f;
    /// <summary>Prędkość roweru (m/s) – ustawiana przez BikeController, wpływa na samocentrowanie.</summary>
    public float Speed { get; set; }

    GripHandle grabbed;
    GripHandle hovered;
    float targetAngle;
    float startAngle;
    float startMouseAngle;
    readonly RaycastHit[] hits = new RaycastHit[16];

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || viewCamera == null) return;

        Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());

        if (grabbed == null)
        {
            GripHandle under = PickGrip(ray);
            SetHover(under);
            if (under != null && mouse.leftButton.wasPressedThisFrame)
                BeginGrab(under, ray);
        }
        else if (!mouse.leftButton.isPressed)
        {
            EndGrab();
        }
        else
        {
            DragGrab(ray);
        }

        if (grabbed != null)
        {
            Angle = Mathf.Lerp(Angle, targetAngle, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        }
        else
        {
            float rate = returnBase + returnPerSpeed * Speed;
            Angle = Mathf.MoveTowards(Angle, 0f, rate * Time.deltaTime);
            targetAngle = Angle;
        }

        transform.localRotation = Quaternion.AngleAxis(Angle, Vector3.up);
    }

    void OnDisable()
    {
        if (grabbed != null) grabbed.SetState(GripHandle.State.Idle);
        if (hovered != null) hovered.SetState(GripHandle.State.Idle);
        grabbed = hovered = null;
    }

    GripHandle PickGrip(Ray ray)
    {
        int count = Physics.RaycastNonAlloc(ray, hits, reachDistance, ~0, QueryTriggerInteraction.Collide);
        GripHandle best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= bestDist) continue;
            var grip = hits[i].collider.GetComponent<GripHandle>();
            if (grip == null) continue;
            best = grip;
            bestDist = hits[i].distance;
        }
        return best;
    }

    void SetHover(GripHandle grip)
    {
        if (hovered == grip) return;
        if (hovered != null) hovered.SetState(GripHandle.State.Idle);
        hovered = grip;
        if (hovered != null) hovered.SetState(GripHandle.State.Hover);
    }

    void BeginGrab(GripHandle grip, Ray ray)
    {
        if (!TryGetMouseAngle(grip, ray, out float mouseAngle)) return;

        grabbed = grip;
        hovered = null;
        grabbed.SetState(GripHandle.State.Grabbed);

        startMouseAngle = mouseAngle;
        startAngle = Angle;
        targetAngle = Angle;
    }

    void EndGrab()
    {
        grabbed.SetState(GripHandle.State.Idle);
        grabbed = null;
    }

    void DragGrab(Ray ray)
    {
        if (!TryGetMouseAngle(grabbed, ray, out float mouseAngle)) return;

        float raw = startAngle + Mathf.DeltaAngle(startMouseAngle, mouseAngle);
        float clamped = Mathf.Clamp(raw, -maxAngle, maxAngle);

        // Gdy dłoń "przekręca" kierownicę poza ogranicznik, przesuwamy punkt odniesienia,
        // żeby nie trzeba było potem cofać nadmiarowego ruchu myszy.
        if (!Mathf.Approximately(raw, clamped))
            startAngle -= raw - clamped;

        targetAngle = clamped;
    }

    /// <summary>
    /// Rzutuje promień myszy na poziomą (względem roweru) płaszczyznę na wysokości chwytu
    /// i zwraca kąt wektora środek kierownicy → punkt w lokalnych współrzędnych rodzica.
    /// </summary>
    bool TryGetMouseAngle(GripHandle grip, Ray ray, out float angle)
    {
        angle = 0f;
        Transform parent = transform.parent;
        Vector3 up = parent != null ? parent.up : Vector3.up;

        var plane = new Plane(up, grip.transform.position);
        if (!plane.Raycast(ray, out float enter)) return false;

        Vector3 world = ray.GetPoint(enter);
        Vector3 local = parent != null ? parent.InverseTransformPoint(world) : world;
        local -= transform.localPosition;
        local.y = 0f;

        if (local.sqrMagnitude < 1e-4f) return false;

        angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        return true;
    }
}
