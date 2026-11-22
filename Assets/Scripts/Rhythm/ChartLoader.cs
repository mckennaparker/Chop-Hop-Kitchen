using System;
using System.Collections.Generic;
using UnityEngine;

// Converts a JSON chart (recipes + orders) into the runtime Song used by the Conductor.
public static class ChartLoader
{
    // returns null if the chart can't be used at all
    public static Song Load(TextAsset file)
    {
        if (file == null)
        {
            Debug.LogError("ChartLoader: no chart file assigned");
            return null;
        }

        Chart chart = JsonUtility.FromJson<Chart>(file.text);
        if (chart == null || chart.bpm <= 0)
        {
            Debug.LogError($"{file.name}: chart is missing or has a non-positive bpm");
            return null;
        }

        // recipe lookup by name (JsonUtility can't read dictionaries, so build one here)
        var recipes = new Dictionary<string, Recipe>();
        foreach (Recipe r in chart.recipes)
        {
            if (string.IsNullOrEmpty(r.name))
                Debug.LogError($"{file.name}: recipe with no name, skipping");
            else if (!recipes.TryAdd(r.name, r))
                Debug.LogError($"{file.name}: duplicate recipe '{r.name}', keeping the first");
        }

        double spb = 60.0 / chart.bpm;
        var song = new Song { bpm = chart.bpm };

        // one ticket per order; its notes are the recipe's steps offset by the order's beat
        foreach (Order order in chart.orders)
        {
            if (order.recipe == null || !recipes.TryGetValue(order.recipe, out Recipe recipe))
            {
                Debug.LogError($"{file.name}: order at beat {order.beat} uses unknown recipe '{order.recipe}', skipping");
                continue;
            }

            var ticket = new Ticket { recipe = recipe };
            foreach (Step step in recipe.steps)
            {
                // IsDefined rejects numeric strings like "7" that TryParse would accept
                if (!Enum.TryParse(step.action, true, out ActionType type) || !Enum.IsDefined(typeof(ActionType), type))
                {
                    Debug.LogError($"{file.name}: unknown action '{step.action}' in recipe '{recipe.name}', skipping step");
                    continue;
                }

                double beat = order.beat + step.beat;
                ticket.notes.Add(new Note { type = type, beat = beat, time = beat * spb, ticket = ticket });
            }

            if (ticket.notes.Count == 0)
            {
                Debug.LogWarning($"{file.name}: order at beat {order.beat} ('{recipe.name}') has no valid steps, skipping");
                continue;
            }

            ticket.notes.Sort(ByTime);
            ticket.startTime = ticket.notes[0].time;
            ticket.endTime = ticket.notes[^1].time;

            song.tickets.Add(ticket);
            song.notes.AddRange(ticket.notes);
        }

        // sort by when the first note actually plays (a recipe may not start at its step beat 0)
        song.tickets.Sort((a, b) => a.startTime.CompareTo(b.startTime));
        for (int i = 0; i < song.tickets.Count; i++)
        {
            Ticket t = song.tickets[i];
            t.id = i + 1;

            // the one-active-ticket UI assumes orders don't overlap; catch charting mistakes
            if (i > 0 && t.startTime <= song.tickets[i - 1].endTime)
                Debug.LogWarning($"{file.name}: order {t.id} ('{t.recipe.name}') overlaps the previous ticket");
        }

        song.notes.Sort(ByTime);
        return song;
    }

    static int ByTime(Note a, Note b) => a.time.CompareTo(b.time);
}
