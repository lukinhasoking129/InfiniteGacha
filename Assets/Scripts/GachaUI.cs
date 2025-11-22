using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// GachaUI - gerencia UI do gacha, criação de slots e integração com HeroPreviewPanel.
/// - Usa heroPreviewPanelComponent.Show(data) para abrir o painel (assim integra com seu script existente).
/// - Mantém correções para primeiro clique (Layout rebuild + PointerDown fallback).
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
    public Transform resultsContainer;    // Content do ScrollView (Grid Layout)
    public GameObject resultSlotPrefab;   // Prefab do slot (deve conter botão "View" ou equivalente)
    public ScrollRect resultsScrollRect;

    [Header("Hero Preview Panel (component)")]
    [Tooltip("Arraste a instância na cena que possui o componente HeroPreviewPanel.")]
    public HeroPreviewPanel heroPreviewPanelComponent;

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
        // não mexe no painel aqui — deixa o HeroPreviewPanel cuidar do seu estado
    }

    void Start()
    {
        if (gacha == null) Debug.LogWarning("GachaUI: gacha not assigned.");
        if (currency == null) Debug.LogWarning("GachaUI: currency not assigned.");
        if (resultsContainer == null) Debug.LogWarning("GachaUI: resultsContainer not assigned.");
        if (resultSlotPrefab == null) Debug.LogWarning("GachaUI: resultSlotPrefab not assigned.");
        if (resultsScrollRect == null) Debug.LogWarning("GachaUI: resultsScrollRect not assigned.");
        if (heroPreviewPanelComponent == null) Debug.LogWarning("GachaUI: heroPreviewPanelComponent not assigned (drag the panel instance).");
    }

    void Update()
    {
        if (gemsText != null && currency != null)
            gemsText.text = $"Gems: {currency.gems}";
    }

    // ------------------------
    // Pull buttons
    // ------------------------
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

    // ------------------------
    // Processa resultados
    // ------------------------
    void HandleResults(List<CharacterData> results)
    {
        if (results == null || results.Count == 0)
        {
            Debug.Log("GachaUI: No results returned.");
            return;
        }

        AppendResultsUI(results);
        ShowResults3D(results); // comportamento original: spawn 3D no mundo
        if (inventory != null)
        {
            foreach (var r in results)
                inventory.Add(r);
        }
    }

    // ------------------------
    // Append UI slots (com correções para primeiro clique)
    // ------------------------
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

            // usa ResultSlot se existir
            var slotComp = go.GetComponent<ResultSlot>();
            if (slotComp != null)
            {
                slotComp.SetData(data);
            }
            else
            {
                // fallback: preenche texto/imagem
                var txt = go.GetComponentInChildren<Text>();
                var img = go.GetComponentInChildren<Image>();
                if (txt != null) txt.text = data?.displayName ?? "(no name)";
                if (img != null && data?.sprite != null) img.sprite = data.sprite;
            }

            // garante layout atualizado para evitar consumo do primeiro clique
            Canvas.ForceUpdateCanvases();
            var rect = resultsContainer as RectTransform;
            if (rect != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            // encontra botão "View" por nome ou pega primeiro Button filho
            Button viewBtn = FindButtonByName(go, "ViewButton") ?? go.GetComponentInChildren<Button>();
            if (viewBtn != null)
            {
                viewBtn.interactable = true;

                // remove listeners antigos e adiciona o nosso (captura a variável local)
                viewBtn.onClick.RemoveAllListeners();
                CharacterData captured = data;
                viewBtn.onClick.AddListener(() =>
                {
                    Debug.Log($"ViewButton onClick invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    if (heroPreviewPanelComponent != null)
                        heroPreviewPanelComponent.Show(captured);
                    else
                        Debug.LogWarning("heroPreviewPanelComponent not assigned on GachaUI.");
                });

                // desativa navigation para evitar "selecionar no primeiro clique"
                var nav = viewBtn.navigation;
                nav.mode = Navigation.Mode.None;
                viewBtn.navigation = nav;

                // adiciona EventTrigger.PointerDown para resposta imediata em ScrollRect/touch
                var trigger = viewBtn.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = viewBtn.gameObject.AddComponent<EventTrigger>();

                // remove entradas PointerDown antigas
                trigger.triggers.RemoveAll(e => e.eventID == EventTriggerType.PointerDown);

                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                entry.callback.AddListener((eventData) =>
                {
                    Debug.Log($"ViewButton PointerDown invoked for '{(captured == null ? "null" : captured.displayName)}'");
                    if (heroPreviewPanelComponent != null)
                        heroPreviewPanelComponent.Show(captured);
                });
                trigger.triggers.Add(entry);
            }
        }

        // rebuild final e rolar
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

    // ------------------------
    // World previews (comportamento original)
    // ------------------------
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

        // limpa previews antigos
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

            // look yaw to camera
            if (cam != null)
            {
                Vector3 dir = cam.transform.position - instance.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    instance.transform.rotation = Quaternion.LookRotation(dir);
            }

            var cols = instance.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.enabled = false;

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

    // single preview from external calls (shows panel via component)
    public void Show3DPreviewInWorld(CharacterData data)
    {
        Camera cam = Camera.main;
        Vector3 spawnPos = previewSpawnPoint != null ? previewSpawnPoint.position :
            (cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero);

        if (data == null || data.prefab3D == null)
        {
            Debug.Log("Show3DPreviewInWorld: data is null or prefab3D missing.");
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

    // Close hero preview (delegates to component)
    public void CloseHeroPreviewPanel()
    {
        if (heroPreviewPanelComponent != null) heroPreviewPanelComponent.Hide();
    }

    // Helper: find a Button by child name
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