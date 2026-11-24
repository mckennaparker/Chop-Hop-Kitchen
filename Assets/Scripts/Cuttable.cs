using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter), typeof(SphereCollider))]
public class Cuttable : MonoBehaviour
{
    [SerializeField] Color startColor = Color.white;
    [SerializeField] Color progressColor = Color.blue;   // where the gradient ends
    [SerializeField] Color cutColor = Color.red;         // outer color of the halves
    [SerializeField] Color capColor = Color.white;       // flat cut face

    [Header("Splitting")]
    [SerializeField] Material clipMaterial;              // material using Custom/ClippedSphere
    [SerializeField] string fragmentLayer = "Fragment";
    [SerializeField] float fragmentMass = 0.25f;
    [SerializeField] float separationSpeed = 0.3f;       // m/s pushing the halves apart

    public UnityEvent onCut;

    public static readonly List<Cuttable> All = new List<Cuttable>();

    public bool IsCut => isCut;
    public SphereCollider Sphere => sphere;

    // Per-cut bookkeeping owned by Blade (assumes a single blade)
    [System.NonSerialized] public bool bladeActive;
    [System.NonSerialized] public bool bladeTouching;
    [System.NonSerialized] public Vector3 bladeSweep;
    [System.NonSerialized] public float bladeProgress;

    class Half
    {
        public GameObject go;
        public Renderer rend;
        public BoxCollider box;
        public Transform boxT;
        public Rigidbody rb;
        public MaterialPropertyBlock block;
    }

    // Low-poly sphere directions used to build half colliders
    const int Lat = 8, Lon = 16;
    static readonly Vector3[] UnitDirs = BuildDirs();
    static readonly int[] Seq = BuildSeq(256);
    static readonly List<Vector3> pts = new List<Vector3>(192);

