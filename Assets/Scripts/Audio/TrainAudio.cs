using UnityEngine;

/// <summary>
/// Dźwięk pociągu (na obiekcie z TrainController, bez plików): niski szum i drgania silnika, rytmiczne stuki kół na łączeniach szyn
/// ("ta-dum") rosnące z prędkością pociągu. Źródło dźwięku jedzie z wagonem z ładunkiem i jest przestrzenne, więc cichnie z odległością
/// i słychać, z której strony jest pociąg. Aby wyciszyć pociąg, usuń komponent.
/// </summary>
[RequireComponent(typeof(TrainController))]
public class TrainAudio : MonoBehaviour
{
    [SerializeField] TrainController train;
    [SerializeField, Range(0f, 1f)] float level = 0.9f;
    [Tooltip("Do tej odległości (m) dźwięk jest pełny, potem cichnie liniowo do zera przy maksymalnej.")]
    [SerializeField] float minDistance = 12f;
    [SerializeField] float maxDistance = 260f;
    [Tooltip("Odległość (m) między łączeniami szyn – tyle metrów między kolejnymi stukami osi.")]
    [SerializeField] float railJoint = 12f;

    AudioSource source;
    Transform follow;

    // parametry (wątek główny -> audio)
    volatile float engineAmp = 0.3f, chugRate = 3f, clatterRate, clatterAmp;
    // stan generatora (wątek audio)
    readonly System.Random rng = new System.Random(9);
    float lp1, lp2, phase, chugPhase, clatterPhase, thump, thumpT, second = -1f;

    void Awake()
    {
        if (train == null) train = GetComponent<TrainController>();
        var go = new GameObject("TrainSound");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.loop = true;
        source.clip = ProcAudio.Stream("train_loop", Fill);
        source.volume = 0f;
        source.Play();
    }

    void LateUpdate()
    {
        if (train == null || source == null) return;
        if (follow == null) follow = train.CargoCar;
        if (follow != null) source.transform.position = follow.position;

        float v = Mathf.Max(0f, train.Speed);
        float k = Mathf.Clamp01(v / 12f);
        engineAmp = 0.35f + 0.5f * k;
        chugRate = 3f + v * 0.9f;
        clatterRate = v / Mathf.Max(2f, railJoint) * 2f;       // dwie osie wózka na łączenie
        clatterAmp = Mathf.Clamp01(k * 1.4f);
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = level * GameSettings.AmbientGain;
    }

    /// <summary>Wątek audio: silnik (szum brązowy z modulacją i niskim buczeniem) plus stuki kół.</summary>
    void Fill(float[] data)
    {
        float eAmp = engineAmp, chug = chugRate / ProcAudio.Rate, cRate = clatterRate / ProcAudio.Rate, cAmp = clatterAmp;
        for (int i = 0; i < data.Length; i++)
        {
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += (n - lp1) * 0.03f;
            lp2 += (lp1 - lp2) * 0.03f;                           // dwa filtry: ciemny, dudniący szum
            chugPhase += chug; if (chugPhase >= 1f) chugPhase -= 1f;
            float mod = 0.65f + 0.35f * Mathf.Sin(chugPhase * 2f * Mathf.PI);
            phase += 52f / ProcAudio.Rate; if (phase >= 1f) phase -= 1f;
            float hum = Mathf.Sin(phase * 2f * Mathf.PI) * 0.18f + Mathf.Sin(phase * 4f * Mathf.PI) * 0.08f;
            float s = (lp2 * 9f * mod + hum * mod) * eAmp;

            // stuk osi: dwa uderzenia w odstępie ~0,09 s
            clatterPhase += cRate;
            if (clatterPhase >= 1f) { clatterPhase -= 1f; thump = 1f; thumpT = 0f; second = 0.09f * ProcAudio.Rate; }
            if (second > 0f && --second <= 0f) { thump = 0.7f; thumpT = 0f; }
            if (thump > 0.002f)
            {
                thumpT += 1f / ProcAudio.Rate;
                s += (Mathf.Sin(2f * Mathf.PI * 75f * thumpT) * 0.5f + n * 0.25f * Mathf.Exp(-thumpT * 120f)) * thump * cAmp;
                thump *= 0.9993f;
            }
            data[i] = Mathf.Clamp(s, -1f, 1f);
        }
    }
}
