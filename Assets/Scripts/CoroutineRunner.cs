using UnityEngine;
using System.Collections;

/// Lightweight runner to execute coroutines from code even when other GameObjects/MonoBehaviours may be inactive.
/// Instance is created on first access and marked DontDestroyOnLoad.
public class CoroutineRunner : MonoBehaviour
{
    static CoroutineRunner _instance;
    public static CoroutineRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("CoroutineRunner");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoroutineRunner>();
            }
            return _instance;
        }
    }

    public Coroutine Run(IEnumerator routine)
    {
        if (routine == null) return null;
        return StartCoroutine(routine);
    }
}