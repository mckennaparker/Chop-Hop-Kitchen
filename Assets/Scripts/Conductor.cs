using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public enum ActionType { Chop, Stir, Plate }

[Serializable]
public class Note
{
    // unconverted
    public string action;
    public double beat;

    // converted (runtime)
    [NonSerialized] public ActionType type; // parsed from action at load
    [NonSerialized] public double time;     // parsed from beat at load
    [NonSerialized] public bool judged;     // already hit or missed
}

[Serializable]
public class Song
{
    public double bpm;
    public List<Note> notes = new List<Note>();
}

[RequireComponent(typeof(AudioSource))]
public class Conductor : MonoBehaviour
{
    [SerializeField] TextAsset chartFile;
    [SerializeField] AudioClip music;
    [SerializeField] double tolerance = 0.1; // seconds allowed on either side of a note's time

    public Song Song;
    public double SongTime;
    public double SongBeat => SongTime * Song.bpm / 60.0;

    AudioSource source;
    double dspStart;
    int firstPending; // index of earliest note that hasn't been judged yet

    public void Awake()
    {
        source = GetComponent<AudioSource>();
        Song = LoadSong(chartFile);
    }

    void Start()
    {
        PlaySong();
    }

    void Update()
    {
        SongTime = AudioSettings.dspTime - dspStart;

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            RegisterAction(ActionType.Chop);

        ExpireMissedNotes();
    }

    // called whenever player performs an action
    // returns true if it hit a note of that type within tolerance window
    public bool RegisterAction(ActionType type)
    {
        double now = AudioSettings.dspTime - dspStart;

        for (int i = firstPending; i < Song.notes.Count; i++)
        {
            Note note = Song.notes[i];
            double delta = now - note.time;

            if (delta < -tolerance) break; // rest of the notes are too far ahead
            if (note.judged || note.type != type || delta > tolerance) continue;

            note.judged = true;
            Debug.Log($"[{now:F3}s] HIT  #{i} {type} ({delta * 1000:+0;-0;0} ms)");
            return true;
        }

        Debug.Log($"[{now:F3}s] {type} pressed, no {type} note in window");
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
                note.judged = true;
                Debug.Log($"[{SongTime:F3}s] MISS #{firstPending} {note.type}");
            }
            firstPending++;
        }
    }

    // converts JSON song chart into runtime note data used by conductor
    public static Song LoadSong(TextAsset file)
    {
        Song song = JsonUtility.FromJson<Song>(file.text);
        double secondsPerBeat = 60.0 / song.bpm;

        foreach (Note note in song.notes)
        {
            if (!Enum.TryParse(note.action, true, out note.type))
                Debug.LogError($"{file.name}: unknown action '{note.action}' at beat {note.beat}");
            note.time = note.beat * secondsPerBeat;
        }

        song.notes.Sort((a, b) => a.beat.CompareTo(b.beat));
        return song;
    }

    // begins playback of song
    public void PlaySong()
    {
        foreach (Note note in Song.notes) note.judged = false;
        firstPending = 0;

        source.clip = music;
        dspStart = AudioSettings.dspTime;
        source.PlayScheduled(dspStart);
    }

}