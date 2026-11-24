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
    bool hasPrev;

    void Awake() => grab = GetComponentInParent<XRGrabInteractable>();

    void OnEnable() => hasPrev = false;   // avoid a fake sweep from stale positions

    void OnDisable()
    {
        foreach (var c in Cuttable.All) CancelCut(c);
    }

    void LateUpdate()
    {
        Vector3 a = edgeBase.position, b = edgeTip.position;

        if (!hasPrev)
        {
            prevA = a; prevB = b; hasPrev = true;
            return;
        }

        bool held = grab != null && grab.isSelected;
        var all = Cuttable.All;

        for (int k = all.Count - 1; k >= 0; k--)
        {
            if (k >= all.Count) continue;          // list can change if a callback disables something
            var c = all[k];
            if (c == null || c.IsCut || c.Sphere == null) continue;

            if (!held) { CancelCut(c); continue; }

            for (int i = 1; i <= substeps; i++)
            {
                float t0 = (i - 1) / (float)substeps, t1 = i / (float)substeps;
                Vector3 a0 = Vector3.Lerp(prevA, a, t0), b0 = Vector3.Lerp(prevB, b, t0);
                Vector3 a1 = Vector3.Lerp(prevA, a, t1), b1 = Vector3.Lerp(prevB, b, t1);
                Vector3 motion = (a1 + b1) * 0.5f - (a0 + b0) * 0.5f;

                if (Step(c, a1, b1, motion)) break;   // true = cut completed
            }

            // Apply the color once per frame instead of once per substep
            if (c.bladeActive && !c.IsCut) c.SetProgress(c.bladeProgress);
        }

        prevA = a;
        prevB = b;
    }

    // Returns true if this step completed the cut
    bool Step(Cuttable c, Vector3 a, Vector3 b, Vector3 motion)
    {
        GetSphere(c.Sphere, out Vector3 center, out float r);

        Vector3 p = ClosestOnSegment(a, b, center);   // point on the edge nearest the center
        Vector3 w = p - center;
        bool touching = w.sqrMagnitude <= r * r;
        bool justEntered = touching && !c.bladeTouching;
        c.bladeTouching = touching;

        Vector3 u = (b - a).normalized;

        // --- Begin: the edge touches the sphere while moving ---
        if (!c.bladeActive)
        {
            if (!touching) return false;

            Vector3 m = motion - u * Vector3.Dot(motion, u);   // sweep direction, minus sliding along the edge
            if (m.sqrMagnitude < 1e-10f) return false;
            Vector3 dir = m.normalized;

            // Fresh contact always starts a cut, even if a fast swing is already past the center.
            // Contact that began some other way must be heading inward.
            if (!justEntered && Vector3.Dot(w, dir) > 0f) return false;

            c.bladeSweep = dir;
            c.bladeActive = true;
            // fall through and evaluate this same step
        }

        // --- Cutting ---
        Vector3 n = c.bladeSweep;
        float s = Vector3.Dot(w, n);                       // position along the sweep, 0 = center
        float q2 = w.sqrMagnitude - s * s;
        float e = Mathf.Sqrt(Mathf.Max(0f, r * r - q2));   // far surface at +e, near surface at -e

        if (s > 0f && e - s <= completionMargin)          // rhythm-game forgiveness
        {
            Complete(c);
            return true;
        }

        if (!touching)
        {
            if (s > 0f) { Complete(c); return true; }      // exited the far side
            CancelCut(c);                                  // poked and retreated
            return false;
        }

        c.bladeProgress = e > 1e-4f ? Mathf.Clamp01((s + e) / (2f * e)) : 1f;
        return false;
    }

    void Complete(Cuttable c)
    {
        // If you bring splitting back, the cut plane normal is Vector3.Cross(edgeDir, c.bladeSweep)
        c.ClearBladeState();
        c.Cut();
        if (conductor != null) conductor.RegisterAction(ActionType.Chop);
    }

    static void CancelCut(Cuttable c)
    {
        if (c == null) return;
        if (c.bladeActive)
        {
            c.bladeActive = false;
            c.ResetProgress();
        }
        c.bladeTouching = false;
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
}