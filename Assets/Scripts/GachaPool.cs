using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GachaPool", menuName = "Gacha/Pool")]
public class GachaPool : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public CharacterData character;
        public float weight; // peso relativo
    }

    public List<Entry> entries = new List<Entry>();

    // runtime
    [NonSerialized] public float totalWeight;

    public void Recalculate()
    {
        totalWeight = 0f;
        foreach (var e in entries) totalWeight += Mathf.Max(0.0001f, e.weight);
    }
}