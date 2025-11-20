using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// ResultSlot finalizado:
/// - tenta encontrar ViewButton se não estiver atribuído,
/// - adiciona listener em runtime sem remover listeners já configurados no prefab,
/// - OnViewClicked agora é público para poder ser ligado pelo Inspector se preferir.
[RequireComponent(typeof(RectTransform))]
public class ResultSlot : MonoBehaviour
{
    [Header("UI refs (assign in prefab)")]
    public Image icon;
    public Text nameText;
    public RawImage previewRaw; // miniatura pequena dentro do slot (opcional)
    public Button viewButton;   // botão "ViewButton" atribuído no prefab

    [Header("Preview settings (mini)")]
    public int previewSize = 256;
    public float modelPreviewScale = 1f;

    PreviewRenderer.PreviewHandle previewHandle;
    CharacterData currentData;

    bool suppressReleaseOnDisable = false;
    bool listenerAdded = false;

    void Awake()
    {
        if (previewRaw != null)
            previewRaw.rectTransform.localScale = Vector3.one;

        // tenta encontrar ViewButton automaticamente se não estiver atribuído
        if (viewButton == null)
        {
            var t = transform.Find("ViewButton");
            if (t != null)
                viewButton = t.GetComponent<Button>();
            else
                viewButton = GetComponentInChildren<Button>(true);

            if (viewButton != null)
                Debug.Log($"ResultSlot ({name}): viewButton atribuído automaticamente: {viewButton.name}");
            else
                Debug.LogWarning($"ResultSlot ({name}): viewButton não encontrado. Arraste o ViewButton no Inspector do prefab.");
        }

        // adiciona listener sem remover listeners existentes (preserva comportamento do prefab)
        if (viewButton != null && !listenerAdded)
        {
            viewButton.onClick.AddListener(OnViewClicked);
            listenerAdded = true;
        }
    }

    public void SetData(CharacterData data)
    {
        // libera preview anterior ao trocar de dados
        if (previewHandle != null)
        {
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }

        currentData = data;

        if (nameText != null) nameText.text = data?.displayName ?? "(empty)";

        if (icon != null)
        {
            if (data != null && data.sprite != null)
            {
                icon.sprite = data.sprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
            }
            else
            {
                icon.sprite = null;
                icon.color = new Color(1,1,1,0);
            }
        }

        if (data != null && data.prefab3D != null && previewRaw != null && PreviewRenderer.Instance != null)
        {
            previewHandle = PreviewRenderer.Instance.CreatePreview(data.prefab3D, previewRaw, previewSize, modelPreviewScale);
        }
        else if (previewRaw != null)
        {
            previewRaw.texture = null;
            previewRaw.color = new Color(1,1,1,0.2f);
        }
    }

    // Agora público — pode ser ligado manualmente em Button.OnClick no Inspector
    public void OnViewClicked()
    {
        Debug.Log($"ResultSlot ({name}): OnViewClicked chamado. currentData is {(currentData == null ? "NULL" : currentData.displayName)}");

        if (currentData == null)
        {
            Debug.LogWarning($"ResultSlot ({name}): currentData é null — verifique se SetData foi chamado antes do clique.");
            return;
        }

        // suprime liberação caso o slot seja momentaneamente desativado ao abrir o painel
        suppressReleaseOnDisable = true;

        if (HeroPreviewPanel.Instance != null)
        {
            HeroPreviewPanel.Instance.Show(currentData);
            Debug.Log($"ResultSlot ({name}): abriu HeroPreviewPanel para '{currentData.displayName}'.");
        }
        else
        {
            Debug.LogError("ResultSlot: HeroPreviewPanel.Instance não encontrado na cena. Adicione o HeroPreviewPanel e configure-o no Canvas.");
        }

        StartCoroutine(ResetSuppressNextFrame());
    }

    IEnumerator ResetSuppressNextFrame()
    {
        yield return null;
        suppressReleaseOnDisable = false;
    }

    void OnDisable()
    {
        if (suppressReleaseOnDisable)
        {
            Debug.Log($"ResultSlot ({name}): OnDisable chamado, mas supressão ativa — não liberando preview.");
            return;
        }

        if (previewHandle != null)
        {
            Debug.Log($"ResultSlot ({name}): OnDisable liberando preview.");
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }
    }

    void OnDestroy()
    {
        if (previewHandle != null)
        {
            Debug.Log($"ResultSlot ({name}): OnDestroy liberando preview.");
            PreviewRenderer.Instance?.ReleasePreview(previewHandle);
            previewHandle = null;
        }
    }
}