    Renderer rend;
    SphereCollider sphere;
    MaterialPropertyBlock block;
    Half[] halves;
    bool isCut;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int CapColorId = Shader.PropertyToID("_CapColor");
    static readonly int CutPlaneId = Shader.PropertyToID("_CutPlane");
    static readonly int CutSideId = Shader.PropertyToID("_CutSide");

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
        BuildHalves();
    }

    void OnEnable() => All.Add(this);
    void OnDisable()
    {
        All.Remove(this);
        HideHalves();
    }

    void BuildHalves()
    {
        if (clipMaterial == null)
        {
            Debug.LogWarning("Cuttable: no Clip Material assigned, splitting disabled.", this);
            return;
        }

        int layer = LayerMask.NameToLayer(fragmentLayer);
        if (layer < 0)
        {
            Debug.LogWarning($"Layer '{fragmentLayer}' not found; halves will collide with the blade.", this);
            layer = gameObject.layer;
        }

        Mesh src = GetComponent<MeshFilter>().sharedMesh;
        halves = new Half[2];

        for (int i = 0; i < 2; i++)
        {
            // Not parented to this object, so pool release/reuse can't affect the halves mid-fall
            var h = new Half { go = new GameObject($"{name}_half{i}") };
            h.go.layer = layer;

            h.go.AddComponent<MeshFilter>().sharedMesh = src;
            var mr = h.go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = clipMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h.rend = mr;
            h.block = new MaterialPropertyBlock();

            var cgo = new GameObject("Collider");
            cgo.layer = layer;
            cgo.transform.SetParent(h.go.transform, false);
            h.boxT = cgo.transform;
            h.box = cgo.AddComponent<BoxCollider>();

            h.rb = h.go.AddComponent<Rigidbody>();
            h.rb.mass = fragmentMass;
            h.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            h.rb.isKinematic = true;

            h.go.SetActive(false);
            halves[i] = h;
        }
    }

    void OnDestroy()
    {
        if (halves == null) return;
        foreach (var h in halves)
            if (h != null && h.go != null) Destroy(h.go);
    }

    // Called by the spawner every time the object is (re)used from the pool
    public void Prepare(Color color)
    {
        Init();
        startColor = color;
        isCut = false;
        ClearBladeState();
        HideHalves();

        rend.enabled = true;
        sphere.enabled = true;
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

    // World-space plane through planePoint with normal planeNormal
    public void Cut(Vector3 planePoint, Vector3 planeNormal)
    {
        if (isCut) return;
        isCut = true;
        ClearBladeState();

        if (halves != null) Split(planePoint, planeNormal.normalized);
        else Apply(cutColor);

        onCut?.Invoke();
    }

    void Split(Vector3 worldPoint, Vector3 worldNormal)
    {
        // Plane in local space (assumes uniform scale)
        Vector3 nl = transform.InverseTransformDirection(worldNormal).normalized;
        Vector3 pl = transform.InverseTransformPoint(worldPoint);
        float w = -Vector3.Dot(nl, pl);

        rend.enabled = false;      // hide the whole sphere
        sphere.enabled = false;    // and stop it blocking the halves

        for (int i = 0; i < 2; i++)
        {
            Half h = halves[i];
            float side = i == 0 ? 1f : -1f;   // +1 keeps the -normal side

            BuildCollider(h, nl, w, side);

            // Match the sphere's world pose and scale so the local-space plane and hull line up
            var t = h.go.transform;
            t.SetPositionAndRotation(transform.position, transform.rotation);
            t.localScale = transform.lossyScale;

            h.block.Clear();
            h.block.SetColor(BaseColorId, cutColor);
            h.block.SetColor(CapColorId, capColor);
            h.block.SetVector(CutPlaneId, new Vector4(nl.x, nl.y, nl.z, w));
            h.block.SetFloat(CutSideId, side);
            h.rend.SetPropertyBlock(h.block);

            h.go.SetActive(true);
            h.rb.isKinematic = false;
            h.rb.WakeUp();
            SetVelocity(h.rb, -side * worldNormal * separationSpeed);
            h.rb.angularVelocity = Vector3.zero;
        }
    }

    static void SetVelocity(Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    // A box sitting on the cut face, rotated so its up axis points into the kept half.
    // Sized to fit inside the half so it never sticks out past the curved surface.
    void BuildCollider(Half h, Vector3 nl, float w, float side)
    {
        Vector3 c = sphere.center;
        float R = sphere.radius;

        float dc = Vector3.Dot(nl, c) + w;                  // center-to-plane signed distance
        Vector3 b = -side * nl;                             // direction this half bulges toward
        Vector3 planePoint = c - nl * dc;

        float thickness = Mathf.Max(0.03f * R, R - side * dc);
        float rho = Mathf.Sqrt(Mathf.Max(0f, R * R - dc * dc));

        float width = 1.25f * Mathf.Max(0.03f * R, rho * 1.4f);
        float height = 1.25f * Mathf.Max(0.02f * R, thickness * 0.7f);

        h.boxT.localRotation = Quaternion.FromToRotation(Vector3.up, b);
        h.boxT.localPosition = planePoint + b * (height * 0.5f);
        h.boxT.localScale = Vector3.one;
        h.box.center = Vector3.zero;
        h.box.size = 1.3f * new Vector3(width, height, width); //  add 1.1f because the shader will reveal the table accidentally when object is intersecting inside the table
    }
    public void HideHalves()
    {
        if (halves == null) return;
        foreach (var h in halves)
        {
            if (h == null || h.go == null) continue;
            if (h.go.activeSelf)
            {
                SetVelocity(h.rb, Vector3.zero);          // zero these before going kinematic
                h.rb.angularVelocity = Vector3.zero;
            }
            h.rb.isKinematic = true;
            h.go.SetActive(false);
        }
    }

    static Vector3[] BuildDirs()
    {
        var d = new Vector3[(Lat + 1) * Lon];
        for (int i = 0; i <= Lat; i++)
        {
            float phi = Mathf.PI * i / Lat;
            for (int j = 0; j < Lon; j++)
            {
                float th = 2f * Mathf.PI * j / Lon;
                d[i * Lon + j] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
            }
        }
        return d;
    }

    static int[] BuildSeq(int n)
    {
        var s = new int[n];
        for (int i = 0; i < n; i++) s[i] = i;
        return s;
    }

    void Apply(Color c)
    {
        rend.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        block.SetColor(ColorId, c);
        rend.SetPropertyBlock(block);
    }
}