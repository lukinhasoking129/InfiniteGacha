using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// HeroPreviewPanel
/// Robust Show/Hide with deterministic layout:
/// - Reparents to top canvas to avoid clipping, disables ancestor masks/layouts while open.
/// - Forces panel RectTransform to full-stretch after reparent to ensure it covers the whole canvas.
/// - Computes left area and centers the preview RawImage inside it, preserving aspect ratio.
/// - Hides external UI (e.g. gems counter) and restores on close.
/// - Auto-fixes header (name/close) and finds stats container if not assigned.
/// - Temporarily disables LayoutGroup/ContentSizeFitter ancestors that could restrict size.
public class HeroPreviewPanel : MonoBehaviour
{
    public static HeroPreviewPanel Instance { get; private set; }

    [Header("References (assign in Inspector)")]
    public GameObject panelRoot;
    public RectTransform panelRootRect;
    public Text nameText;
    public Button closeButton;
    public RawImage bigPreviewRaw;
    public RectTransform statsContainerRect;
    public Text statsText;

    [Header("UI to hide while open")]
    public GameObject[] uiElementsToHideOnOpen;

    [Header("Layout Settings")]
    [Range(0.25f, 0.75f)] public float previewWidthFraction = 0.5f;
    public float headerPadding = 12f;
    public float sidePadding = 12f;
    public float verticalPadding = 12f;
    public int bigPreviewMaxSize = 512;

    [Header("Pull buttons hiding")]
    public List<Button> pullButtonsExplicit = new List<Button>();
    public string[] pullButtonNameMatchers = new string[] { "PullOnce", "PullTen", "Pull 10", "Pull 1", "PullOnceButton", "PullTenButton" };
    public enum HideMode { GameObject, ButtonComponent, CanvasGroup }
    public HideMode hideMode = HideMode.GameObject;
    public bool hidePullButtonsInPanel = true;

    [Header("Preview 3D (optional)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewScale = 1f;
    public bool enable3DPreview = false;

    // runtime / restore state
    GameObject currentPreviewInstance;
    Transform _originalParent;
    int _originalSiblingIndex = -1;
    Canvas _panelCanvas;
    bool _canvasOverrideSortingWasSet;
    int _oldSortingOrder;
    bool _oldOverrideSorting;
    List<Behaviour> _disabledParentMasks = new List<Behaviour>();
    Dictionary<GameObject, bool> _externalActiveBefore = new Dictionary<GameObject, bool>();

    readonly Dictionary<GameObject, bool> _gameObjectActiveBefore = new Dictionary<GameObject, bool>();
    readonly Dictionary<Button, bool> _buttonInteractableBefore = new Dictionary<Button, bool>();
    readonly Dictionary<CanvasGroup, (float alpha, bool interactable, bool blocks)> _canvasGroupBefore = new Dictionary<CanvasGroup, (float, bool, bool)>();

    // disabled layout components to restore later
    readonly List<Behaviour> _disabledLayoutBehaviours = new List<Behaviour>();

    // computed left area (updated after layout)
    float _leftAreaWidth = 0f;
    float _leftAreaHeight = 0f;
    float _leftAreaX = 0f;
    float _leftAreaY = 0f;

    // runner for coroutines when this GO is inactive
    class CoroutineRunner : MonoBehaviour { }
    static CoroutineRunner _runner;
    static CoroutineRunner EnsureRunner()
    {
        if (_runner != null) return _runner;
        var go = GameObject.Find("HeroPreviewPanel_CoroutineRunner");
        if (go == null)
        {
            go = new GameObject("HeroPreviewPanel_CoroutineRunner");
            DontDestroyOnLoad(go);
        }
        _runner = go.GetComponent<CoroutineRunner>();
        if (_runner == null) _runner = go.AddComponent<CoroutineRunner>();
        return _runner;
    }

    void Reset()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (panelRootRect == null && panelRoot != null) panelRootRect = panelRoot.GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(this.gameObject); return; }

        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
        if (panelRootRect == null && panelRoot != null) panelRootRect = panelRoot.GetComponent<RectTransform>();

        EnsureRunner();
    }

