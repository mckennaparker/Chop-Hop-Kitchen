using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One step (note) inside an active ticket's lane. TicketView moves it; this only handles its looks.
[RequireComponent(typeof(RectTransform))]
public class StepView : MonoBehaviour
{
    [SerializeField] Image badge;          // circle behind the icon, tinted with the action color
    [SerializeField] Image icon;
    [SerializeField] TMP_Text label;       // "Chop"
    [SerializeField] TMP_Text stationHint; // "Stove", only shown when this step needs a different station
    [SerializeField] GameObject hitMark;   // check stamp, off until hit
    [SerializeField] GameObject missMark;  // red X, off until missed
    [SerializeField] TMP_Text judgement;   // Perfect / Good / Miss
    [SerializeField] bool showTimingMs;    // adds "+12 ms" to hits; handy while calibrating

    public Note Note { get; private set; }
    public RectTransform Rect { get; private set; }

    public void Bind(Note note, ActionStyles.Entry style, bool showStation)
    {
        Note = note;
        Rect = (RectTransform)transform;

        // y is measured up from the lane's bottom edge, and the step is centred on its beat
        Rect.anchorMin = Rect.anchorMax = new Vector2(0.5f, 0f);
        Rect.pivot = new Vector2(0.5f, 0.5f);

        if (badge) badge.color = style.color;
        if (icon) { icon.sprite = style.icon; icon.enabled = style.icon != null; }
        if (label) label.text = style.label;
        if (stationHint)
        {
            stationHint.text = style.station;
            stationHint.gameObject.SetActive(showStation && !string.IsNullOrEmpty(style.station));
        }

        if (hitMark) hitMark.SetActive(false);
        if (missMark) missMark.SetActive(false);
        if (judgement) judgement.text = "";
    }

    public void ShowResult(ActionStyles styles)
    {
        bool hit = Note.result is Judgement.Perfect or Judgement.Good;
        if (hitMark) hitMark.SetActive(hit);
        if (missMark) missMark.SetActive(!hit);

        if (judgement)
        {
            judgement.color = styles.JudgementColor(Note.result);
            judgement.text = hit && showTimingMs
                ? $"{Note.result} {Note.delta * 1000:+0;-0;0} ms"
                : Note.result.ToString();
        }
    }
}
