using System;
using System.Collections.Generic;

// Serialized chart format, read straight from the JSON TextAsset by JsonUtility.
// Field names must match the JSON keys exactly.

[Serializable]
public class Chart
{
    public double bpm;
    public List<Recipe> recipes = new();
    public List<Order> orders = new();
}

// a series of action steps that make up a dish
[Serializable]
public class Recipe
{
    public string name;
    public List<Step> steps = new();
}

// an individual action step within a recipe
[Serializable]
public class Step
{
    public string action; // parsed into ActionType at load
    public double beat;   // relative to the start of the order
}

// a recipe placed in time within a song
[Serializable]
public class Order
{
    public string recipe; // matches Recipe.name
    public double beat;   // absolute beat in the song
}
