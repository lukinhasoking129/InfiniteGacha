using System;
using UnityEngine;
using UnityEngine.UI;

namespace InfiniteGacha
{
    [DisallowMultipleComponent]
    public class HeroPreviewPanel : MonoBehaviour
    {
        // Singleton disponível como HeroPreviewPanel.Instance
        public static HeroPreviewPanel Instance { get; private set; }

        [Header("Referências de UI")]
        [SerializeField] private GameObject panelRoot;      // root do painel (normalmente o próprio GameObject)
        [SerializeField] private Text heroNameText;
        [SerializeField] private Image heroPortrait;
        [SerializeField] private Button closeButton;
        [SerializeField] private CanvasGroup canvasGroup;    // opcional para fade

        [Header("Animação")]
        [SerializeField] private float fadeDuration = 0.15f;

        private void Awake()
        {
            // Singleton simples
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("HeroPreviewPanel: já existe uma instância, destruindo duplicata.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (panelRoot == null) panelRoot = this.gameObject;

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            // inicia fechado
            if (panelRoot != null) panelRoot.SetActive(false);

            if (canvasGroup == null)
                canvasGroup = panelRoot != null ? panelRoot.GetComponent<CanvasGroup>() : GetComponent<CanvasGroup>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Abre sem dados
        public void Open()
        {
            if (panelRoot == null)
            {
                Debug.LogError("HeroPreviewPanel.Open: panelRoot não atribuído.");
                return;
            }

            panelRoot.SetActive(true);
            transform.SetAsLastSibling();
            if (canvasGroup != null) StartCoroutine(FadeCanvas(0f, 1f, fadeDuration));
        }

        // Abre com HeroData
        public void Open(HeroData hero)
        {
            if (hero == null)
            {
                Debug.LogWarning("HeroPreviewPanel.Open recebeu hero nulo.");
                Open();
                return;
            }

            if (heroNameText != null) heroNameText.text = string.IsNullOrEmpty(hero.displayName) ? "Unknown" : hero.displayName;
            if (heroPortrait != null) heroPortrait.sprite = hero.portraitSprite;

            Open();
            Debug.Log($"HeroPreviewPanel: aberto para {hero.displayName}");
        }

        // Fecha
        public void Close()
        {
            if (panelRoot == null)
            {
                Debug.LogError("HeroPreviewPanel.Close: panelRoot não atribuído.");
                return;
            }

            if (canvasGroup != null)
            {
                StartCoroutine(FadeOutAndDisable(fadeDuration));
            }
            else
            {
                panelRoot.SetActive(false);
            }
        }

        // Aliases para compatibilidade com código que usa Show()
        public void Show() => Open();
        public void Show(HeroData hero) => Open(hero);

        // --- Compatibilidade direta com CharacterData (overloads) ---
        // Se seu projeto usa CharacterData, chame Show(character) diretamente.
        public void Show(CharacterData character)
        {
            Open(Convert(character));
        }

        public void Open(CharacterData character)
        {
            Open(Convert(character));
        }

        // Converte CharacterData -> HeroData (mapeie conforme seu CharacterData real)
        private HeroData Convert(CharacterData character)
        {
            if (character == null) return null;

            var h = new HeroData
            {
                displayName = string.IsNullOrEmpty(character.displayName) ? "Unknown" : character.displayName,
                portraitSprite = character.portraitSprite
            };

            return h;
        }

        // Coroutines de fade
        private System.Collections.IEnumerator FadeCanvas(float from, float to, float duration)
        {
            if (canvasGroup == null) yield break;
            float t = 0f;
            canvasGroup.alpha = from;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            canvasGroup.alpha = to;
        }

        private System.Collections.IEnumerator FadeOutAndDisable(float duration)
        {
            yield return FadeCanvas(1f, 0f, duration);
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}