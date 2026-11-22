using System.Collections.Generic;

// Runtime song data, built from a Chart by ChartLoader.
// Deliberately NOT [Serializable]: Unity must never try to serialize/restore these
// (that would silently drop Note.ticket and the parsed times on a domain reload).

public enum ActionType { Chop, Stir, Plate }

public enum TicketState { Hidden, Queued, Active, Done }

public enum Judgement { None, Perfect, Good, Miss }

public class Song
{
    public double bpm;
    public List<Note> notes = new();     // every note in the song, sorted by time (used for judging)
    public List<Ticket> tickets = new(); // one per order, sorted by start time (used for visuals)

    public double SecondsPerBeat => 60.0 / bpm;

    // clears all judging progress so the song can be replayed
    public void ResetProgress()
    {
        foreach (Note note in notes)
        {
            note.judged = false;
            note.result = Judgement.None;
            note.delta = 0;
        }
        foreach (Ticket t in tickets)
        {
            t.state = TicketState.Hidden;
            t.hits = t.misses = t.judgedCount = 0;
        }
    }
}

public class Note
{
    public ActionType type;
    public double beat;   // absolute beat in the song
    public double time;   // seconds from song start
    public bool judged;   // already hit or missed
    public Judgement result; // set when judged
    public double delta;  // seconds off the beat when hit (+ = late)
    public Ticket ticket; // order this note belongs to
}

// runtime instance of an Order: the notes it spawned plus progress for the UI
public class Ticket
{
    public int id;                    // 1-based, in play order
    public Recipe recipe;
    public List<Note> notes = new();  // sorted by time
    public double startTime, endTime; // seconds; first and last note
    public TicketState state;
    public int hits, misses, judgedCount;
    public bool IsFinished => judgedCount == notes.Count;
}
