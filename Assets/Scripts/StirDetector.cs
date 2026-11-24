using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;


public class StirDetector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Scene object defining the stir zone. Position = center, up = pot axis, " +
             "X/Z scale = diameter, Y scale = height of the detection cylinder.")]
    [SerializeField] Transform stirZone;
    [SerializeField] Transform stirPoint;

    [Header("Stir Requirements")]
    [SerializeField, Min(180f)] float degreesPerStir = 360f;
    [SerializeField, Min(0.01f)] float minimumTipSpeed = 0.1f;
    [SerializeField, Min(0.1f)] float maximumIdleTime = 1.5f;
    [SerializeField, Range(0f, 1f)] float progressCompleteThreshold = 0.98f;
    [SerializeField] bool showDebugLogs;

    [Header("Leeway")]
    [Tooltip("How far the stirring axis may tilt away from the zone's up axis and still count.")]
    [SerializeField, Range(0f, 89f)] float maxTiltAngle = 60f;
    [Tooltip("Tip must move this far (meters) between samples. Lower for small pots.")]
    [SerializeField, Min(0.005f)] float minStepDistance = 0.03f;
    [Tooltip("How long the tip may leave the zone before progress resets.")]
    [SerializeField, Min(0f)] float exitGraceTime = 0.3f;

    [Header("Events")]
    [SerializeField] UnityEvent<float> onProgressChanged;
    [SerializeField] UnityEvent onStirLoopCompleted;
    [SerializeField] UnityEvent resetStir;

    XRGrabInteractable grab;
    Vector3 prevPoint;
    Vector3 prevStep;
    float prevTime;
    float signedAngle;
    float lastMotionTime;
    float lastInsideTime;
    float lastProgress;
    bool hasPrevPoint;
    bool hasPrevStep;
    bool completed;
    float debugTimer;
    bool previousInsideState;

    public bool IsStirring { get; private set; }
    public float Progress { get; private set; }

    // Zone values derived from the stirZone transform.
    Vector3 Center => stirZone.position;
    float Radius => 0.5f * Mathf.Min(Mathf.Abs(stirZone.lossyScale.x), Mathf.Abs(stirZone.lossyScale.z));
    float HalfHeight => 0.5f * Mathf.Abs(stirZone.localScale.y);

    void Awake()
    {
        grab = GetComponentInParent<XRGrabInteractable>();
        if (stirPoint == null) stirPoint = transform;
        previousInsideState = false;
    }

    void OnEnable()
    {
        if (showDebugLogs)
        {
            if (grab == null)
            {
                Debug.LogError("StirDetector: No XRGrabInteractable found on this object or its parents.", this);
            }
            else
            {
                Collider[] childColliders = GetComponentsInChildren<Collider>(true);
                Rigidbody rb = GetComponentInParent<Rigidbody>();
                Debug.Log(
                    $"StirDetector: Grab component found. Explicit XR colliders: {grab.colliders.Count}. " +
                    $"Child colliders: {childColliders.Length}. Rigidbody: {(rb == null ? "missing" : rb.name)}.",
                    grab);
            }
        }

        if (grab != null)
        {
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }
        ResetStir();
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (showDebugLogs)
            Debug.Log($"StirDetector: Ladle grabbed by {args.interactorObject.transform.name}.", args.interactorObject.transform);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        // completed = false; ???
        if (showDebugLogs)
            Debug.Log("StirDetector: Ladle released.", this);
        ResetStir();
    }

    void Update()
    {
        if (stirZone == null || stirPoint == null)
        {
            StopStirring();
            return;
        }

        if (grab == null || !grab.isSelected)
        {
            if (showDebugLogs && Time.frameCount % 60 == 0)
                Debug.Log("StirDetector: Ladle is not grabbed.", this);
            ResetStir();
            return;
        }

        bool insidePot = IsProbeInsidePot();
        if (insidePot != previousInsideState)
        {
            previousInsideState = insidePot;
            if (showDebugLogs)
                Debug.Log($"StirDetector: Tip is {(insidePot ? "inside" : "outside")} the stirring zone.", stirPoint);
        }

        if (!insidePot)
        {
            // Grace period: brief exits don't wipe progress.
            if (Time.time - lastInsideTime > exitGraceTime)
            {
                ResetStir();
            }
            else
            {
                // Drop sample history so re-entry doesn't create a huge fake step.
                hasPrevPoint = false;
                hasPrevStep = false;
            }
            return;
        }

        if (completed)
            return;

        lastInsideTime = Time.time;

        // Stalled for too long: start over.
        if (signedAngle != 0f && Time.time - lastMotionTime > maximumIdleTime)
        {
            signedAngle = 0f;
            Progress = 0f;
            hasPrevStep = false;
            IsStirring = false;
        }

        Vector3 pos = stirPoint.position;

        if (!hasPrevPoint)
        {
            prevPoint = pos;
            prevTime = Time.time;
            hasPrevPoint = true;
            return;
        }

        Vector3 step = pos - prevPoint;
        float stepLength = step.magnitude;
        if (stepLength < minStepDistance) return;

        float dt = Mathf.Max(Time.time - prevTime, 0.0001f);
        float tipSpeed = stepLength / dt;
        prevPoint = pos;
        prevTime = Time.time;

        if (tipSpeed < minimumTipSpeed)
        {
            hasPrevStep = false;
            return;
        }

        if (!hasPrevStep)
        {
            prevStep = step;
            hasPrevStep = true;
            lastMotionTime = Time.time;
            return;
        }

        // Turning angle between consecutive steps. Summing these around a closed loop
        // gives 360 degrees regardless of where the loop's center is.
        float turn = Vector3.Angle(prevStep, step);
        Vector3 axis = Vector3.Cross(prevStep, step);
        prevStep = step;
        lastMotionTime = Time.time;

        // Ignore near-straight steps, and near-reversals (axis is ill-defined, usually jitter).
        if (turn < 1f || turn > 120f || axis.sqrMagnitude < 1e-10f) return;

        float sign = Vector3.Dot(axis, stirZone.up) >= 0f ? 1f : -1f;
        float tilt = Vector3.Angle(axis * sign, stirZone.up);
        if (tilt > maxTiltAngle) return; // mostly not a stir around the pot axis

        IsStirring = true;
        signedAngle += sign * turn;

        if (showDebugLogs && Time.time - debugTimer >= 0.25f)
        {
            debugTimer = Time.time;
            Debug.Log($"StirDetector: Turn {sign * turn:F1} deg, tilt {tilt:F0} deg, speed {tipSpeed:F2} m/s, progress {Progress:P0}.", this);
        }

        // Signed sum: reversing direction subtracts progress instead of resetting it.
        Progress = Mathf.Clamp01(Mathf.Abs(signedAngle) / degreesPerStir);
        PublishProgress();

        if (Progress >= progressCompleteThreshold)
        {
            if (showDebugLogs)
                Debug.Log("StirDetector: Loop completed. Invoking progress and completion events.", this);
            completed = true;
            Progress = 1f;
            lastProgress = 1f;
            onProgressChanged?.Invoke(1f);
            onStirLoopCompleted?.Invoke();
            signedAngle = 0f;
            Progress = 0f;
            lastProgress = 0f;
        }
    }

    bool IsProbeInsidePot()
    {
        Vector3 offset = stirPoint.position - Center;
        Vector3 radial = Vector3.ProjectOnPlane(offset, stirZone.up);
        float depth = Vector3.Dot(offset, stirZone.up);

        return radial.magnitude <= Radius
            && Mathf.Abs(depth) <= HalfHeight;
    }

    void ResetStir()
    {
        bool wasActive = hasPrevPoint || IsStirring || Progress > 0f;
        hasPrevPoint = false;
        hasPrevStep = false;
        signedAngle = 0f;
        lastMotionTime = 0f;
        IsStirring = false;
        Progress = 0f;

        if (completed)
            completed = false;

        if (wasActive && !completed) resetStir?.Invoke();
    }

    void StopStirring()
    {
        ResetStir();
    }

    void PublishProgress()
    {
        if (Mathf.Approximately(lastProgress, Progress)) return;
        lastProgress = Progress;
        onProgressChanged?.Invoke(Progress);
    }

    // Draws the detection cylinder in the Scene view when this object is selected.
    void OnDrawGizmosSelected()
    {
        if (stirZone == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(stirZone.position, stirZone.rotation, Vector3.one);

        const int segments = 32;
        float r = Radius;
        float h = HalfHeight;
        Vector3 prevTop = new Vector3(r, h, 0f);
        Vector3 prevBottom = new Vector3(r, -h, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 top = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
            Vector3 bottom = new Vector3(top.x, -h, top.z);

            Gizmos.DrawLine(prevTop, top);
            Gizmos.DrawLine(prevBottom, bottom);
            if (i % (segments / 4) == 0) Gizmos.DrawLine(top, bottom);

            prevTop = top;
            prevBottom = bottom;
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}