using System;
using UnityEngine;

// Turns hits and misses into score, combo, multiplier and rank. No visuals; ScoreView displays it.
public class ScoreKeeper : MonoBehaviour
{
    [SerializeField] Conductor conductor;

    [Header("Points")]
    [SerializeField] int perfectPoints = 100;
    [SerializeField] int goodPoints = 50;

    [Header("Combo multiplier")]
    [SerializeField] int hitsPerMultiplierStep = 8; // multiplier doubles every N hits in a row: x1, x2, x4, x8
    [SerializeField] int maxMultiplier = 8;

    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int Perfects { get; private set; }
    public int Goods { get; private set; }
    public int Misses { get; private set; }
    public int MaxPossibleScore { get; private set; } // every note Perfect, never breaking combo

    public int Multiplier => MultiplierFor(Combo);
    public float Accuracy => MaxPossibleScore > 0 ? (float)Score / MaxPossibleScore : 0f;

    // S only for a flawless run (per the design doc), then by share of the max score
    public string Rank =>
        MaxPossibleScore > 0 && Score == MaxPossibleScore ? "S" :
        Accuracy >= 0.9f ? "A" :
        Accuracy >= 0.8f ? "B" :
        Accuracy >= 0.7f ? "C" :
        Accuracy >= 0.6f ? "D" : "F";

    public event Action Changed;     // any value above changed
    public event Action ComboBroken; // a miss ended a combo of at least 1

    void OnEnable()
    {
        if (conductor == null) return;
        conductor.SongStarted += ResetScore;
        conductor.NoteHit += OnHit;
        conductor.NoteMissed += OnMiss;
    }

    void OnDisable()
    {
        if (conductor == null) return;
        conductor.SongStarted -= ResetScore;
        conductor.NoteHit -= OnHit;
        conductor.NoteMissed -= OnMiss;
    }

    int MultiplierFor(int combo)
    {
        int steps = Mathf.Min(combo / Mathf.Max(1, hitsPerMultiplierStep), 30);
        return Mathf.Min(maxMultiplier, 1 << steps);
    }

    void ResetScore()
    {
        Score = Combo = MaxCombo = Perfects = Goods = Misses = 0;

        // the multiplier applies using the combo going into each hit, same as OnHit
        MaxPossibleScore = 0;
        for (int i = 0; i < conductor.Song.notes.Count; i++)
            MaxPossibleScore += perfectPoints * MultiplierFor(i);

        Changed?.Invoke();
    }

    void OnHit(Note note, double delta)
    {
        bool perfect = note.result == Judgement.Perfect;
        Score += (perfect ? perfectPoints : goodPoints) * Multiplier;
        if (perfect) Perfects++; else Goods++;

        Combo++;
        MaxCombo = Mathf.Max(MaxCombo, Combo);
        Changed?.Invoke();
    }

    void OnMiss(Note note)
    {
        Misses++;
        bool broke = Combo > 0;
        Combo = 0;
        if (broke) ComboBroken?.Invoke();
        Changed?.Invoke();
    }
}
