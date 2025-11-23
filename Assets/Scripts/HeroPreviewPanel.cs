using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// HeroPreviewPanel
/// - Gerencia o painel modal de preview do herói.
/// - Popula ícone, nome, stats e um preview grande (RawImage).
/// - Mantém a proporção (aspect ratio) da textura exibida no bigPreviewRaw,
///   preferindo a proporção do preview do slot quando fornecida.
public class HeroPreviewPanel : MonoBehaviour
{
    // Singleton (compatibilidade)
    public static HeroPreviewPanel Instance { get; private set; }

    [Header("UI refs (arraste no Inspector)")]
    [Tooltip("O GameObject raiz do painel (modal).")]
    public GameObject panelRoot;

    [Tooltip("Image onde será mostrado o sprite do herói.")]
    public Image iconImage;

    [Tooltip("Texto do nome do herói.")]
    public Text nameText;

    [Tooltip("Texto que exibirá os stats.")]
    public Text statsText;

    [Tooltip("RawImage grande dentro do painel para mostrar preview (arraste aqui).")]
    public RawImage bigPreviewRaw;

    [Tooltip("Tamanho máximo (px) do lado maior do bigPreviewRaw.")]
    public int bigPreviewMaxSize = 320;

    [Tooltip("Botão fechar do painel (opcional).")]
    public Button closeButton;

    [Header("Preview 3D (opcional)")]
    public Transform results3DParent;
    public Transform previewSpawnPoint;
    public float previewScale = 1f;
    public bool enable3DPreview = false;

    // runtime
    GameObject currentPreviewInstance;

