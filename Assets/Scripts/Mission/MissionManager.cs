using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Przebieg misji "Wrong Side of the Tracks": zabij gangsterów na pierwszym wagonie, nie gubiąc pociągu.
/// Wygrana – wszyscy gangsterzy pokonani. Porażka – pociąg odjechał za daleko od roweru albo przejechał przez most.
/// Po zakończeniu gracz traci kontrolę, widzi status i statystyki, a R restartuje misję.
/// </summary>
[DefaultExecutionOrder(100)]   // po TrainController.Start, który tworzy gangsterów
public class MissionManager : MonoBehaviour
{
    public enum Phase { Running, Won, Lost }
    public enum LossReason { None, TooFar, Bridge }

    public static MissionManager Instance { get; private set; }
    static int attempt;

    [Header("Referencje")]
    [SerializeField] BikeController bike;
    [SerializeField] TrainController train;
    [SerializeField] AutoTurret turret;
    [Tooltip("Broń ręczna gracza (statystyki strzałów). Puste = pierwsza znaleziona w scenie.")]
    [SerializeField] Gun gun;
    [SerializeField] TrackPath path;

    [Header("Porażka – odległość od pociągu")]
    [Tooltip("Od tej odległości (m) od najbliższego wagonu pojawia się ostrzeżenie.")]
    [SerializeField] float warnDistance = 55f;
    [Tooltip("Powyżej tej odległości (m) zaczyna się odliczanie do porażki.")]
    [SerializeField] float loseDistance = 90f;
    [Tooltip("Ile sekund trzeba być za daleko, żeby przegrać.")]
    [SerializeField] float loseGrace = 3f;

    [Header("Porażka – most")]
    [Tooltip("Najkrótszy odcinek 'most', który liczy się jako most Frederick (m).")]
    [SerializeField] float bridgeMinLength = 150f;

    [Header("Mowa")]
    [SerializeField] string introLine = "Aw shit, here we go again.";
    [SerializeField] string loseLine = "All we had to do was follow the damn train, CJ!";

    [Header("Debug")]
    [Tooltip("Wyłącza przegranie misji (zbyt duży dystans do pociągu, przejechanie przez most) – do testowania. Przełącznik: F9 (tylko w edytorze i buildach deweloperskich).")]
    [SerializeField] bool disableLosing;
    public bool LosingDisabled => disableLosing;

    public Phase CurrentPhase { get; private set; } = Phase.Running;
    public LossReason Reason { get; private set; }
    public int Attempt => attempt;
    public float MissionTime { get; private set; }
    public float BridgeEndDistance { get; private set; }
    public float DistanceToBridgeEnd => train != null ? Mathf.Max(0f, BridgeEndDistance - train.FrontDistance) : 0f;
    public float TrainDistance { get; private set; }
    public float WarnDistance => warnDistance;
    public float LoseDistance => loseDistance;
    /// <summary>Ile sekund zostało do porażki za dużą odległość (poza odliczaniem = pełny limit).</summary>
    public float LoseCountdown => Mathf.Max(0f, loseGrace - farTimer);
    public bool Counting => farTimer > 0f && CurrentPhase == Phase.Running;

    // statystyki
    public float DistanceRidden { get; private set; }
    public float MaxSpeedKmh { get; private set; }
    public float ClosestApproach { get; private set; }
    public int Falls => bike != null ? bike.FallCount - fallBase : 0;
    public int TurretShots => turret != null ? turret.ShotsFired : 0;
    public int TurretHits => turret != null ? turret.Hits : 0;
    public int GunShots => gun != null ? gun.ShotsFired : 0;
    public int GunHits => gun != null ? gun.Hits : 0;
    public bool HasGun => gun != null;
    // łącznie: wieżyczka + broń ręczna
    public int ShotsFired => TurretShots + GunShots;
    public int Hits => TurretHits + GunHits;
    public float Accuracy => ShotsFired > 0 ? Hits / (float)ShotsFired : 0f;
    public readonly List<float> KillTimes = new List<float>();
    public int TotalGangsters { get; private set; }
    public int AliveGangsters { get; private set; }
    public float BestTime { get; private set; }
    public bool NewRecord { get; private set; }

    float farTimer;
    Vector3 lastBikePos;
    int fallBase;
    const string BestKey = "PGK_BestTime";

    void Awake()
    {
        Instance = this;
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
        if (train == null) train = FindFirstObjectByType<TrainController>();
        if (turret == null) turret = FindFirstObjectByType<AutoTurret>();
        if (gun == null) gun = FindFirstObjectByType<Gun>();
        if (path == null) path = FindFirstObjectByType<TrackPath>();
    }

