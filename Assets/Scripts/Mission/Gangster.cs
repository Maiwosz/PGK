using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gangster stojący na dachu wagonu (żółta bryła – kolor gangu). Ma punkty życia; po trafieniu mignie,
/// po śmierci przewraca się i znika. Wszystkie aktywne obiekty są w liście <see cref="All"/>.
/// </summary>
public class Gangster : MonoBehaviour
{
    public static readonly List<Gangster> All = new List<Gangster>();
    /// <summary>Wywoływane po zabiciu gangstera (dla statystyk misji).</summary>
    public static event Action<Gangster> AnyDied;

    [SerializeField] float maxHealth = 100f;
    [Tooltip("Środek tułowia – tu celuje wieżyczka.")]
    [SerializeField] Transform aimPoint;
    [SerializeField] Renderer[] renderers;
    [SerializeField] Collider[] colliders;
    [Tooltip("Czas (s) przewracania się po śmierci.")]
    [SerializeField] float fallTime = 0.4f;
    [Tooltip("Po ilu sekundach od śmierci ciało znika.")]
    [SerializeField] float vanishDelay = 1.6f;

    float health;
    float flash;
    float deadTime;
    Vector3 startLocalPos;
    Quaternion startLocalRot;
    MaterialPropertyBlock block;
    Color[] baseColors;
    Rigidbody carrier;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public bool Alive => health > 0f;
    public float Health01 => Mathf.Clamp01(health / maxHealth);
    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position + Vector3.up * 1.2f;
    public Vector3 HeadPosition => transform.position + Vector3.up * 1.9f;
    /// <summary>Ciało wagonu, na którym stoi gangster (jego kolizja nie zasłania celu).</summary>
    public Rigidbody Carrier => carrier;

    void Awake()
    {
        health = maxHealth;
        startLocalPos = transform.localPosition;
        startLocalRot = transform.localRotation;
        block = new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            baseColors[i] = renderers[i].sharedMaterial.HasProperty(BaseColor) ? renderers[i].sharedMaterial.GetColor(BaseColor) : Color.yellow;
    }

    void OnEnable()
    {
        All.Add(this);
        carrier = GetComponentInParent<Rigidbody>();
    }

    void OnDisable() => All.Remove(this);

    /// <summary>Zadaje obrażenia; zwraca true, jeśli to trafienie zabiło gangstera.</summary>
    public bool TakeDamage(float amount)
    {
        if (!Alive) return false;
        health -= amount;
        flash = 1f;
        if (health > 0f) return false;
        health = 0f;
        deadTime = 0f;
        foreach (var c in colliders) if (c != null) c.enabled = false;
        AnyDied?.Invoke(this);
        return true;
    }

    /// <summary>Przywraca gangstera (restart misji).</summary>
    public void ResetState()
    {
        health = maxHealth;
        flash = 0f;
        deadTime = 0f;
        transform.localPosition = startLocalPos;
        transform.localRotation = startLocalRot;
        foreach (var r in renderers) if (r != null) { r.enabled = true; r.SetPropertyBlock(null); }
        foreach (var c in colliders) if (c != null) c.enabled = true;
    }

    void Update()
    {
        if (flash > 0f)
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 8f);
            for (int i = 0; i < renderers.Length; i++)
            {
                block.SetColor(BaseColor, Color.Lerp(baseColors[i], Color.white, flash));
                renderers[i].SetPropertyBlock(block);
            }
        }

        if (!Alive)
        {
            deadTime += Time.deltaTime;
            // Przewraca się do tyłu (w stronę dachu), potem znika.
            float t = Mathf.Clamp01(deadTime / Mathf.Max(0.05f, fallTime));
            transform.localRotation = startLocalRot * Quaternion.Euler(0f, 0f, 90f * t * t);
            if (deadTime > vanishDelay)
                foreach (var r in renderers) if (r != null) r.enabled = false;
        }
    }
}
