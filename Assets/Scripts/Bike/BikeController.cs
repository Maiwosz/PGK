using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Prosta dynamika roweru: prędkość pochodzi z pedałowania (PedalDrive) minus opory,
/// a skręt z kąta kierownicy (HandlebarSteering) wg modelu rowerowego (yaw = v / L * tan(delta)).
/// Przechył (lean) jest tylko wizualny i wynika z przyspieszenia dośrodkowego.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BikeController : MonoBehaviour
{
    [Header("Referencje")]
    [SerializeField] PedalDrive pedals;
    [SerializeField] HandlebarSteering steering;
    [SerializeField] Transform leanPivot;
    [SerializeField] Transform[] spinningWheels;

    [Header("Geometria")]
    [SerializeField] float wheelbase = 1.12f;
    [SerializeField] float wheelRadius = 0.34f;

    [Header("Opory i hamulec")]
    [SerializeField] float rollingResistance = 0.08f;   // m/s^2
    [SerializeField] float airDrag = 0.0035f;           // m/s^2 na (m/s)^2
    [SerializeField] float maxSpeed = 25f;

    [Header("Hamulce (uchwyt trzymany myszką + Spacja)")]
    [Tooltip("Lewy uchwyt + Spacja (jak w Polsce i reszcie kontynentalnej Europy / USA).")]
    [SerializeField] float frontBrakeDeceleration = 6.5f;  // m/s^2
    [Tooltip("Prawy uchwyt + Spacja.")]
    [SerializeField] float rearBrakeDeceleration = 3.5f;   // m/s^2

    [Header("Skręt")]
    [Tooltip("Jaka część obrotu kierownicy przechodzi na koło (reszta to 'luz' geometrii).")]
    [SerializeField] float steerRatio = 0.5f;
    [Tooltip("Im większe, tym słabszy skręt przy dużej prędkości.")]
    [SerializeField] float speedSteerDamping = 0.06f;
    [Tooltip("Limit przyspieszenia bocznego (m/s^2) – powyżej rower 'nie dowiezie' zakrętu.")]
    [SerializeField] float maxLateralAccel = 7f;

    [Header("Przechył")]
    [SerializeField] float maxLean = 35f;
    [SerializeField] float leanSharpness = 6f;

    Rigidbody rb;
    float speed;
    float yaw;
    float yawRate;              // rad/s, + = w prawo
    float lean;                 // stopnie, + = w prawo
    Vector3 lastForward;
    Vector3 startPosition;
    float startYaw;

    public float Speed => speed;
    public float SpeedKmh => speed * 3.6f;
    public float Lean => lean;
    public bool FrontBrake { get; private set; }
    public bool RearBrake { get; private set; }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startYaw = transform.eulerAngles.y;
        yaw = startYaw;
        lastForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Kolizje: jeśli fizyka wyhamowała ruch wzdłuż kierunku jazdy, przejmujemy tę prędkość.
        float actual = Vector3.Dot(rb.linearVelocity, lastForward);
        speed = Mathf.Min(speed, Mathf.Max(0f, actual) + 0.01f);

        float drive = pedals != null ? pedals.GetDriveAcceleration(speed) : 0f;
        float brake = (FrontBrake ? frontBrakeDeceleration : 0f) + (RearBrake ? rearBrakeDeceleration : 0f);
        float decel = rollingResistance + airDrag * speed * speed + brake;
        speed = Mathf.Clamp(speed + (drive - decel) * dt, 0f, maxSpeed);

        // Model rowerowy: yaw rate = v / L * tan(delta)
        float handlebar = steering != null ? steering.Angle : 0f;
        float delta = handlebar * steerRatio / (1f + speed * speedSteerDamping);
        float rate = speed / wheelbase * Mathf.Tan(delta * Mathf.Deg2Rad);

        if (speed > 0.1f)
        {
            float limit = maxLateralAccel / speed;
            rate = Mathf.Clamp(rate, -limit, limit);
        }
        yawRate = rate;

        yaw += yawRate * Mathf.Rad2Deg * dt;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = rotation * Vector3.forward;

        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;

        Vector3 v = forward * speed;
        v.y = rb.linearVelocity.y;
        rb.linearVelocity = v;
        lastForward = forward;
    }

    void Update()
    {
        if (steering != null) steering.Speed = speed;
        if (pedals != null) pedals.Speed = speed;

        var kb = Keyboard.current;
        bool leftHeld = steering != null && steering.LeftHeld;
        bool rightHeld = steering != null && steering.RightHeld;

        // Hamulec i manetka działają tylko na uchwycie, który aktualnie trzyma "ręka" (LPM).
        // Lewy: hamulec przedni + przerzutka przednia (1–3). Prawy: hamulec tylny + przerzutka tylna (1–7).
        bool space = kb != null && kb.spaceKey.isPressed;
        FrontBrake = leftHeld && space;
        RearBrake = rightHeld && space;

        if (kb != null && pedals != null && (leftHeld || rightHeld))
        {
            int dir = 0;
            if (kb.leftShiftKey.wasPressedThisFrame) dir += 1;   // bieg w górę (cięższy)
            if (kb.leftCtrlKey.wasPressedThisFrame) dir -= 1;    // bieg w dół (lżejszy)
            if (dir != 0)
            {
                if (leftHeld) pedals.ShiftFront(dir);
                else pedals.ShiftRear(dir);
            }
        }

        if (kb != null && kb.rKey.wasPressedThisFrame) ResetBike();
        if (transform.position.y < -10f) ResetBike();

        // Obrót kół.
        float spin = speed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
        for (int i = 0; i < spinningWheels.Length; i++)
            if (spinningWheels[i] != null) spinningWheels[i].Rotate(spin, 0f, 0f, Space.Self);

        // Przechył wynikający z przyspieszenia dośrodkowego: tan(lean) = v * omega / g
        float targetLean = Mathf.Atan(speed * yawRate / 9.81f) * Mathf.Rad2Deg;
        targetLean = Mathf.Clamp(targetLean, -maxLean, maxLean);
        lean = Mathf.Lerp(lean, targetLean, 1f - Mathf.Exp(-leanSharpness * Time.deltaTime));

        // Dodatni lean = w prawo = ujemny obrót wokół osi Z (układ lewoskrętny Unity).
        // Kamera jest dzieckiem leanPivot, więc przechyla się razem z rowerem
        // i kierownica stoi w tym samym miejscu na ekranie.
        if (leanPivot != null) leanPivot.localRotation = Quaternion.Euler(0f, 0f, -lean);
    }

    public void ResetBike()
    {
        speed = 0f;
        yaw = startYaw;
        yawRate = 0f;
        lean = 0f;
        lastForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        rb.position = startPosition;
        rb.rotation = Quaternion.Euler(0f, yaw, 0f);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (pedals != null) pedals.ResetState();
    }
}
