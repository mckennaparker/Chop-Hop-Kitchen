using System;
using System.Collections.Generic;
using UnityEngine;

// Owns the song clock and audio playback, judges player actions against the chart,
// and moves tickets through Queued -> Active -> Done.
[DefaultExecutionOrder(-100)] // update SongTime before any other script reads it this frame
[RequireComponent(typeof(AudioSource))]
public class Conductor : MonoBehaviour
{
    [Header("Song")]
    [SerializeField] TextAsset chartFile;
    [SerializeField] AudioClip music;
    [SerializeField] double leadIn = 2.0; // seconds of silence before the music starts; the clock (and tickets) run during it

    [Header("Debug")]
    [Tooltip("Beat to start playback from (after the lead-in). Tickets that start before it are skipped. Leave at 0 for normal play.")]
    [SerializeField] double startBeat = 0;

    // per-song sound played whenever the player performs an action (e.g. a kick for Chop)
    [Serializable]
    public class ActionSound
    {
        public ActionType type;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Header("Sound Effects")]
    [SerializeField] List<ActionSound> actionSounds = new();

    [Header("Judging")]
    [SerializeField] double tolerance = 0.1;      // seconds allowed on either side of a note's time
    [SerializeField] double perfectWindow = 0.035; // hits this close to the beat count as Perfect

    [Header("Tickets")]
    [SerializeField] double queueLeadBeats = 8;  // how early a ticket appears on the rail
    [SerializeField] double activeLeadBeats = 2; // how early it expands into a lane
    [Tooltip("Beats after a ticket's last note before it counts as finished (and TicketFinished fires), so the player has time to complete the final action before the next items spawn.")]
    [SerializeField] double finishLeewayBeats = 1;

    public Song Song { get; private set; }
    public double SongTime { get; private set; }   // seconds since song start; negative during the lead-in
    public double VisualTime { get; private set; } // smoothed SongTime; use for anything that moves on screen
    public double SongBeat => SongTime / Song.SecondsPerBeat;
    public double VisualBeat => VisualTime / Song.SecondsPerBeat;
    public bool IsPlaying { get; private set; }

    public event Action SongStarted;           // fired by PlaySong, after progress is reset
    public event Action<Note, double> NoteHit; // note, delta in seconds (+ = late)
    public event Action<Note> NoteMissed;
    public event Action<Ticket> TicketQueued, TicketActivated, TicketFinished;

    AudioSource source;
    AudioSource sfxSource; // separate from the music source so Stop()/clip swaps never cut effects
    double dspStart;
    int firstPending;          // index of earliest note that hasn't been judged yet
    int nextTicketToQueue;     // index of next ticket to put on the rail
    int nextTicketToActivate;  // index of next ticket to expand
    int nextTicketToFinish;    // index of next ticket waiting to be marked Done

    // audio is always scheduled at least this far ahead so PlayScheduled can hit dspStart exactly,
    // even when leadIn is 0
    const double ScheduleBuffer = 0.1;

    // exact time right now, independent of when this frame's Update ran
    double Now => AudioSettings.dspTime - dspStart;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.outputAudioMixerGroup = source.outputAudioMixerGroup;

