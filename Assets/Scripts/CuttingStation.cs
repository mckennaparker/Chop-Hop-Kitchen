using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CuttingStation : MonoBehaviour
{
    [SerializeField] Conductor conductor;
    [SerializeField] GameObject cuttableObject;   // must have a Cuttable
    [SerializeField] GameObject cuttingBoard;

    [Header("Timing")]
    [SerializeField] float spawnDelay = 1.0f; // pause between ticket activation and new cuttables appearing
    [SerializeField] float clearDelay = 0.5f;  // how long a finished ticket's cuttables stay on the board

    [Header("Pooling")]
    [SerializeField] int prewarmCount = 8;     // at least the most cuttables any ticket needs

    [Header("Ticket colors")]
    [Tooltip("Hue range (0-1) that ticket colors avoid, e.g. the green used as the cutting indicator")]
    [SerializeField] Vector2 reservedHueRange = new Vector2(0.22f, 0.45f);

    float boardLength = 0.0f;
    Vector3 rightMostStart = Vector3.zero;

    readonly Dictionary<Ticket, List<Cuttable>> cuttablesByTicket = new();
    readonly Dictionary<Ticket, Coroutine> pendingSpawns = new();
    readonly Dictionary<Ticket, Coroutine> pendingClears = new();

    // Pools
    readonly Stack<Cuttable> pool = new();
    readonly Stack<List<Cuttable>> listPool = new();
    readonly List<Ticket> scratchTickets = new();

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
        if (cuttableObject == null || cuttableObject.GetComponent<Cuttable>() == null)
            Debug.LogError("CuttingStation: cuttableObject needs a Cuttable component.", this);

        if (cuttingBoard != null)
        {
            boardLength = cuttingBoard.transform.localScale.x;

            rightMostStart = cuttingBoard.transform.position;
            rightMostStart.x = cuttingBoard.transform.position.x + cuttingBoard.transform.localScale.x * 0.5f;
            rightMostStart.y = cuttingBoard.transform.position.y
                             + cuttingBoard.transform.localScale.y * 0.5f
                             + cuttableObject.transform.localScale.y * 0.5f;
        }

        // Pay the Instantiate cost at load time, not mid-song
        for (int i = 0; i < prewarmCount; i++)
        {
            var c = CreateCuttable();
            c.gameObject.SetActive(false);
            pool.Push(c);
        }
    }

    // ---------- Pooling ----------

    Cuttable CreateCuttable() => Instantiate(cuttableObject).GetComponent<Cuttable>();

    Cuttable GetCuttable(Vector3 pos, Color color)
    {
        Cuttable c = pool.Count > 0 ? pool.Pop() : CreateCuttable();
        c.transform.SetPositionAndRotation(pos, Quaternion.identity);
        c.Prepare(color);                 // resets cut state and color
        c.gameObject.SetActive(true);
        return c;
    }

    void ReleaseCuttable(Cuttable c)
    {
        c.gameObject.SetActive(false);
        pool.Push(c);
    }

    // ---------- Ticket events ----------

    void OnTicketActivated(Ticket ticket)
    {
        if (ticket == null) return;

#if UNITY_EDITOR
        Debug.Log($"Now cooking #{ticket.id} {ticket.recipe.name}, first step at {ticket.startTime:F2}s");
#endif

        CancelPendingSpawn(ticket);
        pendingSpawns[ticket] = StartCoroutine(SpawnAfterDelay(ticket));
    }

    void OnTicketFinished(Ticket ticket)
    {
        if (ticket == null) return;

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

    // ---------- Spawning / clearing ----------

    void SpawnCuttables(Ticket ticket)
    {
        // Clear anything still lingering from earlier tickets (no allocation)
        scratchTickets.Clear();
        foreach (var t in cuttablesByTicket.Keys) scratchTickets.Add(t);
        for (int i = 0; i < scratchTickets.Count; i++) ClearCuttablesFor(scratchTickets[i]);

        int numChoppables = 0;
        for (int i = 0; i < ticket.notes.Count; i++)
        {
            if (ticket.notes[i] != null && ticket.notes[i].type == ActionType.Chop)
                numChoppables++;
        }

        var list = listPool.Count > 0 ? listPool.Pop() : new List<Cuttable>(numChoppables);
        float delta = boardLength / (numChoppables + 1.0f);
        Color ticketColor = GetTicketColor(ticket);

        for (int i = 1; i <= numChoppables; i++)
        {
            Vector3 pos = rightMostStart;
            pos.x -= delta * i;
            list.Add(GetCuttable(pos, ticketColor));
        }

        cuttablesByTicket[ticket] = list;
    }

    // Golden-ratio hue spacing, skipping the reserved band (e.g. green = cutting indicator)
    Color GetTicketColor(Ticket ticket)
    {
        float lo = reservedHueRange.x, hi = reservedHueRange.y;
        float band = Mathf.Max(0f, hi - lo);

        float h = Mathf.Repeat(ticket.id * 0.61803398875f, 1f) * (1f - band);
        if (h >= lo) h += band;

        return Color.HSVToRGB(h, 0.75f, 1f);
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

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null) ReleaseCuttable(list[i]);
        }
        list.Clear();
        listPool.Push(list);
        cuttablesByTicket.Remove(ticket);
    }
}