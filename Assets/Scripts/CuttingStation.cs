using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CuttingStation : MonoBehaviour
{
    [SerializeField] Conductor conductor; // drag it in, or use Conductor.Instance if you added that
    [SerializeField] GameObject cuttableObject;
    [SerializeField] GameObject cuttingBoard;

    [Header("Timing")]
    [SerializeField] float spawnDelay = 1.0f; // pause between ticket activation and new cuttables appearing
    [SerializeField] float clearDelay = 0.5f;  // how long a finished ticket's cuttables stay on the board

    float boardLength = 0.0f;
    Vector3 rightMostStart = Vector3.zero;

    // Cuttables that currently exist, grouped by the ticket that owns them
    private readonly Dictionary<Ticket, List<GameObject>> cuttablesByTicket = new();
    // Spawns that are waiting on the delay
    private readonly Dictionary<Ticket, Coroutine> pendingSpawns = new();
    // Clears that are waiting on the delay
    private readonly Dictionary<Ticket, Coroutine> pendingClears = new();

    void OnEnable()
    {
        if (conductor == null) return;
        conductor.TicketActivated += OnTicketActivated;
        conductor.TicketFinished += OnTicketFinished;
    }

    void OnDisable()
    {
        if (conductor != null)
        {
            conductor.TicketActivated -= OnTicketActivated;
            conductor.TicketFinished -= OnTicketFinished;
        }

        // Coroutines stop automatically on disable, so just drop the stale handles
        pendingSpawns.Clear();
        pendingClears.Clear();
    }

    void Awake()
    {
        if (cuttingBoard == null) return;

        boardLength = cuttingBoard.transform.localScale.x;

        rightMostStart = cuttingBoard.transform.position;
        rightMostStart.x = cuttingBoard.transform.position.x + cuttingBoard.transform.localScale.x * 0.5f;
        rightMostStart.y = cuttingBoard.transform.position.y
                         + cuttingBoard.transform.localScale.y * 0.5f
                         + cuttableObject.transform.localScale.y * 0.5f;
    }

    void OnTicketActivated(Ticket ticket)
    {
        if (ticket == null) return;

        Debug.Log($"Now cooking #{ticket.id} {ticket.recipe.name}, first step at {ticket.startTime:F2}s");

        CancelPendingSpawn(ticket);
        pendingSpawns[ticket] = StartCoroutine(SpawnAfterDelay(ticket));
    }

    void OnTicketFinished(Ticket ticket)
    {
        if (ticket == null) return;

        // If it finishes before its cuttables ever spawned, cancel the spawn too
        CancelPendingSpawn(ticket);

        // Don't wipe the board on the last beat; let it linger briefly
        if (cuttablesByTicket.ContainsKey(ticket) && !pendingClears.ContainsKey(ticket))
            pendingClears[ticket] = StartCoroutine(ClearAfterDelay(ticket));
    }

    IEnumerator SpawnAfterDelay(Ticket ticket)
    {
        yield return new WaitForSeconds(spawnDelay);

        pendingSpawns.Remove(ticket);
        SpawnCuttables(ticket);
    }

    IEnumerator ClearAfterDelay(Ticket ticket)
    {
        yield return new WaitForSeconds(clearDelay);

        pendingClears.Remove(ticket);
        ClearCuttablesFor(ticket);
    }

    void SpawnCuttables(Ticket ticket)
    {
        // Wipe anything still lingering from earlier tickets (and this one, if re-spawned)
        foreach (var t in new List<Ticket>(cuttablesByTicket.Keys))
            ClearCuttablesFor(t);

        int numChoppables = 0;
        for (int i = 0; i < ticket.notes.Count; i++)
        {
            if (ticket.notes[i] != null && ticket.notes[i].type == ActionType.Chop)
                numChoppables++;
        }

        var list = new List<GameObject>(numChoppables);
        float delta = boardLength / (numChoppables + 1.0f);
        Color ticketColor = GetTicketColor(ticket);

        for (int i = 1; i <= numChoppables; i++)
        {
            Vector3 pos = rightMostStart;
            pos.x -= delta * i;

            GameObject cuttable = Instantiate(cuttableObject, pos, Quaternion.identity);

            var c = cuttable.GetComponent<Cuttable>();
            if (c != null) c.SetStartColor(ticketColor);

            list.Add(cuttable);
        }

        cuttablesByTicket[ticket] = list;
    }

    static Color GetTicketColor(Ticket ticket)
    {
        // Golden ratio spacing spreads hues evenly, even for sequential ids
        float hue = Mathf.Repeat(ticket.id * 0.61803398875f, 1f);
        return Color.HSVToRGB(hue, 0.75f, 1f);
    }

    void CancelPendingSpawn(Ticket ticket)
    {
        if (pendingSpawns.TryGetValue(ticket, out var routine))
        {
            if (routine != null) StopCoroutine(routine);
            pendingSpawns.Remove(ticket);
        }
    }

    void ClearCuttablesFor(Ticket ticket)
    {
        if (ticket == null) return;

        // A manual clear also cancels its pending timer
        if (pendingClears.TryGetValue(ticket, out var routine))
        {
            if (routine != null) StopCoroutine(routine);
            pendingClears.Remove(ticket);
        }

        if (!cuttablesByTicket.TryGetValue(ticket, out var list)) return;

        foreach (var c in list)
        {
            if (c != null) Destroy(c);
        }
        cuttablesByTicket.Remove(ticket);
    }
}