using System;
using UnityEngine;

namespace InfiniteGacha
{
    // Defina aqui os campos reais do seu CharacterData.
    // Se já existir outro CharacterData no projeto, remova/renomeie a duplicata
    // para evitar erro CS0229 (ambiguity).
    [Serializable]
    public class CharacterData
    {
        public string displayName;
        public Sprite portraitSprite;

        // adicione outros campos do seu CharacterData original se necessário
        // public int rarity;
        // public string id;
        // etc...
    }
}