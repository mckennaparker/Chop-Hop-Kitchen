using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Renderer), typeof(SphereCollider))]
public class Cuttable : MonoBehaviour
{
    [SerializeField] Color startColor = Color.white;
    [SerializeField] Color progressColor = Color.blue;   // where the gradient ends
    [SerializeField] Color cutColor = Color.red;         // shown once fully cut

    public UnityEvent onCut;   // hook up particles, sounds, etc. in the Inspector

    // Every enabled Cuttable. Pooled (inactive) ones drop out automatically.
    public static readonly List<Cuttable> All = new List<Cuttable>();

    public bool IsCut => isCut;
    public SphereCollider Sphere => sphere;

    // Per-cut bookkeeping owned by Blade (assumes a single blade)
    [System.NonSerialized] public bool bladeActive;
    [System.NonSerialized] public bool bladeTouching;
    [System.NonSerialized] public Vector3 bladeSweep;
    [System.NonSerialized] public float bladeProgress;

    Renderer rend;
    SphereCollider sphere;
    MaterialPropertyBlock block;
    bool isCut;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void Awake() => Init();

    void Init()
    {
        if (rend != null) return;
        rend = GetComponent<Renderer>();
        sphere = GetComponent<SphereCollider>();
        block = new MaterialPropertyBlock();
        Apply(startColor);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Called by the spawner every time the object is (re)used from the pool
    public void Prepare(Color color)
    {
        Init();
        startColor = color;
        isCut = false;
        ClearBladeState();
        Apply(startColor);
    }

    public void ClearBladeState()
    {
        bladeActive = false;
        bladeTouching = false;
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