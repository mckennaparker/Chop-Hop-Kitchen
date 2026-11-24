using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;


public class StirDetector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform potCenter;
    [SerializeField] Transform stirPoint;

    [Header("Pot Limits")]
    [SerializeField] float potRadius = 0.15f;
    [SerializeField] float minimumDepth = 0.02f;
    [SerializeField] float maximumDepth = 0.25f;

    [Header("Stir Requirements")]
    [SerializeField, Min(180f)] float degreesPerStir = 360f;
    [SerializeField, Min(0.1f)] float minimumAngularSpeed = 20f;
    [SerializeField, Min(0.1f)] float maximumIdleTime = 1.5f;
    [SerializeField, Range(0f, 1f)] float progressCompleteThreshold = 0.98f;
    [SerializeField] bool showDebugLogs;

    [Header("Events")]
    [SerializeField] UnityEvent<float> onProgressChanged;
    [SerializeField] UnityEvent onStirLoopCompleted;

    XRGrabInteractable grab;
    float previousAngle;
    float accumulatedAngle;
    float direction;
    float lastMotionTime;
    float lastProgress;
    bool initialized;
    bool completed;
    float debugTimer;
    bool previousInsideState;

    public bool IsStirring { get; private set; }
    public float Progress { get; private set; }

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
        completed = false;
        if (showDebugLogs)
            Debug.Log("StirDetector: Ladle released.", this);
        ResetStir();
    }

    void Update()
    {
        if (potCenter == null || stirPoint == null)
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
            ResetStir();
            return;
        }

        Vector3 radial = Vector3.ProjectOnPlane(stirPoint.position - potCenter.position, potCenter.up);
        if (radial.sqrMagnitude < 0.0001f)
        {
            ResetStir();
            return;
        }

        float angle = Mathf.Atan2(
            Vector3.Dot(radial, potCenter.right),
            Vector3.Dot(radial, potCenter.forward)) * Mathf.Rad2Deg;

        if (!initialized)
        {
            previousAngle = angle;
            initialized = true;
            return;
        }

        float delta = Mathf.DeltaAngle(previousAngle, angle);
        previousAngle = angle;

        float deltaDegrees = Mathf.Abs(delta);
        if (deltaDegrees < 0.01f || deltaDegrees > 45f) return;

        float degreesPerSecond = deltaDegrees / Mathf.Max(Time.deltaTime, 0.0001f);
        if (showDebugLogs && Time.time - debugTimer >= 0.25f)
        {
            debugTimer = Time.time;
            Debug.Log($"StirDetector: Delta {delta:F1} deg, speed {degreesPerSecond:F1} deg/s, progress {Progress:P0}.", this);
        }
        if (degreesPerSecond < minimumAngularSpeed) return;

        if (Time.time - lastMotionTime > maximumIdleTime)
        {
            accumulatedAngle = 0f;
            direction = 0f;
        }

        lastMotionTime = Time.time;
        IsStirring = true;

        if (direction == 0f)
        {
            direction = Mathf.Sign(delta);
        }
        else if (direction * delta < 0f)
        {
            accumulatedAngle = 0f;
            direction = Mathf.Sign(delta);
        }

        accumulatedAngle += Mathf.Abs(delta);
        Progress = Mathf.Clamp01(accumulatedAngle / degreesPerStir);
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
            accumulatedAngle = 0f;
            Progress = 0f;
            lastProgress = 0f;
        }
    }

    bool IsProbeInsidePot()
    {
        Vector3 offset = stirPoint.position - potCenter.position;
        Vector3 radial = Vector3.ProjectOnPlane(offset, potCenter.up);

        float depth = Vector3.Dot(offset, potCenter.up);
        return radial.magnitude <= potRadius
            && depth >= minimumDepth
            && depth <= maximumDepth;
    }

    void ResetStir()
    {
        bool wasActive = initialized || IsStirring || Progress > 0f;
        initialized = false;
        accumulatedAngle = 0f;
        direction = 0f;
        lastMotionTime = 0f;
        IsStirring = false;
        Progress = 0f;

        if (wasActive && !completed) PublishProgress();
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

}
