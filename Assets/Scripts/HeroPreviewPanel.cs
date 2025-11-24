using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// HeroPreviewPanel
/// - Gerencia o painel modal de preview do herói.
/// - Popula ícone, nome, stats e um preview grande (RawImage).
/// - Pode ocultar/mostrar botões de Pull e contador de gems quando o painel abre,
///   e restaura o estado original quando fecha.
/// - Permite posicionar o nome no canto superior esquerdo do painel e o close no topo direito.
/// - Suporta layout "split": preview (esquerda) / stats (direita).
public class HeroPreviewPanel : MonoBehaviour
{
    public static HeroPreviewPanel Instance { get; private set; }

    [Header("UI refs (arraste no Inspector)")]
    public GameObject panelRoot;
    public Image iconImage;
    public Text nameText;
    public Text statsText;
    public RawImage bigPreviewRaw;
    public int bigPreviewMaxSize = 320;
    public Button closeButton;

    [Header("Split layout")]
    [Tooltip("Se true, o painel será dividido em duas colunas (preview + stats).")]
    public bool enableSplitLayout = true;
    [Tooltip("Se true, o preview ficará à esquerda; se false, ficará à direita.")]
    public bool previewOnLeft = true;
    [Range(0.25f, 0.75f)]
    [Tooltip("Proporção (0..1) da largura ocupada pelo preview (ex.: 0.5 = metade).")]
    public float previewWidthPercent = 0.5f;
    [Tooltip("Nome dos containers criados em tempo de execução (substitua se preferir).")]
    public string previewContainerName = "HeroPreviewPanel_PreviewContainer";
    public string statsContainerName = "HeroPreviewPanel_StatsContainer";

    [Header("Name positioning (top-left)")]
    public bool moveNameToTopLeft = true;
    public RectTransform nameRect; // opcional
    public Vector2 nameTopLeftOffset = new Vector2(10f, -10f);
    public float namePreferredWidth = 0f;

    [Header("Close button positioning (top-right)")]
    public bool moveCloseToTopRight = true;
    public RectTransform closeRect; // optional
    public Vector2 closeTopRightOffset = new Vector2(-10f, -10f);
    public Vector2 closePreferredSize = Vector2.zero;

    [Header("Pull buttons hiding (choose one)")]
    public List<Button> pullButtonsExplicit = new List<Button>();
    public string[] pullButtonNameMatchers = new string[] { "PullOnce", "PullTen", "Pull 10", "Pull 1", "PullOnceButton", "PullTenButton" };

    public enum HideMode { GameObject, ButtonComponent, CanvasGroup }
    public HideMode hideMode = HideMode.GameObject;
    public bool hidePullButtonsInPanel = true;

    [Header("Gems counter hiding")]
    public GameObject gemsCounterObject;
    public bool hideGemsCounter = true;

    [Header("Stats display options")]
    public bool showIdInStats = false;

    [Header("Preview 3D (opcional)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewScale = 1f;
    public bool enable3DPreview = false;

    // runtime
    GameObject currentPreviewInstance;

    // containers created at runtime (cached)
    RectTransform _previewContainer;
    RectTransform _statsContainer;

    // state stores for restore
    private readonly Dictionary<GameObject, bool> _gameObjectActiveBefore = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Button, bool> _buttonInteractableBefore = new Dictionary<Button, bool>();
    private readonly Dictionary<CanvasGroup, (float alpha, bool interactable, bool blocks)> _canvasGroupBefore =
        new Dictionary<CanvasGroup, (float, bool, bool)>();

    // gems counter restore
    private bool _gemsCounterStored = false;
    private bool _gemsCounterPrevActive = false;

    void Reset()
    {
        if (panelRoot == null) panelRoot = this.gameObject;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this)
        {
            Debug.LogWarning("Multiple HeroPreviewPanel instances detected. Destroying duplicate.");
            Destroy(this.gameObject);
            return;
        }

        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
    }

    void OnDestroy()
    {
        RestorePullButtonsInPanel();
        RestoreGemsCounter();
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        RestorePullButtonsInPanel();
        RestoreGemsCounter();
    }

    // Backwards-compatible Show overloads
    public void Show(CharacterData data) { Show(data, null, Vector2.zero); }
    public void Show(CharacterData data, Texture previewTexture) { Show(data, previewTexture, Vector2.zero); }

    // Main Show
    public void Show(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("HeroPreviewPanel.Show: panelRoot not assigned.");
            return;
        }

        // Ensure split containers exist so we can layout correctly later
        if (enableSplitLayout)
            EnsureSplitContainersAndLayout();

        Populate(data, previewTexture, sourceSize);

        if (hidePullButtonsInPanel)
            DisablePullButtonsInPanel();

        if (hideGemsCounter)
            DisableGemsCounter();

        // Make panel visible first
        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        // Start coroutine using a runner guaranteed to be active
        if (enableSplitLayout)
        {
            CoroutineRunner.Instance.Run(ApplySplitLayoutNextFrame());
        }

        if (enable3DPreview && data != null && data.prefab3D != null)
            Create3DPreview(data);
    }