        Song = ChartLoader.Load(chartFile);
        if (Song == null) enabled = false; // error already logged; Start won't run
    }

    void Start() => PlaySong();

    void Update()
    {
        if (!IsPlaying) return;

        SongTime = Now;
        UpdateVisualTime();
        UpdateTickets();
        ExpireMissedNotes();
        FinishTickets();
    }

    // dspTime only advances once per audio buffer (~10-20 ms steps), which makes scrolling stutter.
    // VisualTime advances every frame and is pulled back toward the audio clock.
    void UpdateVisualTime()
    {
        double dt = Time.unscaledDeltaTime;
        VisualTime += dt;

        double error = SongTime - VisualTime;
        if (Math.Abs(error) > 0.05) VisualTime = SongTime;   // big jump (hitch, restart): snap
        else VisualTime += error * Math.Min(1.0, dt * 10.0); // small drift: ease back
    }

    // earliest unjudged note matching `match` that's due within `maxAheadSeconds`, or null
    public Note PeekNext(Predicate<Note> match, double maxAheadSeconds)
    {
        if (!IsPlaying) return null;

        for (int i = firstPending; i < Song.notes.Count; i++)
        {
            Note note = Song.notes[i];
            if (note.time - SongTime > maxAheadSeconds) break;
            if (!note.judged && match(note)) return note;
        }
        return null;
    }

    // begins (or restarts) playback of the song from startBeat (normally the top)
    public void PlaySong()
    {
        Song.ResetProgress();
        firstPending = nextTicketToQueue = nextTicketToActivate = nextTicketToFinish = 0;

        double startTime = Math.Max(0.0, startBeat) * Song.SecondsPerBeat;
        SkipTicketsBefore(startTime);

        source.Stop();
        source.clip = music;
        if (music != null)
            source.timeSamples = Math.Clamp((int)(startTime * music.frequency), 0, music.samples - 1);

        // the music starts (at startTime into the clip) after the lead-in; dspStart is when song time 0
        // would have been, so SongTime reads startTime at the moment the audio begins
        double musicStartDsp = AudioSettings.dspTime + ScheduleBuffer + leadIn;
        dspStart = musicStartDsp - startTime;
        source.PlayScheduled(musicStartDsp);

        SongTime = VisualTime = Now; // negative for the whole lead-in
        IsPlaying = true;
        SongStarted?.Invoke();

        // tickets early enough to already be Queued/Active at the start of the lead-in appear immediately
        UpdateTickets();
    }

    // debug start: tickets that begin before startTime are silently marked done (no hit/miss events)
    // so the song picks up cleanly at the first whole ticket at or after startTime
    void SkipTicketsBefore(double startTime)
    {
        if (startTime <= 0) return;

        var tickets = Song.tickets;
        while (nextTicketToQueue < tickets.Count && tickets[nextTicketToQueue].startTime < startTime)
        {
            Ticket t = tickets[nextTicketToQueue++];
            t.state = TicketState.Done;
            foreach (Note note in t.notes) note.judged = true;
        }
        nextTicketToActivate = nextTicketToFinish = nextTicketToQueue;
        // firstPending stays 0; ExpireMissedNotes/RegisterAction already step over judged notes
    }

    // called whenever the player performs an action
    // returns true if it hit an unjudged note of that type within the tolerance window
    public bool RegisterAction(ActionType type)
    {
        if (!IsPlaying) return false;
        PlayActionSound(type); // feedback for every action, hit or not
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

    void PlayActionSound(ActionType type)
    {
        foreach (ActionSound s in actionSounds)
        {
            if (s.type != type || s.clip == null) continue;
            sfxSource.PlayOneShot(s.clip, s.volume);
            return;
        }
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
        note.delta = delta;
        note.result = !hit ? Judgement.Miss
                    : Math.Abs(delta) <= perfectWindow ? Judgement.Perfect
                    : Judgement.Good;

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
    }

    // a ticket is Done once all its notes are judged AND finishLeewayBeats have passed since its
    // last note, so finishing it (and whatever spawns off TicketFinished) doesn't land on top of
    // the player's final action
    void FinishTickets()
    {
        double leeway = finishLeewayBeats * Song.SecondsPerBeat;
        var tickets = Song.tickets;

        while (nextTicketToFinish < tickets.Count)
        {
            Ticket t = tickets[nextTicketToFinish];
            if (!t.IsFinished || SongTime < t.endTime + leeway) return;

            nextTicketToFinish++;
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
        perfectWindow = Math.Clamp(perfectWindow, 0.0, tolerance);
        leadIn = Math.Max(0.0, leadIn);
        startBeat = Math.Max(0.0, startBeat);
        activeLeadBeats = Math.Max(0.0, activeLeadBeats);
        finishLeewayBeats = Math.Max(0.0, finishLeewayBeats);
        queueLeadBeats = Math.Max(activeLeadBeats, queueLeadBeats); // must queue before activating
    }
}
