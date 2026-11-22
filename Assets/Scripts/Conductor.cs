using System;
using UnityEngine;

// Owns the song clock and audio playback, judges player actions against the chart,
// and moves tickets through all ticket states
[DefaultExecutionOrder(-100)] // update SongTime before any other script reads it this frame
[RequireComponent(typeof(AudioSource))]
public class Conductor : MonoBehaviour
{
    [Header("Song")]
    [SerializeField] TextAsset chartFile;
    [SerializeField] AudioClip music;
    [SerializeField] double startDelay = 0.2; // seconds; schedules audio slightly ahead so it starts exactly on dspStart

    [Header("Judging")]
    [SerializeField] double tolerance = 0.1; // seconds allowed on either side of a note's time

    [Header("Tickets")]
    [SerializeField] double queueLeadBeats = 8;  // how early a ticket appears on the rail
    [SerializeField] double activeLeadBeats = 2; // how early it expands into a lane

    public Song Song { get; private set; }
    public double SongTime { get; private set; } // seconds since song start; negative during the lead-in
    public double SongBeat => SongTime / Song.SecondsPerBeat;
    public bool IsPlaying { get; private set; }

    public event Action<Note, double> NoteHit; // note, delta in seconds (+ = late)
    public event Action<Note> NoteMissed;
    public event Action<Ticket> TicketQueued, TicketActivated, TicketFinished;

    AudioSource source;
    double dspStart;
    int firstPending;          // index of earliest note that hasn't been judged yet
    int nextTicketToQueue;     // index of next ticket to put on the rail
    int nextTicketToActivate;  // index of next ticket to expand

    // exact time right now, independent of when this frame's Update ran
    double Now => AudioSettings.dspTime - dspStart;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;

        Song = ChartLoader.Load(chartFile);
        if (Song == null) enabled = false; // error already logged; Start won't run
    }

    void Start() => PlaySong();

    void Update()
    {
        if (!IsPlaying) return;

        SongTime = Now;
        UpdateTickets();
        ExpireMissedNotes();
    }

    // begins playback of the song from the top
    public void PlaySong()
    {
        Song.ResetProgress();
        firstPending = nextTicketToQueue = nextTicketToActivate = 0;

        source.Stop();
        source.clip = music;
        dspStart = AudioSettings.dspTime + startDelay;
        source.PlayScheduled(dspStart);

        SongTime = -startDelay;
        IsPlaying = true;
    }

    // called whenever the player performs an action
    // returns true if it hit an unjudged note of that type within the tolerance window
    public bool RegisterAction(ActionType type)
    {
        if (!IsPlaying) return false;
        double now = Now;

        for (int i = firstPending; i < Song.notes.Count; i++)
        {
            Note note = Song.notes[i];
            double delta = now - note.time;

            if (delta < -tolerance) break; // this and all later notes are too far ahead
            if (note.judged || note.type != type || delta > tolerance) continue;

            Judge(note, true, delta);
            return true;
        }

        return false;
    }

    // marks notes whose window has passed without a hit as missed
    void ExpireMissedNotes()
    {
        while (firstPending < Song.notes.Count)
        {
            Note note = Song.notes[firstPending];
            if (!note.judged)
            {
                if (SongTime - note.time <= tolerance) return; // still hittable
                Judge(note, false, 0.0);
            }
            firstPending++;
        }
    }

    // records a hit/miss on the note and its ticket, and fires the matching events
    void Judge(Note note, bool hit, double delta)
    {
        note.judged = true;
        Ticket t = note.ticket;
        t.judgedCount++;

        if (hit)
        {
            t.hits++;
            NoteHit?.Invoke(note, delta);
        }
        else
        {
            t.misses++;
            NoteMissed?.Invoke(note);
        }

        if (t.IsFinished)
        {
            t.state = TicketState.Done;
            TicketFinished?.Invoke(t);
        }
    }

    // advances tickets to Queued / Active once the song reaches their lead times
    void UpdateTickets()
    {
        double spb = Song.SecondsPerBeat;
        var tickets = Song.tickets;

        while (nextTicketToQueue < tickets.Count &&
               SongTime >= tickets[nextTicketToQueue].startTime - queueLeadBeats * spb)
        {
            Ticket t = tickets[nextTicketToQueue++];
            t.state = TicketState.Queued;
            TicketQueued?.Invoke(t);
        }

        // only queued tickets can activate, so a ticket is always Queued before it's Active
        while (nextTicketToActivate < nextTicketToQueue &&
               SongTime >= tickets[nextTicketToActivate].startTime - activeLeadBeats * spb)
        {
            Ticket t = tickets[nextTicketToActivate++];
            t.state = TicketState.Active;
            TicketActivated?.Invoke(t);
        }
    }

    void OnValidate()
    {
        tolerance = Math.Max(0.0, tolerance);
        startDelay = Math.Max(0.05, startDelay);
        activeLeadBeats = Math.Max(0.0, activeLeadBeats);
        queueLeadBeats = Math.Max(activeLeadBeats, queueLeadBeats); // must queue before activating
    }
}