    public void Hide()
    {
        if (hidePullButtonsInPanel)
            RestorePullButtonsInPanel();

        if (hideGemsCounter)
            RestoreGemsCounter();

        if (panelRoot != null)
            panelRoot.SetActive(false);

        Clear3DPreview();
    }

    void Populate(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (iconImage != null)
        {
            if (data != null && data.sprite != null) { iconImage.sprite = data.sprite; iconImage.color = Color.white; }
            else { iconImage.sprite = null; iconImage.color = new Color(1, 1, 1, 0); }
        }

        if (nameText != null)
        {
            nameText.text = data?.displayName ?? "(sem nome)";
            if (moveNameToTopLeft)
                MoveNameToTopLeft();
        }

        if (moveCloseToTopRight && closeButton != null)
            MoveCloseToTopRight();

        if (statsText != null)
        {
            statsText.text = FormatStats(data);
            statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statsText.verticalOverflow = VerticalWrapMode.Overflow;
            statsText.alignment = TextAnchor.UpperLeft;
        }

        if (bigPreviewRaw != null)
        {
            Texture used = previewTexture ?? (data != null && data.sprite != null ? data.sprite.texture : null);
            SetBigPreviewTexture(used, sourceSize);
        }
    }

    // Creates or finds the preview/stats containers and applies anchors to split the panel.
    void EnsureSplitContainersAndLayout()
    {
        if (panelRoot == null) return;
        var panelRt = panelRoot.GetComponent<RectTransform>();
        if (panelRt == null) return;

        Transform existingPreview = panelRt.Find(previewContainerName);
        Transform existingStats = panelRt.Find(statsContainerName);

        if (existingPreview != null) _previewContainer = existingPreview as RectTransform;
        if (existingStats != null) _statsContainer = existingStats as RectTransform;

        if (_previewContainer == null)
        {
            var go = new GameObject(previewContainerName, typeof(RectTransform));
            go.transform.SetParent(panelRt, false);
            _previewContainer = go.GetComponent<RectTransform>();
        }
        if (_statsContainer == null)
        {
            var go = new GameObject(statsContainerName, typeof(RectTransform));
            go.transform.SetParent(panelRt, false);
            _statsContainer = go.GetComponent<RectTransform>();
        }

        if (previewOnLeft)
        {
            _previewContainer.anchorMin = new Vector2(0f, 0f);
            _previewContainer.anchorMax = new Vector2(previewWidthPercent, 1f);
            _previewContainer.anchoredPosition = Vector2.zero;
            _previewContainer.sizeDelta = Vector2.zero;

            _statsContainer.anchorMin = new Vector2(previewWidthPercent, 0f);
            _statsContainer.anchorMax = new Vector2(1f, 1f);
            _statsContainer.anchoredPosition = Vector2.zero;
            _statsContainer.sizeDelta = Vector2.zero;
        }
        else
        {
            _previewContainer.anchorMin = new Vector2(1f - previewWidthPercent, 0f);
            _previewContainer.anchorMax = new Vector2(1f, 1f);
            _previewContainer.anchoredPosition = Vector2.zero;
            _previewContainer.sizeDelta = Vector2.zero;

            _statsContainer.anchorMin = new Vector2(0f, 0f);
            _statsContainer.anchorMax = new Vector2(1f - previewWidthPercent, 1f);
            _statsContainer.anchoredPosition = Vector2.zero;
            _statsContainer.sizeDelta = Vector2.zero;
        }
    }

