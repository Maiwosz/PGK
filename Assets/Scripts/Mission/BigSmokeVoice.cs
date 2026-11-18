using UnityEngine;

/// <summary>
/// Komentarze Big Smoke'a (syntezator mowy, bez plików dźwiękowych): zabicie gangstera, niestabilna jazda, pociąg się oddala,
/// upadek roweru, pochwała za płynną jazdę. Dba, żeby nie gadał bez przerwy: globalna przerwa między kwestiami i osobny
/// cooldown każdej kategorii; ostrzeżenia o ucieczce pociągu mają pierwszeństwo. Teksty są własne (wzorowane na tonie postaci).
/// Komponent jest niezależny – wystarczy go usunąć, żeby wyciszyć komentarze.
/// </summary>
public class BigSmokeVoice : MonoBehaviour
{
    [SerializeField] MissionManager mission;
    [SerializeField] AutoTurret turret;
    [SerializeField] BikeController bike;

    [Header("Przerwy (s)")]
    [Tooltip("Minimalna przerwa między dowolnymi dwiema kwestiami.")]
    [SerializeField] float globalGap = 6f;
    [Tooltip("Krótsza przerwa dla pilnych ostrzeżeń (pociąg ucieka).")]
    [SerializeField] float urgentGap = 3f;
    [SerializeField] float unstableCooldown = 15f;
    [SerializeField] float praiseCooldown = 25f;
    [SerializeField] float distanceCooldown = 8f;
    [SerializeField] float fallCooldown = 12f;
    [SerializeField] float encourageCooldown = 20f;

    [Header("Progi")]
    [Tooltip("Stabilność poniżej tej wartości, utrzymana przez 'unstableTime', wywołuje uwagę.")]
    [SerializeField, Range(0f, 1f)] float unstableBelow = 0.35f;
    [SerializeField] float unstableTime = 1.2f;
    [Tooltip("Stabilność powyżej tej wartości przy serii trafień wywołuje pochwałę.")]
    [SerializeField, Range(0f, 1f)] float praiseAbove = 0.7f;
    [SerializeField] int praiseHits = 5;
    [SerializeField] float praiseWindow = 4f;
    [SerializeField] float fallenTime = 1f;
    [Tooltip("Jak długo (s) wieżyczka musi nie mieć celu w zasięgu, zanim Smoke zachęci do podjechania.")]
    [SerializeField] float outOfRangeTime = 5f;

    static readonly string[] Voices = { "David", "Mark" };

    static readonly string[] Kill =
    {
        "That's one Vagos asshole gone!",
        "Man, he ain't gonna be eatin' shit for a while.",
    };
    static readonly string[] LastKill = { "That's the last one! Big Smoke don't miss!" };
    static readonly string[] Unstable =
    {
        "Hold it steady, CJ! I can't hit shit like this!",
        "You're shakin' me like a Chicken Box special!",
        "Easy on the wheel, homie, I'm tryin' to aim here!",
        "My hand's gotta be steady, CJ! Steady!",
    };
    static readonly string[] Praise =
    {
        "That's what I'm talkin' about, smooth like butter!",
        "Keep it right there, CJ, I got 'em!",
        "Now we're cookin'! Stay on that line!",
    };
    static readonly string[] FarWarn = { "Follow that train!", "Go after that loco, CJ!" };
    static readonly string[] FarCritical = { "We're losin' 'em, CJ! Pedal, fool, pedal!", "Damn, you lost 'em CJ!" };
    static readonly string[] Fall = { "CJ, get up, fool!" };
    static readonly string[] Encourage = { "Get me close, CJ. I'm about to pop these fools." };

    float nextAny, nextUnstable, nextPraise, nextDistance, nextFall, nextEncourage;
    float unstableTimer, fallenTimer, outOfRangeTimer, farTimer;
    int lastHits;
    float windowStart;
    int windowHits;

