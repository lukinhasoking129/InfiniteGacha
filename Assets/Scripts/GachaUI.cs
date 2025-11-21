using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// GachaUI - manages gacha UI, result slots and hero preview panel.
/// This version includes fixes to ensure the "Visualizar" button responds on first click
/// by forcing layout rebuild and adding a PointerDown EventTrigger fallback.
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
    public Transform resultsContainer;    // Content of the ScrollView (Grid Layout)
    public GameObject resultSlotPrefab;   // Slot prefab (must contain ResultSlot or compatible elements)
    public ScrollRect resultsScrollRect;

    [Header("Hero Preview Panel (UI)")]
    public GameObject heroPreviewPanel;   // scene instance (drag the Hierarchy instance here)
    public Image heroPreviewIcon;
    public Text heroPreviewName;
    public Button heroPreviewCloseButton;

    [Header("3D preview (world)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewSpacing = 1.2f;
    public float previewScale = 1f;
    public float previewLift = 0.5f;

    // runtime
    private GameObject currentPreviewInstance;

    void Awake()
    {
        // Ensure hero preview panel starts closed (if assigned)
        if (heroPreviewPanel != null)
            heroPreviewPanel.SetActive(false);

        // wire close button if available
        if (heroPreviewCloseButton != null)
        {
            heroPreviewCloseButton.onClick.RemoveAllListeners();
            heroPreviewCloseButton.onClick.AddListener(CloseHeroPreviewPanel);
        }
    }

    void Start()
    {
        if (gacha == null) Debug.LogWarning("GachaUI: gacha not assigned.");
        if (currency == null) Debug.LogWarning("GachaUI: currency not assigned.");
        if (resultsContainer == null) Debug.LogWarning("GachaUI: resultsContainer not assigned.");
        if (resultSlotPrefab == null) Debug.LogWarning("GachaUI: resultSlotPrefab not assigned.");
        if (resultsScrollRect == null) Debug.LogWarning("GachaUI: resultsScrollRect not assigned.");
    }

    void Update()
    {
        if (gemsText != null && currency != null)
            gemsText.text = $"Gems: {currency.gems}";
    }

    // Public UI hooks
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

    // Core flow
    void HandleResults(List<CharacterData> results)
    {
        if (results == null || results.Count == 0)
        {
            Debug.Log("GachaUI: No results returned.");
            return;
        }

        AppendResultsUI(results);
        ShowResults3D(results); // keep original behavior (world previews)
        if (inventory != null)
        {
            foreach (var r in results)
                inventory.Add(r);
        }
    }

    // Append UI slots (improved to avoid first-click ignored)
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

            // try to use ResultSlot if present
            var slotComp = go.GetComponent<ResultSlot>();
            if (slotComp != null)
            {
                slotComp.SetData(data);
            }
            else
            {
                // fallback fill image/text
                var txt = go.GetComponentInChildren<Text>();
                var img = go.GetComponentInChildren<Image>();
                if (txt != null) txt.text = data?.displayName ?? "(no name)";
                if (img != null && data?.sprite != null) img.sprite = data.sprite;
            }

            // Force layout to be up-to-date so clicks are not consumed by layout changes
            Canvas.ForceUpdateCanvases();
            var rect = resultsContainer as RectTransform;
            if (rect != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            // find the specific "View" button (prefer by name to avoid picking e.g. Pull buttons)
            Button viewBtn = FindButtonByName(go, "ViewButton") ?? go.GetComponentInChildren<Button>();
            if (viewBtn != null)
            {
                // ensure interactable
                viewBtn.interactable = true;

                // remove previous runtime listeners and add ours
                viewBtn.onClick.RemoveAllListeners();
                CharacterData captured = data; // capture local for closure
                viewBtn.onClick.AddListener(() =>
                {
                    Debug.Log($"ViewButton onClick invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    OpenHeroPreviewPanel(captured);
                });

                // disable automatic navigation to avoid first-click selection behavior
                var nav = viewBtn.navigation;
                nav.mode = Navigation.Mode.None;
                viewBtn.navigation = nav;

                // Add EventTrigger PointerDown to ensure immediate response (helpful inside ScrollRect / touch)
                var trigger = viewBtn.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = viewBtn.gameObject.AddComponent<EventTrigger>();

                // remove existing PointerDown entries to avoid duplicates
                trigger.triggers.RemoveAll(entry => entry.eventID == EventTriggerType.PointerDown);

                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                entry.callback.AddListener((eventData) =>
                {
                    Debug.Log($"ViewButton PointerDown invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    OpenHeroPreviewPanel(captured);
                });
                trigger.triggers.Add(entry);
            }
        }

        // force layout rebuild and scroll
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

    // Hero preview UI
    public void OpenHeroPreviewPanel(CharacterData data)
    {
        Debug.Log($"OpenHeroPreviewPanel called for '{(data == null ? "null" : data.displayName)}'");

        if (heroPreviewPanel == null)
        {
            Debug.LogWarning("OpenHeroPreviewPanel: heroPreviewPanel not assigned in Inspector.");
            return;
        }

        // populate content
        if (heroPreviewIcon != null)
        {
            if (data != null && data.sprite != null)
            {
                heroPreviewIcon.sprite = data.sprite;
                heroPreviewIcon.color = Color.white;
            }
            else
            {
                heroPreviewIcon.sprite = null;
                heroPreviewIcon.color = new Color(1,1,1,0);
            }
        }

        if (heroPreviewName != null)
            heroPreviewName.text = data?.displayName ?? "(no name)";

        // show panel and bring to front
        heroPreviewPanel.SetActive(true);
        heroPreviewPanel.transform.SetAsLastSibling();

        // ensure visible if CanvasGroup or Canvas ordering interferes
        var cg = heroPreviewPanel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
        var parentCanvas = heroPreviewPanel.GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            parentCanvas.overrideSorting = true;
            parentCanvas.sortingOrder = 1000;
        }
    }

    // Debug version that logs more info and forces visibility (use only while debugging)
    public void OpenHeroPreviewPanel_Debug(CharacterData data)
    {
        Debug.Log($"OpenHeroPreviewPanel_Debug called for '{(data == null ? "null" : data.displayName)}'");

        if (heroPreviewPanel == null)
        {
            Debug.LogWarning("OpenHeroPreviewPanel_Debug: heroPreviewPanel IS NULL in Inspector!");
            return;
        }

        RectTransform rt = heroPreviewPanel.GetComponent<RectTransform>();
        CanvasGroup cg = heroPreviewPanel.GetComponent<CanvasGroup>();
        Canvas parentCanvas = heroPreviewPanel.GetComponentInParent<Canvas>();

        Debug.Log($"Before: activeSelf={heroPreviewPanel.activeSelf} activeInHierarchy={heroPreviewPanel.activeInHierarchy} parent={(heroPreviewPanel.transform.parent ? heroPreviewPanel.transform.parent.name : "null")}");
        if (rt != null)
            Debug.Log($"RectTransform: localScale={rt.localScale} anchoredPosition={rt.anchoredPosition} sizeDelta={rt.sizeDelta}");
        Debug.Log($"CanvasGroup: {(cg == null ? "null" : $"alpha={cg.alpha} interactable={cg.interactable} blocks={cg.blocksRaycasts}")}");
        if (parentCanvas == null)
            Debug.Log("Parent Canvas: null");
        else
            Debug.Log($"Parent Canvas: {parentCanvas.name} sort={parentCanvas.sortingOrder}");

        // force show
        heroPreviewPanel.SetActive(true);
        if (rt != null) rt.localScale = Vector3.one;
        heroPreviewPanel.transform.SetAsLastSibling();

        if (parentCanvas != null)
        {
            parentCanvas.overrideSorting = true;
            parentCanvas.sortingOrder = 9999;
            Debug.Log("Forced parentCanvas.overrideSorting=true and sortingOrder=9999");
        }

        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        Debug.Log($"After: activeSelf={heroPreviewPanel.activeSelf} activeInHierarchy={heroPreviewPanel.activeInHierarchy} siblingIndex={heroPreviewPanel.transform.GetSiblingIndex()}");
        if (heroPreviewIcon != null) Debug.Log($"heroPreviewIcon assigned? {(heroPreviewIcon.sprite == null ? "NO sprite" : "has sprite")}");
    }

    public void CloseHeroPreviewPanel()
    {
        if (heroPreviewPanel != null)
            heroPreviewPanel.SetActive(false);

        Clear3DPreview();
    }

    // World previews (kept as original behavior)
    void ShowResults3D(List<CharacterData> results)
    {
        if (results == null || results.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("ShowResults3D: Camera.main is null.");
            return;
        }

        if (results3DParent != null && results3DParent.localScale == Vector3.zero)
            Debug.LogWarning("ShowResults3D: results3DParent localScale=(0,0,0). Set to (1,1,1).");

        // clear old
        if (results3DParent != null)
        {
            for (int i = results3DParent.childCount - 1; i >= 0; i--)
                Destroy(results3DParent.GetChild(i).gameObject);
        }

        Vector3 center;
        Vector3 right;
        Vector3 up;
        if (previewSpawnPoint != null)
        {
            center = previewSpawnPoint.position;
            right = previewSpawnPoint.right;
            up = previewSpawnPoint.up;
        }
        else
        {
            center = cam.transform.position + cam.transform.forward * 3f;
            right = cam.transform.right;
            up = Vector3.up;
        }

        int count = results.Count;
        float totalWidth = (count - 1) * previewSpacing;
        float startOffset = -totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var r = results[i];
            if (r == null || r.prefab3D == null)
            {
                Debug.Log($"ShowResults3D: result {i} has no prefab3D (ignored).");
                continue;
            }

            Vector3 spawnPos = center + right * (startOffset + i * previewSpacing) + up * previewLift;
            var instance = Instantiate(r.prefab3D, spawnPos, Quaternion.identity);
            if (results3DParent != null)
                instance.transform.SetParent(results3DParent, true);

            instance.transform.localScale = Vector3.one * previewScale;

            // look at camera yaw only
            if (cam != null)
            {
                Vector3 dir = cam.transform.position - instance.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    instance.transform.rotation = Quaternion.LookRotation(dir);
            }

            var cols = instance.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.enabled = false;

            // pop-in animation
            instance.transform.localScale = Vector3.zero;
            StartCoroutine(PopIn(instance.transform, Vector3.one * previewScale, 0.12f + i * 0.03f));
        }

        Debug.Log($"ShowResults3D: spawned {count} previews at center={center} spacing={previewSpacing}");
    }

    IEnumerator PopIn(Transform t, Vector3 targetScale, float duration)
    {
        if (t == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            t.localScale = Vector3.Lerp(Vector3.zero, targetScale, p);
            yield return null;
        }
        t.localScale = targetScale;
    }

    // single preview from button (world-space)
    public void Show3DPreview(CharacterData data)
    {
        Camera cam = Camera.main;
        Vector3 spawnPos = previewSpawnPoint != null ? previewSpawnPoint.position :
            (cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero);

        if (data == null || data.prefab3D == null)
        {
            Debug.Log("Show3DPreview: data is null or prefab3D missing.");
            return;
        }

        if (currentPreviewInstance != null) Destroy(currentPreviewInstance);

        currentPreviewInstance = results3DParent != null
            ? Instantiate(data.prefab3D, spawnPos, Quaternion.identity, results3DParent)
            : Instantiate(data.prefab3D, spawnPos, Quaternion.identity);

        currentPreviewInstance.transform.localScale = Vector3.one * previewScale;

        var cols = currentPreviewInstance.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;

        if (cam != null)
        {
            Vector3 look = cam.transform.position - currentPreviewInstance.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) currentPreviewInstance.transform.rotation = Quaternion.LookRotation(look);
        }

        currentPreviewInstance.transform.localScale = Vector3.zero;
        StartCoroutine(PopIn(currentPreviewInstance.transform, Vector3.one * previewScale, 0.2f));
    }

    // Clear world previews
    public void Clear3DPreview()
    {
        if (currentPreviewInstance != null) Destroy(currentPreviewInstance);

        if (results3DParent != null)
        {
            for (int i = results3DParent.childCount - 1; i >= 0; i--)
                Destroy(results3DParent.GetChild(i).gameObject);
        }
    }

    // Helper: try to find a button by child name inside slot; returns first child Button matching name
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