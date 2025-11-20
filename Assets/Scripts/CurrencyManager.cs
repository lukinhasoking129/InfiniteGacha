using UnityEngine;

public class CurrencyManager : MonoBehaviour
{
    public int gems = 1000; // para testar

    public bool Spend(int amount)
    {
        if (gems < amount) return false;
        gems -= amount;
        return true;
    }

    public void Add(int amount)
    {
        gems += amount;
    }
}