    void OnDestroy()
    {
        RestoreHiddenExternalUI();
        RestoreParentCanvasSorting();
        RestorePullButtonsInPanel();
        ReenableParentMasks();
        ReenableAncestorLayouts();
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        RestoreHiddenExternalUI();
        RestoreParentCanvasSorting();
        RestorePullButtonsInPanel();
        ReenableParentMasks();
        ReenableAncestorLayouts();
    }

    // Public Show: use runner so coroutine starts even if this GO is inactive
    public void Show(CharacterData data) { EnsureRunner().StartCoroutine(ShowCoroutine(data, null, Vector2.zero)); }
    public void Show(CharacterData data, Texture previewTexture) { EnsureRunner().StartCoroutine(ShowCoroutine(data, previewTexture, Vector2.zero)); }
    public void Show(CharacterData data, Texture previewTexture, Vector2 sourceSize) { EnsureRunner().StartCoroutine(ShowCoroutine(data, previewTexture, sourceSize)); }

    IEnumerator ShowCoroutine(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (panelRoot == null) { Debug.LogWarning("HeroPreviewPanel.Show: panelRoot not assigned."); yield break; }

        // store parent for restore
        _originalParent = panelRoot.transform.parent;
        _originalSiblingIndex = panelRoot.transform.GetSiblingIndex();

        // find top-most canvas
        Canvas topCanvas = FindTopmostCanvas();
        if (topCanvas == null) topCanvas = panelRoot.GetComponentInParent<Canvas>();

        if (topCanvas != null)
        {
            panelRoot.transform.SetParent(topCanvas.transform, worldPositionStays: false);
            _panelCanvas = topCanvas;
            _oldOverrideSorting = topCanvas.overrideSorting;
            _oldSortingOrder = topCanvas.sortingOrder;
            if (!_oldOverrideSorting || topCanvas.sortingOrder < 1000)
            {
                topCanvas.overrideSorting = true;
                topCanvas.sortingOrder = 1000;
                _canvasOverrideSortingWasSet = true;
            }
        }

        // disable ancestor masks and ancestor/layout groups that could clip/restrict
        DisableAncestorMasks(_originalParent);
        DisableAncestorLayouts(_originalParent);

        // Force panelRect to full-stretch immediately to avoid half-screen panels
        ForceFixPanelRect();

        if (!panelRoot.activeSelf) panelRoot.SetActive(true);
        if (panelRootRect == null) panelRootRect = panelRoot.GetComponent<RectTransform>();

        HideExternalUI();

        if (hidePullButtonsInPanel) DisablePullButtonsInPanel();

        // wait a frame and force rebuild so rects are valid
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (panelRootRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panelRootRect);

        // Auto-fix children backgrounds/containers anchors if some child is restricting panel to right half
        AutoFixPanelChildren();

        // Auto-fix header and stats references/positions if not assigned correctly
        EnsureHeaderAndStats();

        // Arrange layout (sets up left/right areas)
        ArrangeLayout();

        // wait another frame to ensure offsets applied and then compute left area and fit preview
        yield return null;
        Canvas.ForceUpdateCanvases();
        ComputeLeftArea();
        FitBigPreviewToLeftArea(previewTexture, sourceSize);

        panelRoot.transform.SetAsLastSibling();

        if (enable3DPreview && data != null && data.prefab3D != null)
            EnsureRunner().StartCoroutine(PopInCoroutineForInstance(data));

        // finally populate textual content
        Populate(data, previewTexture);
    }

