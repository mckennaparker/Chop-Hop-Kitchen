using UnityEngine;
using UnityEngine.InputSystem;

// Test charts without a headset: Space / J = chop, K = stir, L = plate.
// Also logs every judgement to the console. Remove from the scene for real play.
public class DebugKeyboardInput : MonoBehaviour
{
    [SerializeField] Conductor conductor;
    [SerializeField] bool logJudgements = true;

    void OnEnable()
    {
        if (conductor == null) return;
        conductor.NoteHit += LogHit;
        conductor.NoteMissed += LogMiss;
        conductor.TicketFinished += LogTicket;
    }

    void OnDisable()
    {
        if (conductor == null) return;
        conductor.NoteHit -= LogHit;
        conductor.NoteMissed -= LogMiss;
        conductor.TicketFinished -= LogTicket;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || conductor == null) return;

        if (kb.spaceKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame) Press(ActionType.Chop);
        if (kb.kKey.wasPressedThisFrame) Press(ActionType.Stir);
        if (kb.lKey.wasPressedThisFrame) Press(ActionType.Plate);
    }

    void Press(ActionType type)
    {
        if (!conductor.RegisterAction(type) && logJudgements)
            Debug.Log($"[{conductor.SongTime:F3}s] {type} pressed, no {type} note in window");
    }

    void LogHit(Note note, double delta)
    {
        if (logJudgements)
            Debug.Log($"[{conductor.SongTime:F3}s] HIT  {note.type} beat {note.beat} ({delta * 1000:+0;-0;0} ms)");
    }

    void LogMiss(Note note)
    {
        if (logJudgements)
            Debug.Log($"[{conductor.SongTime:F3}s] MISS {note.type} beat {note.beat}");
    }

    void LogTicket(Ticket t)
    {
        if (logJudgements)
            Debug.Log($"Ticket #{t.id} {t.recipe.name} done: {t.hits}/{t.notes.Count} hit");
    }
}
