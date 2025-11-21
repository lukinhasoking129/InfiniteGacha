using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterData", menuName = "Gacha/Character")]
public class CharacterData : ScriptableObject
{
    [Tooltip("ID único (sem espaços).")]
    public string characterId;

    [Header("Identidade")]
    public string displayName;
    [TextArea] public string description;

    // Para protótipo 3D: prefab a ser instanciado
    [Tooltip("Prefab 3D usado no preview")]
    public GameObject prefab3D;

    // Ícone opcional para UI
    public Sprite sprite;

    [Header("Meta")]
    public Rarity rarity;

    // --- Campos opcionais adicionais (mantém compatibilidade com scripts que usem stats) ---
    [Header("Progressão")]
    public int level = 1;

    [Header("Status de combate")]
    public int hp = 100;
    public int atk = 10;
    public int def = 5;
    public int spd = 5; // exemplo de outro atributo
}
 
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}