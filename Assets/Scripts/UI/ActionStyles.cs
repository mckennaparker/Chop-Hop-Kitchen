using System;
using System.Collections.Generic;
using UnityEngine;

// One shared asset that says how each action looks: icon, color, label, and which station it's at.
// Tickets, steps and station cues all read from this so the colors stay consistent everywhere.
// Create with: Assets > Create > Chop Hop > Action Styles
[CreateAssetMenu(fileName = "ActionStyles", menuName = "Chop Hop/Action Styles")]
public class ActionStyles : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public ActionType type;
        public string label;   // shown on the step, e.g. "Chop"
        public string station; // shown when the player has to turn, e.g. "Stove"
        public Sprite icon;
        public Color color = Color.white;
    }

    [SerializeField] List<Entry> entries = new();

    [Header("Judgement colors")]
    [SerializeField] Color perfectColor = new(1f, 0.78f, 0.2f);
    [SerializeField] Color goodColor = new(0.45f, 0.8f, 0.5f);
    [SerializeField] Color missColor = new(0.9f, 0.3f, 0.3f);

    [NonSerialized] readonly Dictionary<ActionType, Entry> fallbacks = new();

    public Entry Get(ActionType type)
    {
        foreach (Entry e in entries)
            if (e.type == type) return e;

        // missing entry: warn once and use a plain placeholder (never write it into the asset)
        if (!fallbacks.TryGetValue(type, out Entry fallback))
        {
            Debug.LogWarning($"ActionStyles: no entry for {type}", this);
            fallback = new Entry { type = type, label = type.ToString(), station = "" };
            fallbacks[type] = fallback;
        }
        return fallback;
    }

    public Color JudgementColor(Judgement j) => j switch
    {
        Judgement.Perfect => perfectColor,
        Judgement.Good => goodColor,
        Judgement.Miss => missColor,
        _ => Color.white,
    };

    // editor: fills in sensible defaults when the asset is first created
    void Reset()
    {
        entries = new List<Entry>
        {
            new() { type = ActionType.Chop,  label = "Chop",  station = "Cutting board", color = new Color(0.85f, 0.35f, 0.19f) },
            new() { type = ActionType.Stir,  label = "Stir",  station = "Stove",         color = new Color(0.11f, 0.62f, 0.46f) },
            new() { type = ActionType.Plate, label = "Plate", station = "Pass",          color = new Color(0.50f, 0.47f, 0.87f) },
        };
    }
}
