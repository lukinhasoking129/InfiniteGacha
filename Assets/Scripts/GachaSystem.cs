using System;
using System.Collections.Generic;
using UnityEngine;

public class GachaSystem : MonoBehaviour
{
    [Header("Dados")]
    public GachaPool pool;

    [Header("Pity")]
    public int pityThreshold = 90; // garantia lendária
    private int pullsSinceLastLegendary = 0;

    private System.Random rng;

    void Awake()
    {
        rng = new System.Random(Environment.TickCount);
        if (pool != null) pool.Recalculate();
        LoadState();
    }

    public List<CharacterData> Pull(int count)
    {
        var results = new List<CharacterData>();
        for (int i = 0; i < count; i++)
        {
            bool forceLegendary = (pullsSinceLastLegendary + 1) >= pityThreshold;
            var chosen = RollOnce(forceLegendary);
            results.Add(chosen);

            if (chosen != null && chosen.rarity == Rarity.Legendary)
                pullsSinceLastLegendary = 0;
            else
                pullsSinceLastLegendary++;
        }
        SaveState();
        return results;
    }

    private CharacterData RollOnce(bool forceLegendary)
    {
        if (pool == null || pool.entries.Count == 0) return null;

        if (forceLegendary)
        {
            var legals = pool.entries.FindAll(e => e.character != null && e.character.rarity == Rarity.Legendary);
            if (legals != null && legals.Count > 0)
            {
                int idx = rng.Next(legals.Count);
                return legals[idx].character;
            }
        }

        float pick = (float)rng.NextDouble() * pool.totalWeight;
        float acc = 0f;
        foreach (var e in pool.entries)
        {
            acc += Mathf.Max(0.0001f, e.weight);
            if (pick <= acc) return e.character;
        }

        return pool.entries[UnityEngine.Random.Range(0, pool.entries.Count)].character;
    }

    void SaveState()
    {
        PlayerPrefs.SetInt("pullsSinceLastLegendary", pullsSinceLastLegendary);
        PlayerPrefs.Save();
    }

    void LoadState()
    {
        pullsSinceLastLegendary = PlayerPrefs.GetInt("pullsSinceLastLegendary", 0);
    }
}