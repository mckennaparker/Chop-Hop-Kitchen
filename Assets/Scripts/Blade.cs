using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


public class Blade : MonoBehaviour
{
    [Tooltip("Point on the blade that does the cutting. Defaults to this transform.")]
    [SerializeField] Transform edgePoint;

    XRGrabInteractable grab;
    Vector3 prevEdge;

    SphereCollider target;
    Cuttable cuttable;
    bool active;
    bool done;
    Vector3 entry;

    void Awake()
    {
        grab = GetComponentInParent<XRGrabInteractable>();
        if (edgePoint == null) edgePoint = transform;
        prevEdge = edgePoint.position;
    }

    void OnTriggerStay(Collider other)
    {
        if (target == null && other is SphereCollider sc)
        {
            var c = sc.GetComponentInParent<Cuttable>();
            if (c == null) return;
            target = sc;
            cuttable = c;
            active = done = false;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other != target) return;
        if (active && grab.isSelected)
        {
            GetSphere(target, out Vector3 c, out _);
            UpdateCut(c, edgePoint.position);
        }
        Cancel();
        target = null;
        cuttable = null;
        done = false;
    }

    void Update()
    {
        Vector3 edge = edgePoint.position;
        if (target != null) Step(edge);
        prevEdge = edge;
    }

    bool UpdateCut(Vector3 c, Vector3 edge)
    {
        Vector3 delta = edge - entry;
        float depth = delta.magnitude;
        if (depth < 1e-4f) return false;

        Vector3 dir = delta / depth;
        float chord = -2f * Vector3.Dot(dir, entry - c);
        if (chord <= 0f) return false;

        if (depth >= chord - 0.001f)   // 1 mm tolerance for the boundary case
        {
            cuttable.Cut();
            done = true;
            return true;
        }

        cuttable.SetProgress(depth / chord);
        return false;
    }
    void Step(Vector3 edge)
    {
        GetSphere(target, out Vector3 c, out float r);
        bool inside = (edge - c).sqrMagnitude <= r * r;

        if (!inside)
        {
            if (active && grab.isSelected) UpdateCut(c, edge);
            Cancel();
            done = false;
            return;
        }
        if (!grab.isSelected) { Cancel(); return; }
        if (done) return;

        if (!active)
        {
            entry = FindEntry(c, r, prevEdge, edge);
            active = true;
            cuttable.SetProgress(0f);
            return;
        }

        if (UpdateCut(c, edge)) active = false;
    }

    void Cancel()
    {
        active = false;
        if (cuttable != null) cuttable.ResetProgress();
    }

    static void GetSphere(SphereCollider sc, out Vector3 center, out float radius)
    {
        center = sc.transform.TransformPoint(sc.center);
        Vector3 s = sc.transform.lossyScale;
        radius = sc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    static Vector3 FindEntry(Vector3 c, float r, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float A = Vector3.Dot(d, d);
        if (A > 1e-10f && (a - c).sqrMagnitude > r * r)
        {
            Vector3 f = a - c;
            float B = 2f * Vector3.Dot(f, d);
            float C = Vector3.Dot(f, f) - r * r;
            float disc = B * B - 4f * A * C;
            if (disc >= 0f)
            {
                float t = (-B - Mathf.Sqrt(disc)) / (2f * A);
                if (t >= 0f && t <= 1f) return a + d * t;
            }
        }
        Vector3 n = b - c;
        return n.sqrMagnitude > 1e-10f ? c + n.normalized * r : b;
    }

    void OnDisable()
    {
        Cancel();
        target = null;
        cuttable = null;
        active = done = false;
    }
}