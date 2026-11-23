using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Lives on the world-space ticket rail canvas. Spawns a TicketView for each ticket the Conductor
// queues and moves it along: spawn slot -> queue slot -> active slot -> pass slot -> destroyed.
public class TicketRailView : MonoBehaviour
{
    [SerializeField] Conductor conductor;
    [SerializeField] ActionStyles styles;
    [SerializeField] TicketView ticketPrefab;

    [Header("Slots (empty RectTransforms, pivot at top centre where a ticket hangs)")]
    [SerializeField] RectTransform spawnSlot;    // just past the end of the rail; new tickets slide in from here
    [SerializeField] RectTransform activeSlot;   // centre of the rail
    [SerializeField] RectTransform[] queueSlots; // nearest the active slot first
    [SerializeField] RectTransform passSlot;     // over the pass window; finished tickets fly here

    [Header("Timing")]
    [SerializeField] float serveSeconds = 1.5f; // how long a finished ticket stays visible on its way out

    readonly Dictionary<Ticket, TicketView> views = new(); // tickets still being played
    readonly List<TicketView> waiting = new();             // on the rail in play order, not in the active slot
    readonly List<TicketView> spawned = new();             // everything alive, for cleanup on restart
    TicketView current;                                    // the ticket in the active slot

    void OnEnable()
    {
        if (conductor == null) return;
        conductor.SongStarted += Clear;
        conductor.TicketQueued += OnQueued;
        conductor.TicketActivated += OnActivated;
        conductor.TicketFinished += OnFinished;
        conductor.NoteHit += OnHit;
        conductor.NoteMissed += OnJudged;
    }

    void OnDisable()
    {
        if (conductor == null) return;
        conductor.SongStarted -= Clear;
        conductor.TicketQueued -= OnQueued;
        conductor.TicketActivated -= OnActivated;
        conductor.TicketFinished -= OnFinished;
        conductor.NoteHit -= OnHit;
        conductor.NoteMissed -= OnJudged;
    }

    void OnQueued(Ticket ticket)
    {
        TicketView view = Instantiate(ticketPrefab, spawnSlot);
        view.transform.localPosition = Vector3.zero;
        view.transform.localRotation = Quaternion.identity;
        view.transform.localScale = Vector3.one;
        view.Bind(ticket, conductor, styles);

        views[ticket] = view;
        waiting.Add(view);
        spawned.Add(view);
        Layout();
    }

    void OnActivated(Ticket ticket)
    {
        if (!views.TryGetValue(ticket, out TicketView view)) return;
        view.SetState(TicketState.Active);
        Layout();
    }

    void OnFinished(Ticket ticket)
    {
        if (!views.TryGetValue(ticket, out TicketView view)) return;

        views.Remove(ticket);
        waiting.Remove(view);
        if (current == view) current = null;

        view.SetState(TicketState.Done);
        view.gameObject.SetActive(true); // in case it finished while waiting off-rail
        StartCoroutine(Serve(view));
        Layout(); // the next ticket moves into the active slot straight away
    }

    void OnHit(Note note, double delta) => OnJudged(note);

    void OnJudged(Note note)
    {
        if (views.TryGetValue(note.ticket, out TicketView view))
            view.OnNoteJudged(note);
    }

    // put every ticket where it belongs; views glide there by themselves
    void Layout()
    {
        // the oldest ticket that has activated takes the active slot once it's free
        if (current == null && waiting.Count > 0 && waiting[0].Ticket.state == TicketState.Active)
        {
            current = waiting[0];
            waiting.RemoveAt(0);
            current.MoveTo(activeSlot);
        }

        // the rest fill the queue slots in order; anything past the last slot waits hidden at spawn
        for (int i = 0; i < waiting.Count; i++)
        {
            bool hasSlot = i < queueSlots.Length;
            if (hasSlot) waiting[i].MoveTo(queueSlots[i]);
            waiting[i].gameObject.SetActive(hasSlot);
        }
    }

    IEnumerator Serve(TicketView view)
    {
        view.MoveTo(passSlot);
        yield return new WaitForSeconds(serveSeconds);
        spawned.Remove(view);
        if (view) Destroy(view.gameObject);
    }

    void Clear()
    {
        StopAllCoroutines();
        foreach (TicketView view in spawned)
            if (view) Destroy(view.gameObject);

        spawned.Clear();
        waiting.Clear();
        views.Clear();
        current = null;
    }
}
