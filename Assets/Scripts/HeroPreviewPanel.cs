using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// HeroPreviewPanel
/// - Gerencia o painel modal de preview do herói.
/// - Popula ícone, nome e stats (usa reflexão para montar os stats automaticamente).
/// - Opcionalmente instancia um preview 3D (prefab) dentro de results3DParent.
/// - Expõe um singleton Instance para compatibilidade com chamadas antigas.
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

    [Tooltip("Texto que exibirá os stats (pode ficar em branco se você não quiser).")]
    public Text statsText;

    [Tooltip("Botão fechar do painel (opcional).")]
    public Button closeButton;

    [Header("Preview 3D (opcional)")]
    [Tooltip("Parent onde instanciar o modelo 3D para preview (opcional).")]
    public Transform results3DParent;

    [Tooltip("Ponto de spawn local/world (usado se results3DParent for nulo).")]
    public Transform previewSpawnPoint;

    [Tooltip("Escala aplicada ao preview 3D instanciado.")]
    public float previewScale = 1f;

    [Tooltip("Se true, o painel instancia o prefab3D (se existir) ao abrir.")]
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

    /// Show: abre o painel e popula com os dados fornecidos.
    public void Show(CharacterData data)
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("HeroPreviewPanel.Show: panelRoot não atribuído.");
            return;
        }

        Populate(data);

        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();

        if (enable3DPreview && data != null && data.prefab3D != null)
        {
            Create3DPreview(data);
        }
    }

    /// Hide: fecha o painel e limpa qualquer preview 3D criado
    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        Clear3DPreview();
    }

    /// Populate: preenche os campos do painel com os dados do CharacterData
    void Populate(CharacterData data)
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

        // Stats (formatted, prefer ordered presentation)
        if (statsText != null)
        {
            statsText.text = FormatStats(data);
        }
    }

    /// Cria um preview 3D simples: instancia prefab, parenta em results3DParent (se houver) e aplica scale/rot.
    void Create3DPreview(CharacterData data)
    {
        Clear3DPreview(); // garante limpeza anterior

        Vector3 spawnPos;
        Quaternion spawnRot = Quaternion.identity;

        if (results3DParent != null)
        {
            spawnPos = results3DParent.position;
        }
        else if (previewSpawnPoint != null)
        {
            spawnPos = previewSpawnPoint.position;
        }
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

    // Nicely-formatted stats output.
    // Prefers a fixed / logical order for common fields (rarity, level, hp, atk, def, spd),
    // hides engine/internal fields (hideFlags) and empty description.
    string FormatStats(object data)
    {
        if (data == null) return "";

        // helper to fetch either property or field value (null if not found)
        object GetMemberValue(string name)
        {
            var type = data.GetType();
            // property
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (p != null && p.CanRead)
            {
                try { return p.GetValue(data); } catch { return null; }
            }
            // field
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (f != null)
            {
                try { return f.GetValue(data); } catch { return null; }
            }
            return null;
        }

        var sb = new StringBuilder();

        // Optional: show characterId (if present)
        var charId = GetMemberValue("characterId") ?? GetMemberValue("id");
        if (charId != null)
        {
            sb.AppendLine($"ID: {charId}");
        }

        // Preferred order
        var preferred = new List<string> { "rarity", "level", "hp", "atk", "def", "spd" };

        foreach (var key in preferred)
        {
            var val = GetMemberValue(key);
            if (val == null) continue;

            // skip zero/empty values for some fields? keep as-is for clarity
            string label = key.ToUpper();
            // nicer labels for certain keys
            if (key.Equals("hp", System.StringComparison.OrdinalIgnoreCase)) label = "HP";
            else if (key.Equals("atk", System.StringComparison.OrdinalIgnoreCase)) label = "ATK";
            else if (key.Equals("def", System.StringComparison.OrdinalIgnoreCase)) label = "DEF";
            else if (key.Equals("spd", System.StringComparison.OrdinalIgnoreCase)) label = "SPD";
            else if (key.Equals("level", System.StringComparison.OrdinalIgnoreCase)) label = "Level";
            else if (key.Equals("rarity", System.StringComparison.OrdinalIgnoreCase)) label = "Rarity";

            sb.AppendLine($"{label}: {val}");
        }

        // Description (only if non-empty)
        var desc = GetMemberValue("description") as string;
        if (!string.IsNullOrEmpty(desc))
        {
            sb.AppendLine();
            sb.AppendLine("Description:");
            sb.AppendLine(desc);
        }

        // If we already have useful output, return it
        var outStr = sb.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(outStr))
            return outStr;

        // Fallback: reflection enumerate public primitive-like properties/fields, excluding engine/internal ones
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