    // Force the panelRoot RectTransform to full-stretch and reset transforms/scales to avoid partial sizes
    void ForceFixPanelRect()
    {
        if (panelRoot == null) return;
        var rt = panelRoot.GetComponent<RectTransform>();
        if (rt == null) return;

        rt.SetParent(rt.parent, worldPositionStays: false); // ensure local coords
        rt.localScale = Vector3.one;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        if (panelRootRect == null) panelRootRect = rt;
        Debug.Log("HeroPreviewPanel: ForceFixPanelRect applied to panelRoot.");
    }

    // Auto-fix: find probable background/containers and force full-stretch anchors so panel covers the full canvas
    void AutoFixPanelChildren()
    {
        if (panelRoot == null) return;
        if (panelRootRect != null)
        {
            panelRootRect.anchorMin = Vector2.zero;
            panelRootRect.anchorMax = Vector2.one;
            panelRootRect.offsetMin = Vector2.zero;
            panelRootRect.offsetMax = Vector2.zero;
        }

        var images = panelRoot.GetComponentsInChildren<Image>(true);
        if (images == null || images.Length == 0) return;

        var ordered = images.OrderBy(i =>
        {
            string n = (i.gameObject.name ?? "").ToLower();
            if (n.Contains("background") || n.Contains("bg")) return 0;
            if (n.Contains("panel") || n.Contains("container")) return 1;
            if (n.Contains("root") || n.Contains("window")) return 2;
            return 3;
        }).ToArray();

        foreach (var img in ordered)
        {
            if (img == null || img.rectTransform == null) continue;
            var rt = img.rectTransform;
            bool looksConstrained = (rt.anchorMin.x > 0.01f || rt.anchorMax.x < 0.99f || rt.anchorMin.y > 0.01f || rt.anchorMax.y < 0.99f)
                                    || (rt.rect.width < (panelRootRect != null ? panelRootRect.rect.width * 0.9f : 0) || rt.rect.height < (panelRootRect != null ? panelRootRect.rect.height * 0.9f : 0));
            if (looksConstrained)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                Debug.Log($"HeroPreviewPanel: AutoFixPanelChildren expanded '{img.gameObject.name}' to full-stretch.");
                break;
            }
        }
    }

    // --- Auto-fix: guarantees header (name/close) and locates stats if necessary ---
    void EnsureHeaderAndStats()
    {
        if (panelRoot == null) return;

        // Auto-assign nameText if missing (search by name or take first Text)
        if (nameText == null)
        {
            var texts = panelRoot.GetComponentsInChildren<Text>(true);
            nameText = texts.FirstOrDefault(t =>
                t.gameObject.name.ToLower().Contains("name") ||
                t.gameObject.name.ToLower().Contains("title") ||
                t.gameObject.name.ToLower().Contains("titulo"));
            if (nameText == null && texts.Length > 0) nameText = texts[0];
            Debug.Log($"HeroPreviewPanel: EnsureHeaderAndStats assigned nameText = {(nameText != null ? nameText.gameObject.name : "null")}");
        }

        // Auto-assign closeButton if missing (search by common names)
        if (closeButton == null)
        {
            var btns = panelRoot.GetComponentsInChildren<Button>(true);
            closeButton = btns.FirstOrDefault(b =>
                b.gameObject.name.ToLower().Contains("close") ||
                b.gameObject.name.ToLower().Contains("fechar") ||
                b.gameObject.name.ToLower().Contains("x"));
            if (closeButton != null) Debug.Log($"HeroPreviewPanel: EnsureHeaderAndStats assigned closeButton = {closeButton.gameObject.name}");
            else Debug.Log("HeroPreviewPanel: EnsureHeaderAndStats did not find a closeButton automatically.");
        }

        // Auto-assign statsContainerRect / statsText: search for 'stat'/'info'/'desc'
        if (statsContainerRect == null || statsText == null)
        {
            var texts = panelRoot.GetComponentsInChildren<Text>(true);
            var candidate = texts.FirstOrDefault(t =>
                t.gameObject.name.ToLower().Contains("stat") ||
                t.gameObject.name.ToLower().Contains("info") ||
                t.gameObject.name.ToLower().Contains("desc") ||
                t.gameObject.name.ToLower().Contains("description"));
            if (candidate != null)
            {
                if (statsText == null) statsText = candidate;
                if (statsContainerRect == null && candidate.transform.parent != null)
                    statsContainerRect = candidate.transform.parent as RectTransform;
                Debug.Log($"HeroPreviewPanel: EnsureHeaderAndStats assigned statsText = {candidate.gameObject.name}, statsContainer = {(statsContainerRect != null ? statsContainerRect.gameObject.name : "null")}");
            }
            else
            {
                Debug.Log("HeroPreviewPanel: EnsureHeaderAndStats did not find stats automatically.");
            }
        }

        // Force header positioning: top-left for name, top-right for close
        if (nameText != null)
        {
            var rt = nameText.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            float w = Mathf.Max(120f, rt.rect.width);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(28f, rt.rect.height));
            rt.anchoredPosition = new Vector2(sidePadding, -headerPadding);
            nameText.alignment = TextAnchor.UpperLeft;
        }

        if (closeButton != null)
        {
            var rt = closeButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            float size = Mathf.Max(28f, Mathf.Min(48f, rt.rect.height));
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
            rt.anchoredPosition = new Vector2(-sidePadding, -headerPadding);
        }

        if (statsContainerRect != null)
        {
            if (!statsContainerRect.gameObject.activeSelf) statsContainerRect.gameObject.SetActive(true);
            statsContainerRect.anchorMin = new Vector2(previewWidthFraction, 0f);
            statsContainerRect.anchorMax = new Vector2(1f, 1f);
            statsContainerRect.pivot = new Vector2(0.5f, 0.5f);
            statsContainerRect.offsetMin = new Vector2(sidePadding, verticalPadding);
            statsContainerRect.offsetMax = new Vector2(-sidePadding, -headerPadding - verticalPadding);
            Debug.Log($"HeroPreviewPanel: statsContainerRect anchored and ensured active: {statsContainerRect.gameObject.name}");
        }
        else
        {
            Debug.Log("HeroPreviewPanel: statsContainerRect is still null after EnsureHeaderAndStats().");
        }

