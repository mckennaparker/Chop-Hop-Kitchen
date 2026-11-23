using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One order ticket hanging on the rail.
//   Queued: compact. Name, a row of step icons, and a countdown.
//   Active: grows into a lane where steps fall toward the hit line in time with the song.
//   Done:   keeps the lane and shows a grade stamp; the rail then sends it to the pass.
// It always glides toward whichever slot it's parented to, so moving it is just MoveTo(slot).
[RequireComponent(typeof(RectTransform))]
public class TicketView : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] TMP_Text title;    // "#1 Burger"
    [SerializeField] TMP_Text progress; // "2 / 5"

    [Header("Compact body (queued)")]
    [SerializeField] GameObject compactBody;
    [SerializeField] RectTransform iconRow; // give it a HorizontalLayoutGroup
    [SerializeField] Image iconPrefab;
    [SerializeField] TMP_Text countdown;    // "In 6 beats"

    [Header("Lane body (active)")]
    [SerializeField] GameObject laneBody;
    [SerializeField] RectTransform lane;              // the masked area (RectMask2D) steps scroll through
    [SerializeField] RectTransform beatLineContainer; // stretched over the lane by code
    [SerializeField] RectTransform stepContainer;     // stretched over the lane by code
    [SerializeField] RectTransform hitLine;           // placed at hitLineY by code
    [SerializeField] Image beatLinePrefab;
    [SerializeField] StepView stepPrefab;

    [Header("Done")]
    [SerializeField] GameObject stamp;
    [SerializeField] TMP_Text stampText;

    [Header("Lane layout (canvas units)")]
    [SerializeField] float hitLineY = 60f;        // hit line height above the lane's bottom edge
    [SerializeField] float unitsPerSecond = 160f; // scroll speed
    [SerializeField] int beatsPerMeasure = 4;
    [SerializeField] float beatLineThickness = 1f;
    [SerializeField] float measureLineThickness = 3f;
    [SerializeField] Color beatLineColor = new(0f, 0f, 0f, 0.12f);
    [SerializeField] Color measureLineColor = new(0f, 0f, 0f, 0.35f);

    [Header("Size and motion")]
    [SerializeField] Vector2 compactSize = new(220f, 150f);
    [SerializeField] Vector2 activeSize = new(260f, 520f);
    [SerializeField] float followSharpness = 10f; // higher = snappier moves between slots
    [SerializeField] float stampPunchScale = 1.6f;

    public Ticket Ticket { get; private set; }

    Conductor conductor;
    ActionStyles styles;
    RectTransform rect;
    readonly List<StepView> steps = new();
    readonly List<Image> beatLines = new();
    Vector2 targetSize;
    int shownCountdown = int.MinValue;

    public void Bind(Ticket ticket, Conductor conductor, ActionStyles styles)
    {
        Ticket = ticket;
        this.conductor = conductor;
        this.styles = styles;

        // hang from the slot: anchored at a point so sizeDelta is the size, pivot at the top so it grows downward
        rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);

        Stretch(beatLineContainer);
        Stretch(stepContainer);
        hitLine.anchorMin = new Vector2(0f, 0f);
        hitLine.anchorMax = new Vector2(1f, 0f);
        hitLine.pivot = new Vector2(0.5f, 0.5f);
        hitLine.anchoredPosition = new Vector2(0f, hitLineY);

        title.text = $"#{ticket.id} {ticket.recipe.name}";

        string previousStation = null;
        foreach (Note note in ticket.notes)
        {
            ActionStyles.Entry style = styles.Get(note.type);

            Image icon = Instantiate(iconPrefab, iconRow);
            icon.sprite = style.icon;
            icon.color = style.color;

            // tell the player where to turn on the first step and whenever the station changes
            StepView step = Instantiate(stepPrefab, stepContainer);
            step.Bind(note, style, showStation: style.station != previousStation);
            steps.Add(step);
            previousStation = style.station;
        }

        stamp.SetActive(false);
        SetState(TicketState.Queued);
        rect.sizeDelta = targetSize; // appear at full compact size rather than growing from zero
        RefreshProgress();
    }

    public void SetState(TicketState state)
    {
        bool expanded = state is TicketState.Active or TicketState.Done;
        compactBody.SetActive(!expanded);
        laneBody.SetActive(expanded);
        targetSize = expanded ? activeSize : compactSize;

        if (state == TicketState.Done)
        {
            stamp.SetActive(true);
            stamp.transform.localScale = Vector3.one * stampPunchScale;
            stampText.text = Grade(Ticket);
        }
    }

    // reparent and let Update glide it into place
    public void MoveTo(RectTransform slot) => transform.SetParent(slot, worldPositionStays: true);

    public void OnNoteJudged(Note note)
    {
        int i = Ticket.notes.IndexOf(note);
        if (i >= 0) steps[i].ShowResult(styles);
        RefreshProgress();
    }

    public static string Grade(Ticket t)
    {
        if (t.misses == 0)
            return t.notes.TrueForAll(n => n.result == Judgement.Perfect) ? "Perfect" : "Served";
        return t.hits * 2 >= t.notes.Count ? "Sloppy" : "Burnt";
    }

    void Update()
    {
        if (Ticket == null) return;

        FollowSlot();
        if (compactBody.activeSelf) UpdateCountdown();
        if (laneBody.activeSelf) UpdateLane();
    }

    void FollowSlot()
    {
        float k = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime); // frame-rate independent lerp
        transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, k);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.identity, k);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, k);
        rect.sizeDelta = Vector2.Lerp(rect.sizeDelta, targetSize, k);

        if (stamp.activeSelf)
            stamp.transform.localScale = Vector3.Lerp(stamp.transform.localScale, Vector3.one, k);
    }

    void UpdateCountdown()
    {
        double beatsLeft = (Ticket.startTime - conductor.SongTime) / conductor.Song.SecondsPerBeat;
        int beats = Mathf.Max(0, (int)Math.Ceiling(beatsLeft));
        if (beats == shownCountdown) return; // only touch the text when the number changes
        shownCountdown = beats;
        countdown.text = beats == 1 ? "In 1 beat" : $"In {beats} beats";
    }

    void UpdateLane()
    {
        double now = conductor.VisualTime;
        foreach (StepView step in steps)
            step.Rect.anchoredPosition = new Vector2(0f, LaneY(step.Note.time, now));

        UpdateBeatLines(now);
    }

    float LaneY(double time, double now) => hitLineY + (float)((time - now) * unitsPerSecond);

    // one line per beat across the visible part of the lane, thicker on the first beat of each measure
    void UpdateBeatLines(double now)
    {
        double spb = conductor.Song.SecondsPerBeat;
        double bottomTime = now - hitLineY / unitsPerSecond;
        double topTime = now + (lane.rect.height - hitLineY) / unitsPerSecond;
        int first = (int)Math.Floor(bottomTime / spb);
        int count = (int)Math.Ceiling(topTime / spb) - first + 1;

        while (beatLines.Count < count) beatLines.Add(CreateBeatLine());

        for (int i = 0; i < beatLines.Count; i++)
        {
            Image line = beatLines[i];
            bool used = i < count;
            if (line.gameObject.activeSelf != used) line.gameObject.SetActive(used);
            if (!used) continue;

            int beat = first + i;
            bool downbeat = ((beat % beatsPerMeasure) + beatsPerMeasure) % beatsPerMeasure == 0; // safe for negative beats
            RectTransform r = line.rectTransform;
            r.anchoredPosition = new Vector2(0f, LaneY(beat * spb, now));
            r.sizeDelta = new Vector2(0f, downbeat ? measureLineThickness : beatLineThickness);
            line.color = downbeat ? measureLineColor : beatLineColor;
        }
    }

    Image CreateBeatLine()
    {
        Image line = Instantiate(beatLinePrefab, beatLineContainer);
        RectTransform r = line.rectTransform;
        r.anchorMin = new Vector2(0f, 0f); // full lane width, positioned up from the bottom
        r.anchorMax = new Vector2(1f, 0f);
        r.pivot = new Vector2(0.5f, 0.5f);
        line.raycastTarget = false;
        return line;
    }

    void RefreshProgress() => progress.text = $"{Ticket.judgedCount} / {Ticket.notes.Count}";

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    void OnValidate() => beatsPerMeasure = Mathf.Max(1, beatsPerMeasure);
}