    void Reset()
    {
        if (panelRoot == null) panelRoot = this.gameObject;
    }

    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
        }
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
        if (Instance == this) Instance = null;
    }

    // Backwards-compatible Show
    public void Show(CharacterData data)
    {
        Show(data, null, Vector2.zero);
    }

    // Show accepting optional preview texture and optional sourceSize (width, height) to preserve same aspect.
    // sourceSize: if non-zero, its aspect ratio is preferred (useful to match the ResultSlot preview rect).
    public void Show(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("HeroPreviewPanel.Show: panelRoot não atribuído.");
            return;
        }

        Populate(data, previewTexture, sourceSize);

        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        if (enable3DPreview && data != null && data.prefab3D != null)
        {
            Create3DPreview(data);
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        Clear3DPreview();
    }

    void Populate(CharacterData data, Texture previewTexture, Vector2 sourceSize)
    {
        // Icon
        if (iconImage != null)
        {
            if (data != null && data.sprite != null)
            {
                iconImage.sprite = data.sprite;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.color = new Color(1,1,1,0);
            }
        }

        // Name
        if (nameText != null)
            nameText.text = data?.displayName ?? "(sem nome)";

        // Stats
        if (statsText != null)
            statsText.text = FormatStats(data);

        // Big preview: set texture and respect aspect ratio.
        if (bigPreviewRaw != null)
        {
            Texture used = null;
            if (previewTexture != null)
                used = previewTexture;
            else if (data != null && data.sprite != null)
                used = data.sprite.texture;

            SetBigPreviewTexture(used, sourceSize);
        }
    }

    /// Sets the texture on bigPreviewRaw and resizes the RectTransform preserving aspect ratio.
    /// If sourceSize has positive components, those are used to compute the aspect (this matches the slot rect).
    void SetBigPreviewTexture(Texture tex, Vector2 sourceSize)
    {
        if (bigPreviewRaw == null) return;

        if (tex == null)
        {
            // hide the raw image if no texture
            bigPreviewRaw.texture = null;
            bigPreviewRaw.color = new Color(1,1,1,0f);
            var le0 = bigPreviewRaw.GetComponent<LayoutElement>();
            if (le0 != null)
            {
                le0.ignoreLayout = true;
                le0.preferredWidth = 0;
                le0.preferredHeight = 0;
            }
            return;
        }

        bigPreviewRaw.texture = tex;
        bigPreviewRaw.color = Color.white;

        // Determine aspect using sourceSize if provided, otherwise use texture pixel size
        float srcW = 0f, srcH = 0f;
        if (sourceSize.x > 0.001f && sourceSize.y > 0.001f)
        {
            srcW = sourceSize.x;
            srcH = sourceSize.y;
        }
        else
        {
            srcW = Mathf.Max(1, tex.width);
            srcH = Mathf.Max(1, tex.height);
        }

        float aspect = srcW / srcH;

        // Compute new size so that the largest side equals bigPreviewMaxSize (but scale up if smaller? keep <= max)
        float targetW, targetH;
        if (srcW >= srcH)
        {
            targetW = Mathf.Min(bigPreviewMaxSize, srcW);
            // scale factor based on srcW vs max - if srcW > max, shrink; if srcW <= max, keep srcW but we may want to scale up a bit — here we limit to max
            float scale = (srcW > bigPreviewMaxSize) ? (bigPreviewMaxSize / srcW) : (bigPreviewMaxSize / srcW < 1f ? 1f : 1f);
            targetW = srcW * scale;
            targetH = targetW / aspect;
        }
        else
        {
            targetH = Mathf.Min(bigPreviewMaxSize, srcH);
            float scale = (srcH > bigPreviewMaxSize) ? (bigPreviewMaxSize / srcH) : (bigPreviewMaxSize / srcH < 1f ? 1f : 1f);
            targetH = srcH * scale;
            targetW = targetH * aspect;
        }

        // Fallback ensure positive
        if (targetW <= 0) targetW = Mathf.Min(bigPreviewMaxSize, tex.width);
        if (targetH <= 0) targetH = Mathf.Min(bigPreviewMaxSize, tex.height);

        // apply size to RectTransform
        var rt = bigPreviewRaw.rectTransform;
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetW);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);
        }

        // ensure LayoutElement informs layout system
        var le = bigPreviewRaw.GetComponent<LayoutElement>();
        if (le == null) le = bigPreviewRaw.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        le.preferredWidth = targetW;
        le.preferredHeight = targetH;
        le.minWidth = -1;
        le.minHeight = -1;
    }

    // --- 3D preview helpers (inspired by previous implementation) ---
    void Create3DPreview(CharacterData data)
    {
        Clear3DPreview();

        Vector3 spawnPos;
        Quaternion spawnRot = Quaternion.identity;

        if (results3DParent != null)
            spawnPos = results3DParent.position;
        else if (previewSpawnPoint != null)
            spawnPos = previewSpawnPoint.position;
        else
        {
            var cam = Camera.main;
            spawnPos = cam != null ? cam.transform.position + cam.transform.forward * 3f : Vector3.zero;
        }

        if (data.prefab3D == null)
            return;

        currentPreviewInstance = Instantiate(data.prefab3D, spawnPos, spawnRot);

        if (results3DParent != null)
            currentPreviewInstance.transform.SetParent(results3DParent, true);

        currentPreviewInstance.transform.localScale = Vector3.one * previewScale;

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector3 dir = mainCam.transform.position - currentPreviewInstance.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                currentPreviewInstance.transform.rotation = Quaternion.LookRotation(dir);
        }

        var cols = currentPreviewInstance.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;

        if (currentPreviewInstance != null)
            StartCoroutine(PopIn(currentPreviewInstance.transform, Vector3.one * previewScale, 0.18f));
    }

    void Clear3DPreview()
    {
        if (currentPreviewInstance != null)
        {
            Destroy(currentPreviewInstance);
            currentPreviewInstance = null;
        }
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

    // Nicely-formatted stats output as before
    string FormatStats(object data)
    {
        if (data == null) return "";

        object GetMemberValue(string name)
        {
            var type = data.GetType();
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p != null && p.CanRead)
            {
                try { return p.GetValue(data); } catch { return null; }
            }
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (f != null)
            {
                try { return f.GetValue(data); } catch { return null; }
            }
            return null;
        }

        var sb = new StringBuilder();

        var charId = GetMemberValue("characterId") ?? GetMemberValue("id");
        if (charId != null)
            sb.AppendLine($"ID: {charId}");

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
        if (!string.IsNullOrEmpty(desc))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(desc);
        }

        var outStr = sb.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(outStr))
            return outStr;

        // Fallback generic listing
        var typeFallback = data.GetType();
        var exclude = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "hideFlags", "gameObject", "transform", "sprite", "prefab3D", "displayName", "name"
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
            if (pt.IsPrimitive || pt.IsEnum || pt == typeof(string) || pt.IsValueType)
                sb2.AppendLine($"{p.Name}: {val}");
        }

        var fields = typeFallback.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var f in fields)
        {
            if (exclude.Contains(f.Name)) continue;
            object val = null;
            try { val = f.GetValue(data); } catch { continue; }
            if (val == null) continue;
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft.IsValueType)
                sb2.AppendLine($"{f.Name}: {val}");
        }

        outStr = sb2.ToString().TrimEnd();
        return string.IsNullOrEmpty(outStr) ? "(no stats)" : outStr;
    }
}