#if UNITY_EDITOR
        var childNames = panelRoot.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject.name).ToArray();
        Debug.Log("HeroPreviewPanel: panel children = " + string.Join(", ", childNames));
#endif
    }

    IEnumerator PopInCoroutineForInstance(CharacterData data)
    {
        Clear3DPreview();
        Vector3 spawnPos; Quaternion spawnRot = Quaternion.identity;
        if (results3DParent != null) spawnPos = results3DParent.position;
        else if (previewSpawnPoint != null) spawnPos = previewSpawnPoint.position;
        else { var cam = Camera.main; spawnPos = cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero; }

        if (data.prefab3D == null) yield break;
        currentPreviewInstance = Instantiate(data.prefab3D, spawnPos, spawnRot);
        if (results3DParent != null) currentPreviewInstance.transform.SetParent(results3DParent, true);
        currentPreviewInstance.transform.localScale = Vector3.one * previewScale;

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector3 dir = mainCam.transform.position - currentPreviewInstance.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) currentPreviewInstance.transform.rotation = Quaternion.LookRotation(dir);
        }

        var cols = currentPreviewInstance.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;

        yield return EnsureRunner().StartCoroutine(PopIn(currentPreviewInstance.transform, Vector3.one * previewScale, 0.18f));
    }

    public void Hide()
    {
        RestorePullButtonsInPanel();
        RestoreHiddenExternalUI();
        ReenableParentMasks();
        ReenableAncestorLayouts();
        RestoreParentCanvasSorting();

        if (_originalParent != null)
        {
            panelRoot.transform.SetParent(_originalParent, worldPositionStays: false);
            if (_originalSiblingIndex >= 0) panelRoot.transform.SetSiblingIndex(_originalSiblingIndex);
        }

        if (panelRoot != null) panelRoot.SetActive(false);
        Clear3DPreview();
    }

    void Populate(CharacterData data, Texture previewTexture)
    {
        if (nameText != null) nameText.text = data?.displayName ?? "(sem nome)";
        if (statsText != null) statsText.text = FormatStats(data);
        if (bigPreviewRaw != null)
        {
            Texture used = previewTexture ?? (data != null && data.sprite != null ? data.sprite.texture : null);
            bigPreviewRaw.texture = used;
            bigPreviewRaw.color = used != null ? Color.white : new Color(1, 1, 1, 0);
        }
    }

    // Arrange header and set up columns (but do NOT size preview here)
    void ArrangeLayout()
    {
        if (panelRootRect == null) return;

        // full-stretch
        panelRootRect.anchorMin = Vector2.zero;
        panelRootRect.anchorMax = Vector2.one;
        panelRootRect.offsetMin = Vector2.zero;
        panelRootRect.offsetMax = Vector2.zero;

        float headerHeight = Mathf.Max(36f, headerPadding * 2f);
        float panelW = Mathf.Max(200f, panelRootRect.rect.width);

        // Name top-left (fixed anchor)
        if (nameText != null)
        {
            var rt = nameText.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            float nameW = Mathf.Clamp(panelW * 0.4f, 120f, panelW * 0.7f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, nameW);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, headerHeight);
            rt.anchoredPosition = new Vector2(sidePadding, -headerPadding);
            nameText.alignment = TextAnchor.UpperLeft;
        }

        // Close top-right
        if (closeButton != null)
        {
            var rt = closeButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            float size = Mathf.Clamp(headerHeight - headerPadding, 28f, headerHeight);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
            rt.anchoredPosition = new Vector2(-sidePadding, -headerPadding);
        }

        // Setup preview area anchor to top-left while we'll compute absolute center later
        if (bigPreviewRaw != null)
        {
            var rt = bigPreviewRaw.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // Stats (right column) stretch
        RectTransform statsRt = statsContainerRect != null ? statsContainerRect : (statsText != null ? statsText.rectTransform : null);
        if (statsRt != null)
        {
            statsRt.anchorMin = new Vector2(previewWidthFraction, 0f);
            statsRt.anchorMax = new Vector2(1f, 1f);
            statsRt.pivot = new Vector2(0.5f, 0.5f);
            statsRt.offsetMin = new Vector2(sidePadding, verticalPadding);
            statsRt.offsetMax = new Vector2(-sidePadding, -headerHeight - verticalPadding);
            if (statsText != null) statsText.alignment = TextAnchor.UpperLeft;
        }
    }

    // After layout rebuild, compute the available left-area rect (absolute in panel rect coords)
    void ComputeLeftArea()
    {
        if (panelRootRect == null || bigPreviewRaw == null) { _leftAreaWidth = _leftAreaHeight = 0f; return; }

        float panelW = panelRootRect.rect.width;
        float panelH = panelRootRect.rect.height;
        float headerH = Mathf.Max(36f, headerPadding * 2f);

        _leftAreaX = sidePadding;
        _leftAreaY = -headerH - verticalPadding; // negative because anchored from top

        float leftAreaTotalW = Mathf.Max(10f, panelW * previewWidthFraction - sidePadding * 2f);
        float leftAreaTotalH = Mathf.Max(10f, panelH - headerH - verticalPadding * 2f);

        _leftAreaWidth = leftAreaTotalW;
        _leftAreaHeight = leftAreaTotalH;
    }

    // Fit preview preserving aspect and center it inside left area
    void FitBigPreviewToLeftArea(Texture previewTexture, Vector2 sourceSize)
    {
        if (bigPreviewRaw == null || panelRootRect == null) return;

        float w = 0f, h = 0f;
        if (sourceSize.x > 0.001f && sourceSize.y > 0.001f) { w = sourceSize.x; h = sourceSize.y; }
        else if (previewTexture != null) { w = previewTexture.width; h = previewTexture.height; }
        else if (bigPreviewRaw.texture != null) { w = bigPreviewRaw.texture.width; h = bigPreviewRaw.texture.height; }
        else { return; }

        if (h <= 0f) h = 1f;
        float aspect = Mathf.Abs(w / h);

        float maxW = _leftAreaWidth;
        float maxH = _leftAreaHeight;
        if (maxW <= 0f || maxH <= 0f)
        {
            maxW = panelRootRect.rect.width * previewWidthFraction - sidePadding * 2f;
            maxH = panelRootRect.rect.height - headerPadding * 3f;
        }

        float fittedW = maxW;
        float fittedH = fittedW / aspect;
        if (fittedH > maxH)
        {
            fittedH = maxH;
            fittedW = fittedH * aspect;
        }

        float maxSide = Mathf.Max(fittedW, fittedH);
        if (maxSide > bigPreviewMaxSize)
        {
            float scale = bigPreviewMaxSize / maxSide;
            fittedW *= scale;
            fittedH *= scale;
        }

        var rt = bigPreviewRaw.rectTransform;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, fittedW);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fittedH);

        float centerX = _leftAreaX + (_leftAreaWidth * 0.5f);
        float centerY = _leftAreaY - (_leftAreaHeight * 0.5f); // negative because anchored from top

        rt.anchoredPosition = new Vector2(centerX, centerY);

        var le = bigPreviewRaw.GetComponent<LayoutElement>();
        if (le == null) le = bigPreviewRaw.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        le.preferredWidth = fittedW;
        le.preferredHeight = fittedH;
    }

    // --- ancestor layout handling (disable LayoutGroup / ContentSizeFitter) ---
    void DisableAncestorLayouts(Transform start)
    {
        _disabledLayoutBehaviours.Clear();
        if (start == null) return;
        Transform t = start;
        while (t != null)
        {
            // LayoutGroup (Horizontal/Vertical/Grid) or ContentSizeFitter
            var layout = t.GetComponent<LayoutGroup>();
            if (layout != null && layout.enabled) { layout.enabled = false; _disabledLayoutBehaviours.Add(layout); Debug.Log($"HeroPreviewPanel: disabled LayoutGroup on {t.gameObject.name}"); }
            var csf = t.GetComponent<ContentSizeFitter>();
            if (csf != null && csf.enabled) { csf.enabled = false; _disabledLayoutBehaviours.Add(csf); Debug.Log($"HeroPreviewPanel: disabled ContentSizeFitter on {t.gameObject.name}"); }
            t = t.parent;
        }
    }

    void ReenableAncestorLayouts()
    {
        foreach (var b in _disabledLayoutBehaviours)
        {
            if (b == null) continue;
            b.enabled = true;
            Debug.Log($"HeroPreviewPanel: re-enabled layout behaviour {b.GetType().Name} on {b.gameObject.name}");
        }
        _disabledLayoutBehaviours.Clear();
    }

    // --- external UI hide/restore ---
    void HideExternalUI()
    {
        _externalActiveBefore.Clear();
        if (uiElementsToHideOnOpen == null || uiElementsToHideOnOpen.Length == 0) return;
        foreach (var go in uiElementsToHideOnOpen)
        {
            if (go == null) continue;
            _externalActiveBefore[go] = go.activeSelf;
            go.SetActive(false);
        }
    }

    void RestoreHiddenExternalUI()
    {
        foreach (var kv in _externalActiveBefore.ToList())
        {
            var go = kv.Key;
            if (go != null) go.SetActive(kv.Value);
        }
        _externalActiveBefore.Clear();
    }

    // --- ancestor mask handling ---
    void DisableAncestorMasks(Transform start)
    {
        _disabledParentMasks.Clear();
        if (start == null) return;
        Transform t = start;
        while (t != null)
        {
            var rectMask = t.GetComponent<RectMask2D>();
            if (rectMask != null && rectMask.enabled) { rectMask.enabled = false; _disabledParentMasks.Add(rectMask); Debug.Log($"HeroPreviewPanel: disabled RectMask2D on {t.gameObject.name}"); }
            var mask = t.GetComponent<Mask>();
            if (mask != null && mask.enabled) { mask.enabled = false; _disabledParentMasks.Add(mask); Debug.Log($"HeroPreviewPanel: disabled Mask on {t.gameObject.name}"); }
            t = t.parent;
        }
    }

    void ReenableParentMasks()
    {
        foreach (var comp in _disabledParentMasks) if (comp != null) comp.enabled = true;
        _disabledParentMasks.Clear();
    }

    void RestoreParentCanvasSorting()
    {
        if (_panelCanvas != null && _canvasOverrideSortingWasSet)
        {
            _panelCanvas.overrideSorting = _oldOverrideSorting;
            _panelCanvas.sortingOrder = _oldSortingOrder;
            _panelCanvas = null;
            _canvasOverrideSortingWasSet = false;
        }
    }

    Canvas FindTopmostCanvas()
    {
        var canvases = FindObjectsOfType<Canvas>();
        if (canvases == null || canvases.Length == 0) return null;
        Canvas chosen = canvases.OrderByDescending(c => (c.renderMode == RenderMode.ScreenSpaceOverlay ? 2 : 1)).ThenByDescending(c => c.sortingOrder).First();
        return chosen;
    }

    // --- pull buttons hide/restore (kept) ---
    private List<Button> FindTargetPullButtons()
    {
        var found = new List<Button>();
        if (pullButtonsExplicit != null && pullButtonsExplicit.Count > 0)
        {
            foreach (var b in pullButtonsExplicit) if (b != null && !found.Contains(b)) found.Add(b);
            return found;
        }
        if (panelRoot == null) return found;
        var all = panelRoot.GetComponentsInChildren<Button>(true);
        foreach (var b in all)
        {
            if (b == null) continue;
            string n = b.gameObject.name ?? "";
            foreach (var m in pullButtonNameMatchers)
            {
                if (string.IsNullOrEmpty(m)) continue;
                if (n.IndexOf(m, System.StringComparison.OrdinalIgnoreCase) >= 0) { if (!found.Contains(b)) found.Add(b); break; }
            }
        }
        return found;
    }

    void DisablePullButtonsInPanel()
    {
        var targets = FindTargetPullButtons();
        if (targets == null || targets.Count == 0) return;
        _gameObjectActiveBefore.Clear();
        _buttonInteractableBefore.Clear();
        _canvasGroupBefore.Clear();
        foreach (var btn in targets)
        {
            if (btn == null || btn.gameObject == null) continue;
            switch (hideMode)
            {
                case HideMode.GameObject:
                    if (!_gameObjectActiveBefore.ContainsKey(btn.gameObject)) _gameObjectActiveBefore[btn.gameObject] = btn.gameObject.activeSelf;
                    btn.gameObject.SetActive(false);
                    break;
                case HideMode.ButtonComponent:
                    if (!_buttonInteractableBefore.ContainsKey(btn)) _buttonInteractableBefore[btn] = btn.interactable;
                    btn.interactable = false;
                    break;
                case HideMode.CanvasGroup:
                    var cg = btn.gameObject.GetComponent<CanvasGroup>();
                    if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
                    if (!_canvasGroupBefore.ContainsKey(cg)) _canvasGroupBefore[cg] = (cg.alpha, cg.interactable, cg.blocksRaycasts);
                    cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
                    break;
            }
        }
    }

    void RestorePullButtonsInPanel()
    {
        foreach (var kv in _gameObjectActiveBefore.ToList())
        {
            var go = kv.Key; if (go != null) go.SetActive(kv.Value);
        }
        _gameObjectActiveBefore.Clear();

        foreach (var kv in _buttonInteractableBefore.ToList())
        {
            var b = kv.Key; if (b != null) b.interactable = kv.Value;
        }
        _buttonInteractableBefore.Clear();

        foreach (var kv in _canvasGroupBefore.ToList())
        {
            var cg = kv.Key;
            if (cg != null)
            {
                var v = kv.Value;
                cg.alpha = v.alpha; cg.interactable = v.interactable; cg.blocksRaycasts = v.blocks;
            }
        }
        _canvasGroupBefore.Clear();
    }

    // pop-in animation
    IEnumerator PopIn(Transform t, Vector3 targetScale, float duration)
    {
        if (t == null) yield break;
        float elapsed = 0f;
        Vector3 start = Vector3.zero;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            t.localScale = Vector3.Lerp(start, targetScale, p);
            yield return null;
        }
        t.localScale = targetScale;
    }

    void Clear3DPreview()
    {
        if (currentPreviewInstance != null) { Destroy(currentPreviewInstance); currentPreviewInstance = null; }
    }

    // Stats formatter (kept)
    string FormatStats(object data)
    {
        if (data == null) return "";
        object GetMemberValue(string name)
        {
            var type = data.GetType();
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p != null && p.CanRead) { try { return p.GetValue(data); } catch { return null; } }
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (f != null) { try { return f.GetValue(data); } catch { return null; } }
            return null;
        }

        var sb = new StringBuilder();
        var charId = GetMemberValue("characterId") ?? GetMemberValue("id");
        if (charId != null) sb.AppendLine($"ID: {charId}");

        var preferred = new List<string> { "rarity", "level", "hp", "atk", "def", "spd" };
        foreach (var key in preferred)
        {
            var val = GetMemberValue(key);
            if (val == null) continue;
            string label = key.ToUpper();
            if (key.Equals("hp", System.StringComparison.OrdinalIgnoreCase)) label = "HP";
            else if (key.Equals("atk", System.StringComparison.OrdinalIgnoreCase)) label = "ATK";
            else if (key.Equals("def", System.StringComparison.OrdinalIgnoreCase)) label = "DEF";
            else if (key.Equals("spd", System.StringComparison.OrdinalIgnoreCase)) label = "SPD";
            else if (key.Equals("level", System.StringComparison.OrdinalIgnoreCase)) label = "Level";
            else if (key.Equals("rarity", System.StringComparison.OrdinalIgnoreCase)) label = "Rarity";
            sb.AppendLine($"{label}: {val}");
        }

        var desc = GetMemberValue("description") as string;
        if (!string.IsNullOrEmpty(desc)) { sb.AppendLine(); sb.AppendLine("Description:"); sb.AppendLine(desc); }

        var outStr = sb.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(outStr)) return outStr;

        var typeFallback = data.GetType();
        var exclude = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "hideFlags","gameObject","transform","sprite","prefab3D","displayName","name"
        };

        var sb2 = new StringBuilder();
        var props = typeFallback.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var p in props)
        {
            if (!p.CanRead) continue;
            if (exclude.Contains(p.Name)) continue;
            object val = null;
            try { val = p.GetValue(data); } catch { continue; }
            if (val == null) continue;
            var pt = p.PropertyType;
            if (pt.IsPrimitive || pt.IsEnum || pt == typeof(string) || pt.IsValueType) sb2.AppendLine($"{p.Name}: {val}");
        }

        var fields = typeFallback.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var f in fields)
        {
            if (exclude.Contains(f.Name)) continue;
            object val = null;
            try { val = f.GetValue(data); } catch { continue; }
            if (val == null) continue;
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft.IsValueType) sb2.AppendLine($"{f.Name}: {val}");
        }

        outStr = sb2.ToString().TrimEnd();
        return string.IsNullOrEmpty(outStr) ? "(no stats)" : outStr;
    }
}