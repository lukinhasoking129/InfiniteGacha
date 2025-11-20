using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public List<string> obtainedIds = new List<string>();

    public void Add(CharacterData character)
    {
        if (character == null) return;
        if (!obtainedIds.Contains(character.characterId))
        {
            obtainedIds.Add(character.characterId);
            Save();
        }
    }

    public bool Has(string characterId)
    {
        return obtainedIds.Contains(characterId);
    }

    void Save()
    {
        var wrapper = new Serialization<string>(obtainedIds);
        var json = JsonUtility.ToJson(wrapper);
        PlayerPrefs.SetString("inventory", json);
        PlayerPrefs.Save();
    }

    void Awake()
    {
        Load();
    }

    void Load()
    {
        string json = PlayerPrefs.GetString("inventory", "");
        if (!string.IsNullOrEmpty(json))
        {
            var s = JsonUtility.FromJson<Serialization<string>>(json);
            if (s != null && s.target != null) obtainedIds = s.target;
        }
    }

    [System.Serializable]
    private class Serialization<T>
    {
        public List<T> target;
        public Serialization(List<T> target) { this.target = target; }
    }
}