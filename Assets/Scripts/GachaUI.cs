using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// GachaUI - gerencia UI do gacha, criação de slots e integração com HeroPreviewPanel.
/// - Passa a proporção do previewRaw do ResultSlot para o painel para que o big preview mantenha a MESMA proporção.
public class GachaUI : MonoBehaviour
{
    [Header("Managers & Data")]
    public GachaSystem gacha;
    public CurrencyManager currency;
    public InventoryManager inventory;

    [Header("Costs")]
    public int costPerPull = 100;

    [Header("UI References")]
    public Text gemsText;
    public Transform resultsContainer;
    public GameObject resultSlotPrefab;
    public ScrollRect resultsScrollRect;

    [Header("Hero Preview Panel (component)")]
    public HeroPreviewPanel heroPreviewPanelComponent;

    [Header("3D preview (world)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewSpacing = 1.2f;
    public float previewScale = 1f;
    public float previewLift = 0.5f;

    private GameObject currentPreviewInstance;

    void Start()
    {
        if (gacha == null) Debug.LogWarning("GachaUI: gacha not assigned.");
        if (currency == null) Debug.LogWarning("GachaUI: currency not assigned.");
        if (resultsContainer == null) Debug.LogWarning("GachaUI: resultsContainer not assigned.");
        if (resultSlotPrefab == null) Debug.LogWarning("GachaUI: resultSlotPrefab not assigned.");
        if (resultsScrollRect == null) Debug.LogWarning("GachaUI: resultsScrollRect not assigned.");
        if (heroPreviewPanelComponent == null) Debug.LogWarning("GachaUI: heroPreviewPanelComponent not assigned.");
    }

    void Update()
    {
        if (gemsText != null && currency != null)
            gemsText.text = $"Gems: {currency.gems}";
    }

    public void OnPullOnce()
    {
        if (currency == null || gacha == null) return;
        if (!currency.Spend(costPerPull))
        {
            Debug.Log("GachaUI: Not enough gems for 1 pull.");
            return;
        }
        List<CharacterData> results = gacha.Pull(1);
        HandleResults(results);
    }

    public void OnPullTen()
    {
        if (currency == null || gacha == null) return;
        int totalCost = costPerPull * 10;
        if (!currency.Spend(totalCost))
        {
            Debug.Log("GachaUI: Not enough gems for 10 pulls.");
            return;
        }
        List<CharacterData> results = gacha.Pull(10);
        HandleResults(results);
    }

    void HandleResults(List<CharacterData> results)
    {
        if (results == null || results.Count == 0) return;

        AppendResultsUI(results);
        ShowResults3D(results);
        if (inventory != null)
        {
            foreach (var r in results)
                inventory.Add(r);
        }
    }

    void AppendResultsUI(List<CharacterData> results)
    {
        if (resultsContainer == null || resultSlotPrefab == null)
        {
            Debug.LogWarning("AppendResultsUI: resultsContainer or resultSlotPrefab not assigned.");
            return;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var data = results[i];
            var go = Instantiate(resultSlotPrefab, resultsContainer, false);
            go.transform.localScale = Vector3.one;

            var slotComp = go.GetComponent<ResultSlot>();
            if (slotComp != null)
            {
                slotComp.SetData(data);
            }
            else
            {
                var txt = go.GetComponentInChildren<Text>();
                var img = go.GetComponentInChildren<Image>();
                if (txt != null) txt.text = data?.displayName ?? "(no name)";
                if (img != null && data?.sprite != null) img.sprite = data.sprite;
            }

            // force layout
            Canvas.ForceUpdateCanvases();
            var rect = resultsContainer as RectTransform;
            if (rect != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            // get preview texture and source rect size from slot if available
            Texture slotPreviewTexture = null;
            Vector2 slotPreviewSourceSize = Vector2.zero;
            if (slotComp != null && slotComp.previewRaw != null)
            {
                slotPreviewTexture = slotComp.previewRaw.texture;
                var rt = slotComp.previewRaw.rectTransform;
                if (rt != null)
                {
                    // use rect size (width x height) of the preview RawImage inside the slot
                    slotPreviewSourceSize = new Vector2(rt.rect.width, rt.rect.height);
                    // if rect is zero (sometimes before layout), use sizeDelta as fallback
                    if (Mathf.Approximately(slotPreviewSourceSize.x, 0f) || Mathf.Approximately(slotPreviewSourceSize.y, 0f))
                        slotPreviewSourceSize = rt.sizeDelta;
                }
            }

            // find view button
            Button viewBtn = FindButtonByName(go, "ViewButton") ?? go.GetComponentInChildren<Button>();
            if (viewBtn != null)
            {
                viewBtn.interactable = true;
                viewBtn.onClick.RemoveAllListeners();
                CharacterData captured = data;
                Texture capturedPreviewTexture = slotPreviewTexture;
                Vector2 capturedSourceSize = slotPreviewSourceSize;

                viewBtn.onClick.AddListener(() =>
                {
                    Debug.Log($"ViewButton onClick invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    if (heroPreviewPanelComponent != null)
                        heroPreviewPanelComponent.Show(captured, capturedPreviewTexture, capturedSourceSize);
                    else if (HeroPreviewPanel.Instance != null)
                        HeroPreviewPanel.Instance.Show(captured, capturedPreviewTexture, capturedSourceSize);
                    else
                        Debug.LogWarning("No HeroPreviewPanel available to show preview.");
                });

                var nav = viewBtn.navigation;
                nav.mode = Navigation.Mode.None;
                viewBtn.navigation = nav;

                var trigger = viewBtn.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = viewBtn.gameObject.AddComponent<EventTrigger>();
                trigger.triggers.RemoveAll(e => e.eventID == EventTriggerType.PointerDown);

                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                entry.callback.AddListener((eventData) =>
                {
                    Debug.Log($"ViewButton PointerDown invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    if (heroPreviewPanelComponent != null)
                        heroPreviewPanelComponent.Show(captured, capturedPreviewTexture, capturedSourceSize);
                    else if (HeroPreviewPanel.Instance != null)
                        HeroPreviewPanel.Instance.Show(captured, capturedPreviewTexture, capturedSourceSize);
                });
                trigger.triggers.Add(entry);
            }
        }

        // final rebuild and scroll
        var rectFinal = resultsContainer as RectTransform;
        if (rectFinal != null)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rectFinal);

        if (resultsScrollRect != null)
            StartCoroutine(ScrollToBottomNextFrame());
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        if (resultsScrollRect == null) yield break;
        resultsScrollRect.verticalNormalizedPosition = 0f;
        yield return null;
    }

    // (Keep your existing ShowResults3D/PopIn/Show3DPreviewInWorld/Clear3DPreview implementations)
    void ShowResults3D(List<CharacterData> results) { /* existing code */ }
    IEnumerator PopIn(Transform t, Vector3 targetScale, float duration) { yield break; }
    public void Show3DPreviewInWorld(CharacterData data) { /* existing code */ }
    public void Clear3DPreview() { /* existing code */ }

    private Button FindButtonByName(GameObject root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            if (b.gameObject.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return null;
    }
}