    // Wait a frame (or until panel rect is valid) then finalize sizes and reparent.
    IEnumerator ApplySplitLayoutNextFrame()
    {
        var panelRt = panelRoot.GetComponent<RectTransform>();
        int attempts = 0;
        while (attempts < 6)
        {
            yield return null;
            if (panelRt != null && panelRt.rect.width > 1f) break;
            attempts++;
        }

        if (_previewContainer == null || _statsContainer == null || panelRt == null)
            yield break;

        float previewW = Mathf.Round(panelRt.rect.width * previewWidthPercent);
        float statsW = Mathf.Round(panelRt.rect.width - previewW);
        float h = Mathf.Round(panelRt.rect.height);

        _previewContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, previewW);
        _previewContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        _statsContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, statsW);
        _statsContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

        if (statsText != null && _statsContainer != null)
        {
            statsText.rectTransform.SetParent(_statsContainer, worldPositionStays: false);
            statsText.rectTransform.anchorMin = new Vector2(0f, 0f);
            statsText.rectTransform.anchorMax = new Vector2(1f, 1f);
            statsText.rectTransform.anchoredPosition = Vector2.zero;
            statsText.rectTransform.sizeDelta = Vector2.zero;
            statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statsText.verticalOverflow = VerticalWrapMode.Overflow;
            statsText.alignment = TextAnchor.UpperLeft;
        }

        if (bigPreviewRaw != null && _previewContainer != null)
        {
            bigPreviewRaw.rectTransform.SetParent(_previewContainer, worldPositionStays: false);
            bigPreviewRaw.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            bigPreviewRaw.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bigPreviewRaw.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bigPreviewRaw.rectTransform.anchoredPosition = Vector2.zero;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_previewContainer);
            SetBigPreviewTexture(bigPreviewRaw.texture, Vector2.zero);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRt);
    }

    void MoveNameToTopLeft()
    {
        RectTransform rt = nameRect != null ? nameRect : (nameText != null ? nameText.rectTransform : null);
        if (rt == null || panelRoot == null) return;

        if (rt.transform.parent != panelRoot.transform)
        {
            Vector3 worldPos = rt.transform.position;
            rt.SetParent(panelRoot.transform, worldPositionStays: true);
            rt.position = worldPos;
        }

        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = nameTopLeftOffset;

        if (namePreferredWidth > 0f)
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, namePreferredWidth);

        var layoutElem = rt.GetComponent<LayoutElement>();
        if (layoutElem == null) layoutElem = rt.gameObject.AddComponent<LayoutElement>();
        layoutElem.ignoreLayout = true;
    }

    void MoveCloseToTopRight()
    {
        RectTransform rt = closeRect != null ? closeRect : (closeButton != null ? closeButton.GetComponent<RectTransform>() : null);
        if (rt == null || panelRoot == null) return;

        if (rt.transform.parent != panelRoot.transform)
        {
            Vector3 worldPos = rt.transform.position;
            rt.SetParent(panelRoot.transform, worldPositionStays: true);
            rt.position = worldPos;
        }

        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = closeTopRightOffset;

        if (closePreferredSize.x > 0f)
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, closePreferredSize.x);
        if (closePreferredSize.y > 0f)
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, closePreferredSize.y);

        var layoutElem = rt.GetComponent<LayoutElement>();
        if (layoutElem == null) layoutElem = rt.gameObject.AddComponent<LayoutElement>();
        layoutElem.ignoreLayout = true;
    }

    private List<Button> FindTargetPullButtons()
    {
        var found = new List<Button>();
        if (pullButtonsExplicit != null && pullButtonsExplicit.Count > 0)
        {
            foreach (var b in pullButtonsExplicit)
                if (b != null && !found.Contains(b)) found.Add(b);
            return found;
        }
        if (panelRoot == null) return found;
        var allButtons = panelRoot.GetComponentsInChildren<Button>(true);
        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            string name = btn.gameObject.name ?? "";
            foreach (var matcher in pullButtonNameMatchers)
            {
                if (string.IsNullOrEmpty(matcher)) continue;
                if (name.IndexOf(matcher, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!found.Contains(btn)) found.Add(btn);
                    break;
                }
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
                    if (!_gameObjectActiveBefore.ContainsKey(btn.gameObject))
                        _gameObjectActiveBefore[btn.gameObject] = btn.gameObject.activeSelf;
                    btn.gameObject.SetActive(false);
                    break;

                case HideMode.ButtonComponent:
                    if (!_buttonInteractableBefore.ContainsKey(btn))
                        _buttonInteractableBefore[btn] = btn.interactable;
                    btn.interactable = false;
                    break;

                case HideMode.CanvasGroup:
                    var cg = btn.gameObject.GetComponent<CanvasGroup>();
                    if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
                    if (!_canvasGroupBefore.ContainsKey(cg))
                        _canvas_groupBefore_add(cg, (cg.alpha, cg.interactable, cg.blocksRaycasts));
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                    break;
            }
        }
    }

    // helper for adding to canvasGroup dictionary safely
    void _canvas_groupBefore_add(CanvasGroup cg, (float, bool, bool) v)
    {
        if (!_canvasGroupBefore.ContainsKey(cg)) _canvasGroupBefore[cg] = v;
    }

    void RestorePullButtonsInPanel()
    {
        foreach (var kv in _gameObjectActiveBefore.ToList())
        {
            var go = kv.Key;
            if (go != null)
                go.SetActive(kv.Value);
        }
        _gameObjectActiveBefore.Clear();

        foreach (var kv in _buttonInteractableBefore.ToList())
        {
            var btn = kv.Key;
            if (btn != null)
                btn.interactable = kv.Value;
        }
        _buttonInteractableBefore.Clear();

        foreach (var kv in _canvasGroupBefore.ToList())
        {
            var cg = kv.Key;
            if (cg != null)
            {
                var vals = kv.Value;
                cg.alpha = vals.alpha;
                cg.interactable = vals.interactable;
                cg.blocksRaycasts = vals.blocks;
            }
        }
        _canvasGroupBefore.Clear();
    }

    void DisableGemsCounter()
    {
        if (gemsCounterObject == null) TryAutoFindGemsCounter();
        if (gemsCounterObject == null) return;
        _gemsCounterPrevActive = gemsCounterObject.activeSelf;
        gemsCounterObject.SetActive(false);
        _gemsCounterStored = true;
    }

    void RestoreGemsCounter()
    {
        if (!_gemsCounterStored) return;
        if (gemsCounterObject != null) gemsCounterObject.SetActive(_gemsCounterPrevActive);
        _gemsCounterStored = false;
    }

    void TryAutoFindGemsCounter()
    {
        var allObjs = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in allObjs)
        {
            if (go == null) continue;
            string n = go.name ?? "";
            if (n.IndexOf("gems", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("gem", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (go.hideFlags != HideFlags.None) continue;
                gemsCounterObject = go;
                return;
            }
        }

        var texts = Resources.FindObjectsOfTypeAll<Text>();
        foreach (var t in texts)
        {
            if (t == null) continue;
            if (t.gameObject.hideFlags != HideFlags.None) continue;
            string s = (t.text ?? "");
            if (s.IndexOf("Gems", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                gemsCounterObject = t.gameObject;
                return;
            }
        }
    }

    void SetBigPreviewTexture(Texture tex, Vector2 sourceSize)
    {
        if (bigPreviewRaw == null) return;

        if (tex == null)
        {
            bigPreviewRaw.texture = null;
            bigPreviewRaw.color = new Color(1, 1, 1, 0f);
            var le0 = bigPreviewRaw.GetComponent<LayoutElement>();
            if (le0 != null) { le0.ignoreLayout = true; le0.preferredWidth = 0; le0.preferredHeight = 0; }
            return;
        }

        bigPreviewRaw.texture = tex;
        bigPreviewRaw.color = Color.white;

        float srcW = (sourceSize.x > 0.001f && sourceSize.y > 0.001f) ? sourceSize.x : Mathf.Max(1, tex.width);
        float srcH = (sourceSize.x > 0.001f && sourceSize.y > 0.001f) ? sourceSize.y : Mathf.Max(1, tex.height);

        float containerMaxSide = bigPreviewMaxSize;
        if (_previewContainer != null)
        {
            var cw = Mathf.Max(1f, _previewContainer.rect.width);
            var ch = Mathf.Max(1f, _previewContainer.rect.height);
            containerMaxSide = Mathf.Min(bigPreviewMaxSize, Mathf.Max(cw, ch) * 0.95f);
        }

        float maxSide = Mathf.Max(srcW, srcH);
        float scale = 1f;
        if (maxSide > containerMaxSide) scale = containerMaxSide / maxSide;

        float newW = srcW * scale;
        float newH = srcH * scale;

        var rt = bigPreviewRaw.rectTransform;
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newW);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newH);
        }

        var le = bigPreviewRaw.GetComponent<LayoutElement>();
        if (le == null) le = bigPreviewRaw.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        le.preferredWidth = newW;
        le.preferredHeight = newH;
    }

    // 3D preview helpers (kept)
    void Create3DPreview(CharacterData data)
    {
        Clear3DPreview();

        Vector3 spawnPos;
        Quaternion spawnRot = Quaternion.identity;

        if (results3DParent != null) spawnPos = results3DParent.position;
        else if (previewSpawnPoint != null) spawnPos = previewSpawnPoint.position;
        else { var cam = Camera.main; spawnPos = cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero; }

        if (data.prefab3D == null) return;

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

        if (currentPreviewInstance != null)
            StartCoroutine(PopIn(currentPreviewInstance.transform, Vector3.one * previewScale, 0.18f));
    }

    void Clear3DPreview()
    {
        if (currentPreviewInstance != null) { Destroy(currentPreviewInstance); currentPreviewInstance = null; }
    }

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

    // Stats formatting (kept)
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

        if (showIdInStats)
        {
            var charId = GetMemberValue("characterId") ?? GetMemberValue("id");
            if (charId != null) sb.AppendLine($"ID: {charId}");
        }

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