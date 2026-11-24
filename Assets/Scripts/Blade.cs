using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Blade : MonoBehaviour
{
    [Tooltip("The two ends of the sharp edge")]
    [SerializeField] Transform edgeBase;
    [SerializeField] Transform edgeTip;
    [SerializeField] Conductor conductor;
    [SerializeField, Range(1, 8)] int substeps = 4;

    [Tooltip("Cut completes when the edge is within this distance (meters) of the far surface")]
    [SerializeField] float completionMargin = 0.01f;

    XRGrabInteractable grab;
    Vector3 prevA, prevB;

    SphereCollider target;
    Cuttable cuttable;
    bool active;          // cut in progress
    bool done;            // already cut; re-arm once contact ends
    bool pendingRelease;
    Vector3 n;            // sweep direction, fixed when the cut begins

    void Awake()
    {
        grab = GetComponentInParent<XRGrabInteractable>();
        prevA = edgeBase.position;
        prevB = edgeTip.position;
    }

    // The trigger only chooses which sphere to watch
    void OnTriggerStay(Collider other)
    {
        if (target == null && other is SphereCollider sc)
        {
            var c = sc.GetComponentInParent<Cuttable>();
            if (c == null || c.IsCut) return;
            target = sc;
            cuttable = c;
            active = done = pendingRelease = false;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (target != null && other == target) pendingRelease = true;
    }

    void LateUpdate()
    {
        Vector3 a = edgeBase.position, b = edgeTip.position;

        if (target != null)
        {
            for (int i = 1; i <= substeps; i++)
            {
                float t0 = (i - 1) / (float)substeps, t = i / (float)substeps;
                Vector3 a0 = Vector3.Lerp(prevA, a, t0), b0 = Vector3.Lerp(prevB, b, t0);
                Vector3 a1 = Vector3.Lerp(prevA, a, t), b1 = Vector3.Lerp(prevB, b, t);
                Vector3 motion = (a1 + b1) * 0.5f - (a0 + b0) * 0.5f;

                Step(a1, b1, motion);
                if (target == null) break;
            }

            if (pendingRelease && target != null)
            {
                Cancel();
                target = null;
                cuttable = null;
                pendingRelease = false;
            }
        }

        prevA = a;
        prevB = b;
    }

    void Step(Vector3 a, Vector3 b, Vector3 motion)
    {
        GetSphere(target, out Vector3 c, out float r);

        Vector3 p = ClosestOnSegment(a, b, c);   // point on the edge nearest the sphere center
        Vector3 w = p - c;
        bool touching = w.sqrMagnitude <= r * r;

        if (done) { if (!touching) done = false; return; }
        if (!grab.isSelected) { Cancel(); return; }

        Vector3 u = (b - a).normalized;

        // --- Begin: the edge touches the sphere while moving ---
        if (!active)
        {
            if (!touching) return;

            Vector3 m = motion - u * Vector3.Dot(motion, u);   // sweep direction, minus sliding along the edge
            if (m.sqrMagnitude < 1e-10f) return;
            Vector3 dir = m.normalized;

            if (Vector3.Dot(w, dir) > 0f) return;              // already past the center: not entering

            n = dir;
            active = true;
            cuttable.SetProgress(0f);
            return;
        }

        // --- Cutting ---
        float s = Vector3.Dot(w, n);                           // position along the sweep, 0 = center
        float q2 = w.sqrMagnitude - s * s;
        float e = Mathf.Sqrt(Mathf.Max(0f, r * r - q2));       // far surface at +e, near surface at -e

        // Rhythm-game forgiveness: count the hit slightly before the edge fully exits
        if (s > 0f && e - s <= completionMargin)
        {
            Complete(p, u);
            return;
        }

        if (!touching)
        {
            if (s > 0f) Complete(p, u);
            else Cancel();
            return;
        }

        cuttable.SetProgress(e > 1e-4f ? Mathf.Clamp01((s + e) / (2f * e)) : 1f);
    }

    void Complete(Vector3 point, Vector3 edgeDir)
    {
        var cut = cuttable;
        if (cut == null || cut.IsCut) return;

        Vector3 normal = Vector3.Cross(edgeDir, n).normalized;

        active = false;
        done = true;
        target = null;
        cuttable = null;
        pendingRelease = false;

        cut.Cut();   // or cut.Cut(point, normal) if you're using the splitting version
        if (conductor != null) conductor.RegisterAction(ActionType.Chop);
    }

    void Cancel()
    {
        active = false;
        if (cuttable != null) cuttable.ResetProgress();
    }

    static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-10f) return a;
        return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
    }

    static void GetSphere(SphereCollider sc, out Vector3 center, out float radius)
    {
        center = sc.transform.TransformPoint(sc.center);
        Vector3 s = sc.transform.lossyScale;
        radius = sc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    void OnDisable()
    {
        Cancel();
        target = null;
        cuttable = null;
        active = done = pendingRelease = false;
    }
}