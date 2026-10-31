using UnityEngine;

/// <summary>
/// Uchwyt kierownicy. Collider (trigger) na tym obiekcie służy do "łapania" myszką,
/// a ten komponent zmienia kolor chwytu w zależności od stanu (neutralny / najechany / chwycony).
/// </summary>
[RequireComponent(typeof(Collider))]
public class GripHandle : MonoBehaviour
{
    public enum State { Idle, Hover, Grabbed }

    [SerializeField] Renderer targetRenderer;
    [SerializeField] Color idleColor = new Color(0.08f, 0.08f, 0.08f);
    [SerializeField] Color hoverColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] Color grabbedColor = new Color(0.25f, 0.95f, 0.35f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    MaterialPropertyBlock block;
    State state = State.Idle;

    public State Current => state;

    void Awake() => Apply();

    public void SetState(State newState)
    {
        if (state == newState) return;
        state = newState;
        Apply();
    }

    void Apply()
    {
        if (targetRenderer == null) return;
        block ??= new MaterialPropertyBlock();

        Color c = state switch
        {
            State.Hover => hoverColor,
            State.Grabbed => grabbedColor,
            _ => idleColor
        };

        targetRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        block.SetColor(ColorId, c);
        targetRenderer.SetPropertyBlock(block);
    }
}
