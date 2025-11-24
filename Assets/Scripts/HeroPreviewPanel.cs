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
/// - Pode ocultar/mostrar botões de Pull. Suporta lista explícita ou busca por nome.
/// - Três modos de ocultação: GameObject, Button component, CanvasGroup visual hide.
/// - Restaura estados mesmo se o painel for desativado por outro código (OnDisable/OnDestroy).
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

    [Header("Pull buttons hiding (choose one)")]
    [Tooltip("Se preenchido, esses botões serão usados (mais confiável).")]
    public List<Button> pullButtonsExplicit = new List<Button>();

    [Tooltip("Se a lista explícita estiver vazia, o sistema vai buscar por botões filhos do panelRoot que contenham estes termos no nome (case-insensitive).")]
    public string[] pullButtonNameMatchers = new string[] { "PullOnce", "PullTen", "Pull 10", "Pull 1", "PullOnceButton", "PullTenButton" };

    public enum HideMode { GameObject, ButtonComponent, CanvasGroup }
    [Tooltip("Como os botões serão 'escondidos'")]
    public HideMode hideMode = HideMode.GameObject;

    [Tooltip("Se verdadeiro, o painel tentará automaticamente esconder os botões de Pull ao abrir.")]
    public bool hidePullButtonsInPanel = true;

    [Header("Preview 3D (opcional)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewScale = 1f;
    public bool enable3DPreview = false;

    // runtime
    GameObject currentPreviewInstance;

    // state stores for restore
    private readonly Dictionary<GameObject, bool> _gameObjectActiveBefore = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Button, bool> _buttonInteractableBefore = new Dictionary<Button, bool>();
    private readonly Dictionary<CanvasGroup, (float alpha, bool interactable, bool blocks)> _canvasGroupBefore = new Dictionary<CanvasGroup, (float, bool, bool)>();

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

        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
    }

    void OnDestroy()
    {
        // garante restauração se algo foi esquecido
        RestorePullButtonsInPanel();
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        // Se o painel for desativado por outro script/animator, restaura os botões
        RestorePullButtonsInPanel();
    }

    // Backwards-compatible Show overloads
    public void Show(CharacterData data) { Show(data, null, Vector2.zero); }
    public void Show(CharacterData data, Texture previewTexture) { Show(data, previewTexture, Vector2.zero); }

    // Main Show: accepts optional previewTexture and optional sourceSize (to match slot aspect)
    public void Show(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("HeroPreviewPanel.Show: panelRoot not assigned.");
            return;
        }

        Populate(data, previewTexture, sourceSize);

        // hide pull buttons BEFORE showing panel so they don't flash and so we can restore reliably
        if (hidePullButtonsInPanel)
            DisablePullButtonsInPanel();

        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        if (enable3DPreview && data != null && data.prefab3D != null)
            Create3DPreview(data);
    }

    public void Hide()
    {
        // restore buttons BEFORE deactivating the panel so they become visible immediately
        if (hidePullButtonsInPanel)
            RestorePullButtonsInPanel();

        if (panelRoot != null)
            panelRoot.SetActive(false);

        Clear3DPreview();
    }

    void Populate(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (iconImage != null)
        {
            if (data != null && data.sprite != null) { iconImage.sprite = data.sprite; iconImage.color = Color.white; }
            else { iconImage.sprite = null; iconImage.color = new Color(1,1,1,0); }
        }

        if (nameText != null) nameText.text = data?.displayName ?? "(sem nome)";
        if (statsText != null) statsText.text = FormatStats(data);

        if (bigPreviewRaw != null)
        {
            Texture used = previewTexture ?? (data != null && data.sprite != null ? data.sprite.texture : null);
            SetBigPreviewTexture(used, sourceSize);
        }
    }

    // Find target buttons: explicit list first, otherwise search under panelRoot by name matchers.
    private List<Button> FindTargetPullButtons()
    {
        var found = new List<Button>();

        // explicit assigned buttons (only non-null)
        if (pullButtonsExplicit != null && pullButtonsExplicit.Count > 0)
        {
            foreach (var b in pullButtonsExplicit)
                if (b != null && !found.Contains(b)) found.Add(b);
            Debug.Log($"HeroPreviewPanel: using {found.Count} explicit pullButtons from Inspector.");
            return found;
        }

        // fallback: search under panelRoot
        if (panelRoot == null)
        {
            Debug.Log("HeroPreviewPanel: panelRoot is null, cannot search for pull buttons.");
            return found;
        }

        var allButtons = panelRoot.GetComponentsInChildren<Button>(true);
        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            string name = btn.gameObject.name ?? "";
            bool match = false;
            foreach (var matcher in pullButtonNameMatchers)
            {
                if (string.IsNullOrEmpty(matcher)) continue;
                if (name.IndexOf(matcher, System.StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
            }
            if (match && !found.Contains(btn)) found.Add(btn);
        }

        Debug.Log($"HeroPreviewPanel: found {found.Count} pull button(s) by name matching under panelRoot.");
        if (found.Count > 0)
            Debug.Log("Matched buttons: " + string.Join(", ", found.Select(b => b.gameObject.name)));
        return found;
    }

    // Disable according to chosen hideMode; store previous states for restore.
    void DisablePullButtonsInPanel()
    {
        var targets = FindTargetPullButtons();
        if (targets == null || targets.Count == 0)
        {
            Debug.Log("HeroPreviewPanel: no pull buttons found to hide.");
            return;
        }

        // clear previous stores (we will refill)
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
                    Debug.Log($"HeroPreviewPanel: deactivated GameObject '{btn.gameObject.name}'.");
                    break;

                case HideMode.ButtonComponent:
                    if (!_buttonInteractableBefore.ContainsKey(btn))
                        _buttonInteractableBefore[btn] = btn.interactable;
                    btn.interactable = false;
                    Debug.Log($"HeroPreviewPanel: set Button.interactable=false for '{btn.gameObject.name}'.");
                    break;

                case HideMode.CanvasGroup:
                    var cg = btn.gameObject.GetComponent<CanvasGroup>();
                    if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
                    if (!_canvasGroupBefore.ContainsKey(cg))
                        _canvasGroupBefore[cg] = (cg.alpha, cg.interactable, cg.blocksRaycasts);
                    cg.alpha = 0f;
                    cg.interactable = false;
                    cg.blocksRaycasts = false;
                    Debug.Log($"HeroPreviewPanel: hid via CanvasGroup for '{btn.gameObject.name}'.");
                    break;
            }
        }
    }

    // Restore previously saved states
    void RestorePullButtonsInPanel()
    {
        // restore GameObject active states
        foreach (var kv in _gameObjectActiveBefore.ToList())
        {
            var go = kv.Key;
            if (go != null)
            {
                go.SetActive(kv.Value);
                Debug.Log($"HeroPreviewPanel: restored GameObject '{go.name}' active={kv.Value}.");
            }
        }
        _gameObjectActiveBefore.Clear();

        // restore Button.interactable
        foreach (var kv in _buttonInteractableBefore.ToList())
        {
            var btn = kv.Key;
            if (btn != null)
            {
                btn.interactable = kv.Value;
                Debug.Log($"HeroPreviewPanel: restored Button.interactable for '{btn.gameObject.name}' = {kv.Value}.");
            }
        }
        _buttonInteractableBefore.Clear();

        // restore CanvasGroup
        foreach (var kv in _canvasGroupBefore.ToList())
        {
            var cg = kv.Key;
            if (cg != null)
            {
                var vals = kv.Value;
                cg.alpha = vals.alpha;
                cg.interactable = vals.interactable;
                cg.blocksRaycasts = vals.blocks;
                Debug.Log($"HeroPreviewPanel: restored CanvasGroup for '{cg.gameObject.name}'.");
            }
        }
        _canvasGroupBefore.Clear();
    }

    // --- Big preview helpers (aspect / size) ---
    void SetBigPreviewTexture(Texture tex, Vector2 sourceSize)
    {
        if (bigPreviewRaw == null) return;

        if (tex == null)
        {
            bigPreviewRaw.texture = null;
            bigPreviewRaw.color = new Color(1,1,1,0f);
            var le0 = bigPreviewRaw.GetComponent<LayoutElement>();
            if (le0 != null) { le0.ignoreLayout = true; le0.preferredWidth = 0; le0.preferredHeight = 0; }
            return;
        }

        bigPreviewRaw.texture = tex;
        bigPreviewRaw.color = Color.white;

        float srcW = (sourceSize.x > 0.001f && sourceSize.y > 0.001f) ? sourceSize.x : Mathf.Max(1, tex.width);
        float srcH = (sourceSize.x > 0.001f && sourceSize.y > 0.001f) ? sourceSize.y : Mathf.Max(1, tex.height);
        float aspect = srcW / srcH;

        float targetW, targetH;
        if (srcW >= srcH)
        {
            targetW = Mathf.Min(bigPreviewMaxSize, srcW);
            targetH = targetW / aspect;
        }
        else
        {
            targetH = Mathf.Min(bigPreviewMaxSize, srcH);
            targetW = targetH * aspect;
        }

        if (targetW <= 0) targetW = Mathf.Min(bigPreviewMaxSize, tex.width);
        if (targetH <= 0) targetH = Mathf.Min(bigPreviewMaxSize, tex.height);

        var rt = bigPreviewRaw.rectTransform;
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);
        }

        var le = bigPreviewRaw.GetComponent<LayoutElement>();
        if (le == null) le = bigPreviewRaw.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        le.preferredWidth = targetW;
        le.preferredHeight = targetH;
        le.minWidth = -1;
        le.minHeight = -1;
    }

    // --- 3D preview helpers (kept) ---
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