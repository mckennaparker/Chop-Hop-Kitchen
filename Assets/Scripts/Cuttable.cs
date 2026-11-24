using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Renderer))]
public class Cuttable : MonoBehaviour
{
    [SerializeField] Color startColor = Color.white;
    [SerializeField] Color progressColor = Color.blue;   // where the gradient ends
    [SerializeField] Color cutColor = Color.red;         // shown once fully cut

    public UnityEvent onCut;   // hook up particles, sounds, etc. in the Inspector

    Renderer rend;
    MaterialPropertyBlock block;
    bool isCut;
    public bool IsCut => isCut;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        Apply(startColor);
    }

    // Lets the spawner assign a per-ticket color
    public void SetStartColor(Color c)
    {
        startColor = c;
        if (!isCut) Apply(startColor);
    }

    public void SetProgress(float t)
    {
        if (isCut) return;
        Apply(Color.Lerp(startColor, progressColor, Mathf.Clamp01(t)));
    }

    public void ResetProgress()
    {
        if (isCut) return;
        Apply(startColor);
    }

    public void Cut()
    {
        if (isCut) return;
        isCut = true;
        Apply(cutColor);
        onCut?.Invoke();
    }

    void Apply(Color c)
    {
        rend.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        block.SetColor(ColorId, c);
        rend.SetPropertyBlock(block);
    }
}