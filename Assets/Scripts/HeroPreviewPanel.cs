using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// HeroPreviewPanel - mostra versão grande do herói (RenderTexture via PreviewRenderer),
/// além do nome, raridade, ícone, level e outros status.
/// Esta versão inclui logs de debug para ajudar a identificar se os dados chegam corretamente.
public class HeroPreviewPanel : MonoBehaviour
{
    public static HeroPreviewPanel Instance { get; private set; }

    [Header("UI (assign in Inspector)")]
    public GameObject panelRoot;      // root do painel (Active/Inactive)
    public RawImage bigPreviewRaw;    // RawImage grande para RenderTexture
    public Image iconImage;
    public Text nameText;
    public Text rarityText;
    public Text statsText;            // campo que exibirá HP/ATK/DEF/SPD/Level etc.
    public Text descriptionText;
    public Button closeButton;

    [Header("Preview settings")]
    public int previewSize = 512;
    public float modelPreviewScale = 1.4f;

    PreviewRenderer.PreviewHandle previewHandle;
    CharacterData currentData;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (panelRoot == null)
        {
            panelRoot = this.gameObject;
            Debug.LogWarning("HeroPreviewPanel: panelRoot não estava atribuído; usando o próprio GameObject como panelRoot.");
        }

        // garante que comece desativado
        if (panelRoot != null) panelRoot.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
    }

    public void Show(CharacterData data)
    {
        if (data == null)
        {
            Debug.LogWarning("HeroPreviewPanel.Show: data is null");
            return;
        }

        currentData = data;

        if (panelRoot == null)
        {
            Debug.LogError("HeroPreviewPanel.Show: panelRoot é null — não é possível abrir o painel.");
            return;
        }

        Debug.Log($"HeroPreviewPanel.Show: abrindo painel para '{data.displayName}' (id='{data.characterId}')");
        panelRoot.SetActive(true);

        // Nome
        if (nameText != null) nameText.text = string.IsNullOrEmpty(data.displayName) ? "(sem nome)" : data.displayName;

        // Raridade (texto + cor)
        if (rarityText != null)
        {
            rarityText.text = data.rarity.ToString();
            rarityText.color = RarityToColor(data.rarity);
        }

        // Ícone
        if (iconImage != null)
        {
            if (data.sprite != null) { iconImage.sprite = data.sprite; iconImage.color = Color.white; }
            else { iconImage.sprite = null; iconImage.color = new Color(1,1,1,0); }
        }

        // Stats
        if (statsText != null)
            statsText.text = BuildStatsText(data);
        else
            Debug.LogWarning("HeroPreviewPanel.Show: statsText não atribuído no Inspector — não será mostrado o texto de status.");

        // Descrição
        if (descriptionText != null)
            descriptionText.text = string.IsNullOrEmpty(data.description) ? "" : data.description;

        // preview grande via PreviewRenderer
        if (previewHandle != null)
        {
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }

        if (PreviewRenderer.Instance != null && data.prefab3D != null && bigPreviewRaw != null)
        {
            previewHandle = PreviewRenderer.Instance.CreatePreview(data.prefab3D, bigPreviewRaw, previewSize, modelPreviewScale);
        }
        else if (bigPreviewRaw != null)
        {
            bigPreviewRaw.texture = null;
            bigPreviewRaw.color = new Color(1,1,1,0.2f);
        }

        // debug: imprime todos os campos relevantes no Console para inspecionar valores
        Debug.Log(BuildDebugLog(data));
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        if (previewHandle != null)
        {
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }
        currentData = null;
    }

    string BuildStatsText(CharacterData data)
    {
        if (data == null) return "";

        var sb = new StringBuilder();
        sb.AppendLine($"Nível: {data.level}");
        sb.AppendLine($"Raridade: {data.rarity}");
        sb.AppendLine($"HP: {data.hp}");
        sb.AppendLine($"ATK: {data.atk}");
        sb.AppendLine($"DEF: {data.def}");
        sb.AppendLine($"SPD: {data.spd}");
        return sb.ToString();
    }

    string BuildDebugLog(CharacterData d)
    {
        if (d == null) return "CharacterData: null";
        var sb = new StringBuilder();
        sb.AppendLine("CharacterData dump:");
        sb.AppendLine($"  id: {d.characterId}");
        sb.AppendLine($"  name: {d.displayName}");
        sb.AppendLine($"  rarity: {d.rarity} ({(int)d.rarity})");
        sb.AppendLine($"  level: {d.level}");
        sb.AppendLine($"  hp: {d.hp}");
        sb.AppendLine($"  atk: {d.atk}");
        sb.AppendLine($"  def: {d.def}");
        sb.AppendLine($"  spd: {d.spd}");
        sb.AppendLine($"  prefab3D: {(d.prefab3D != null ? d.prefab3D.name : "null")}");
        sb.AppendLine($"  sprite: {(d.sprite != null ? d.sprite.name : "null")}");
        return sb.ToString();
    }

    Color RarityToColor(Rarity r)
    {
        switch (r)
        {
            case Rarity.Common:    return new Color(0.9f, 0.9f, 0.9f);
            case Rarity.Uncommon:  return new Color(0.3f, 0.8f, 0.3f);
            case Rarity.Rare:      return new Color(0.25f, 0.5f, 1f);
            case Rarity.Epic:      return new Color(0.6f, 0.2f, 0.9f);
            case Rarity.Legendary: return new Color(1f, 0.7f, 0.15f);
            default: return Color.white;
        }
    }

    void OnDestroy()
    {
        if (previewHandle != null)
        {
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }
        if (Instance == this) Instance = null;
    }
}