    void OnEnable() => Gangster.AnyDied += OnGangsterDied;
    void OnDisable()
    {
        Gangster.AnyDied -= OnGangsterDied;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        BridgeEndDistance = FindBridgeEnd();
        BestTime = PlayerPrefs.GetFloat(BestKey, 0f);
        TotalGangsters = Gangster.All.Count;
        BeginRun();
    }

    /// <summary>Dystans (m wzdłuż trasy), na którym kończy się ostatni długi most – czoło pociągu za tym punktem oznacza porażkę.</summary>
    float FindBridgeEnd()
    {
        if (path == null) return float.MaxValue;
        int n = path.Count;
        float d = 0f, runStart = 0f, lastEnd = path.Length;
        bool found = false;
        bool inBridge = false;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) d += Vector3.Distance(path.PointAt(i - 1), path.PointAt(i));
            bool bridge = path.KindAt(i) == TrackPath.Kind.Bridge;
            if (bridge && !inBridge) { inBridge = true; runStart = d; }
            if ((!bridge || i == n - 1) && inBridge)
            {
                inBridge = false;
                if (d - runStart >= bridgeMinLength) { lastEnd = d; found = true; }
            }
        }
        return found ? lastEnd : path.Length;
    }

    void BeginRun()
    {
        attempt++;
        CurrentPhase = Phase.Running;
        Reason = LossReason.None;
        MissionTime = 0f;
        farTimer = 0f;
        DistanceRidden = 0f;
        MaxSpeedKmh = 0f;
        ClosestApproach = float.MaxValue;
        KillTimes.Clear();
        NewRecord = false;
        fallBase = bike != null ? bike.FallCount : 0;
        lastBikePos = bike != null ? bike.transform.position : Vector3.zero;
        AliveGangsters = CountAlive();
        Speak(introLine, new[] { "David" });
    }

    /// <summary>Restart misji od początku: rower, pociąg, gangsterzy, wieżyczka i statystyki.</summary>
    public void Restart()
    {
        SpeechSynth.Stop();
        if (bike != null) { bike.ControlLocked = false; bike.ResetBike(); }
        if (train != null) train.ResetTrain();
        foreach (var g in Gangster.All) g.ResetState();
        if (turret != null) turret.ResetTurret();
        if (gun != null) gun.ResetStats();
        BeginRun();
    }

    void Update()
    {
        if (bike == null || train == null) return;
        float dt = Time.deltaTime;
        if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            disableLosing = !disableLosing;
            farTimer = 0f;
        }
        if (CurrentPhase != Phase.Running) return;

        MissionTime += dt;
        TrainDistance = train.DistanceTo(bike.transform.position);
        ClosestApproach = Mathf.Min(ClosestApproach, TrainDistance);
        MaxSpeedKmh = Mathf.Max(MaxSpeedKmh, bike.SpeedKmh);
        Vector3 p = bike.transform.position;
        float step = Vector3.Distance(p, lastBikePos);
        if (step < 5f) DistanceRidden += step;
        lastBikePos = p;

        AliveGangsters = CountAlive();
        if (TotalGangsters > 0 && AliveGangsters == 0) { Win(); return; }

        if (disableLosing) { farTimer = 0f; return; }

        if (TrainDistance > loseDistance) farTimer += dt;
        else farTimer = Mathf.Max(0f, farTimer - dt * 2f);
        if (farTimer >= loseGrace) { Lose(LossReason.TooFar); return; }

        if (train.FrontDistance >= BridgeEndDistance) Lose(LossReason.Bridge);
    }

    int CountAlive()
    {
        int c = 0;
        foreach (var g in Gangster.All) if (g.Alive) c++;
        return c;
    }

    void OnGangsterDied(Gangster g)
    {
        if (CurrentPhase == Phase.Running) KillTimes.Add(MissionTime);
    }

    void Win()
    {
        CurrentPhase = Phase.Won;
        EndCommon();
        if (train != null) train.Halted = true;
        if (BestTime <= 0f || MissionTime < BestTime)
        {
            NewRecord = true;
            BestTime = MissionTime;
            PlayerPrefs.SetFloat(BestKey, BestTime);
            PlayerPrefs.Save();
        }
    }

    void Lose(LossReason reason)
    {
        CurrentPhase = Phase.Lost;
        Reason = reason;
        EndCommon();
        Speak(loseLine, new[] { "Mark", "David" });
    }

    void EndCommon()
    {
        if (bike != null) bike.ControlLocked = true;
        if (turret != null) turret.Active = false;
        farTimer = 0f;
    }

    static void Speak(string line, string[] voices) => SpeechSynth.Speak(line, voices, 0);

    public string ReasonText => Reason == LossReason.TooFar ? "Pociąg odjechał za daleko."
                              : Reason == LossReason.Bridge ? "Pociąg przejechał przez most."
                              : "";
}