    void Awake()
    {
        if (mission == null) mission = GetComponent<MissionManager>();
        if (mission == null) mission = FindFirstObjectByType<MissionManager>();
        if (turret == null) turret = FindFirstObjectByType<AutoTurret>();
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
    }

    void OnEnable() => Gangster.AnyDied += OnGangsterDied;

    void OnDisable() => Gangster.AnyDied -= OnGangsterDied;

    void OnGangsterDied(Gangster g)
    {
        if (mission == null || mission.CurrentPhase != MissionManager.Phase.Running || mission.Waiting) return;
        int alive = 0;
        foreach (var x in Gangster.All) if (x.Alive) alive++;
        // Ostatni gangster: kwestia o zabiciu ma pierwszeństwo, bo zaraz kończy się misja.
        Say(alive == 0 ? LastKill : Kill, true);
    }

    void Update()
    {
        if (mission == null || turret == null || bike == null || Time.timeScale == 0f) return;
        if (mission.CurrentPhase != MissionManager.Phase.Running || mission.Waiting)
        {
            ResetTimers();
            return;
        }
        float dt = Time.deltaTime, now = Time.time;

        // Niestabilna jazda: musi trwać dłuższą chwilę, a nie jedno szarpnięcie.
        if (turret.Stability < unstableBelow && !bike.Fallen) unstableTimer += dt; else unstableTimer = 0f;
        if (unstableTimer >= unstableTime && now >= nextUnstable)
            if (Say(Unstable, false)) { nextUnstable = now + unstableCooldown; unstableTimer = 0f; }

        // Pochwała: seria trafień przy płynnej jeździe.
        if (turret.Hits != lastHits)
        {
            if (turret.Hits > lastHits)
            {
                if (now - windowStart > praiseWindow) { windowStart = now; windowHits = 0; }
                windowHits += turret.Hits - lastHits;
            }
            lastHits = turret.Hits;
        }
        if (windowHits >= praiseHits && now - windowStart <= praiseWindow && turret.Stability > praiseAbove && now >= nextPraise)
        {
            if (Say(Praise, false)) nextPraise = now + praiseCooldown;
            windowHits = 0;
        }

        // Pociąg ucieka: ostrzeżenie przy dystansie ostrzegawczym, pilniejsze za granicą porażki.
        float d = mission.TrainDistance;
        bool critical = d >= mission.LoseDistance;
        bool warn = d >= mission.WarnDistance;
        farTimer = warn ? farTimer + dt : 0f;
        if (warn && farTimer >= 1f && now >= nextDistance)
            if (Say(critical ? FarCritical : FarWarn, critical)) nextDistance = now + distanceCooldown;

        // Upadek roweru.
        fallenTimer = bike.Fallen ? fallenTimer + dt : 0f;
        if (fallenTimer >= fallenTime && now >= nextFall)
            if (Say(Fall, false)) nextFall = now + fallCooldown;

        // Brak celu w zasięgu: zachęta do podjechania.
        outOfRangeTimer = turret.State == AutoTurret.TurretState.OutOfRange ? outOfRangeTimer + dt : 0f;
        if (outOfRangeTimer >= outOfRangeTime && !warn && now >= nextEncourage)
            if (Say(Encourage, false)) nextEncourage = now + encourageCooldown;
    }

    void ResetTimers()
    {
        unstableTimer = fallenTimer = outOfRangeTimer = farTimer = 0f;
        windowHits = 0;
        lastHits = turret != null ? turret.Hits : 0;
    }

    /// <summary>Wypowiada losową kwestię, jeśli minęła przerwa (pilne: krótsza przerwa). Zwraca, czy coś powiedział.</summary>
    bool Say(string[] lines, bool urgent)
    {
        float now = Time.time;
        float gap = urgent ? urgentGap : globalGap;
        if (now < nextAny - globalGap + gap) return false;
        SpeechSynth.Speak(lines[Random.Range(0, lines.Length)], Voices, 0);
        nextAny = now + globalGap;
        return true;
    }
}
