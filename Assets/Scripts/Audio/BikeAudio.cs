using UnityEngine;

/// <summary>
/// Dźwięki roweru (na obiekcie Bike, bez plików dźwiękowych):
///  - szum opon i wiatru rosnący z prędkością oraz tykanie wolnobiegu, gdy nie pedałujemy (pętla generowana w locie),
///  - dźwięk błędu pedałowania jako informacja zwrotna: poprawne pedałowanie jest ciche, a błąd daje zgrzyt łańcucha –
///    krótki i lżejszy przy za wczesnym / za późnym kliknięciu, dłuższy i ostrzejszy przy złej nodze i spamie.
/// Głośności bierze z <see cref="GameSettings"/>. Aby wyciszyć rower, usuń komponent.
/// </summary>
[RequireComponent(typeof(PedalDrive))]
public class BikeAudio : MonoBehaviour
{
    [SerializeField] BikeController bike;
    [SerializeField] PedalDrive pedals;

    [Header("Poziomy (przed ustawieniami gracza)")]
    [SerializeField, Range(0f, 1f)] float loopLevel = 0.35f;
    [SerializeField, Range(0f, 1f)] float pedalLevel = 0.8f;
    [Tooltip("Po ilu sekundach bez pedałowania zaczyna tykać wolnobieg.")]
    [SerializeField] float coastDelay = 1.2f;
    [Tooltip("Prędkość (m/s), przy której szum jest już pełny.")]
    [SerializeField] float fullSpeed = 14f;

    AudioSource loopSource, sfxSource;
    float lastPush = -99f, clickSmooth;

    // parametry pętli (zapisuje wątek główny, czyta wątek audio)
    volatile float windAmp, windCut = 0.02f, clickRate, clickAmp;
    // stan generatora (tylko wątek audio)
    readonly System.Random rng = new System.Random(5);
    float lp, clickPhase, clickEnv, clickPrev;

    void Awake()
    {
        if (bike == null) bike = GetComponent<BikeController>();
        if (pedals == null) pedals = GetComponent<PedalDrive>();

        loopSource = gameObject.AddComponent<AudioSource>();
        loopSource.spatialBlend = 0f;
        loopSource.loop = true;
        loopSource.clip = ProcAudio.Stream("bike_loop", FillLoop);
        loopSource.volume = 0f;
        loopSource.Play();

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.spatialBlend = 0f;
        sfxSource.playOnAwake = false;
    }

    void OnEnable()
    {
        if (pedals == null) pedals = GetComponent<PedalDrive>();
        pedals.Judged += OnJudged;
    }

    void OnDisable()
    {
        if (pedals != null) pedals.Judged -= OnJudged;
    }

    void Update()
    {
        if (bike == null || pedals == null) return;
        float v = Mathf.Max(0f, bike.Speed);
        float k = Mathf.Clamp01(v / fullSpeed);
        bool down = bike.Fallen;
        windAmp = down ? 0f : Mathf.Pow(k, 1.3f) * 1.3f;
        windCut = Mathf.Lerp(0.02f, 0.16f, k);
        // Wolnobieg tyka dopiero, gdy naprawdę przestaliśmy pedałować: przerwy między skokami (zwłaszcza w trybie przytrzymania)
        // nie liczą się, więc przy normalnej jeździe tykania nie ma.
        if (pedals.Pushing || pedals.Engaged) lastPush = Time.time;
        bool coasting = Time.time - lastPush > coastDelay && !down && v > 0.8f;
        clickRate = v * 7f;                       // tykanie zapadki wolnobiegu rośnie z prędkością koła
        float targetClick = coasting ? 0.22f * Mathf.Clamp01(v / 4f) : 0f;
        clickSmooth = Mathf.MoveTowards(clickSmooth, targetClick, Time.deltaTime * 0.5f);
        clickAmp = clickSmooth;
        loopSource.volume = loopLevel * GameSettings.AmbientGain;
    }

    void OnJudged(PedalDrive.Foot foot, PedalDrive.Judgement j, float error, float mult)
    {
        switch (j)
        {
            case PedalDrive.Judgement.Perfect:
            case PedalDrive.Judgement.Good:
            case PedalDrive.Judgement.Start:
                break;     // poprawne pedałowanie jest ciche – dźwięk słychać tylko przy błędzie
            case PedalDrive.Judgement.Early:
            case PedalDrive.Judgement.Late:
                Play(ProcAudio.Clip("grind_light", () => ProcAudio.Grind(0.16f, 70f, 0.3f, 0.45f)), 0.8f);
                break;
            default:      // Mash, WrongFoot
                Play(ProcAudio.Clip("grind_hard", () => ProcAudio.Grind(0.3f, 55f, 0.5f, 0.75f)), 1f);
                break;
        }
    }

    void Play(AudioClip clip, float gain)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(pedalLevel * gain * GameSettings.PedalingGain));
    }

    /// <summary>Wątek audio: szum filtrowany dolnoprzepustowo (wiatr, opony) plus tykanie wolnobiegu.</summary>
    void FillLoop(float[] data)
    {
        float amp = windAmp, cut = windCut, rate = clickRate / ProcAudio.Rate, cAmp = clickAmp;
        for (int i = 0; i < data.Length; i++)
        {
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (n - lp) * cut;
            float s = lp * amp;
            clickPhase += rate;
            if (clickPhase >= 1f) { clickPhase -= 1f; clickEnv = 1f; }
            if (clickEnv > 0.001f)
            {
                float hp = n - clickPrev;
                s += hp * clickEnv * cAmp;
                clickEnv *= 0.992f;
            }
            clickPrev = n;
            data[i] = Mathf.Clamp(s, -1f, 1f);
        }
    }
}
