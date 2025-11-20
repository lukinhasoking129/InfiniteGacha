using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// GachaUI - Gerencia a interface do gacha: botões de pull, exibição de resultados UI (grid/scroll)
/// e previews 3D (instancia modelos em frente à câmera / previewSpawnPoint).
public class GachaUI : MonoBehaviour
{
    [Header("Managers & Data")]
    public GachaSystem gacha;           // seu sistema de gacha (deve expor Pull(int) -> List<CharacterData>)
    public CurrencyManager currency;   // gerenciador de gems (deve expor gems e Spend(int) -> bool)
    public InventoryManager inventory; // opcional: adiciona resultados ao inventário

    [Header("Costs")]
    public int costPerPull = 100;

    [Header("UI References")]
    public Text gemsText;               // mostra número de gems
    public Transform resultsContainer;  // Content do Scroll View (GridLayoutGroup)
    public GameObject resultSlotPrefab; // prefab UI do slot (com ResultSlot)
    public ScrollRect resultsScrollRect; // ScrollRect para ajustar a rolagem

    [Header("3D Preview")]
    public Transform results3DParent;   // onde parentar os previews 3D (pode ficar vazio)
    public Transform previewSpawnPoint; // ponto base (usado para calcular centro e direção)
    public float previewSpacing = 1.2f;
    public float previewScale = 1f;
    public float previewLift = 0.5f;

    // estado runtime
    private GameObject currentPreviewInstance;

    void Start()
    {
        if (gacha == null) Debug.LogWarning("GachaUI: gacha não atribuído.");
        if (currency == null) Debug.LogWarning("GachaUI: currency não atribuído.");
        if (resultsContainer == null) Debug.LogWarning("GachaUI: resultsContainer não atribuído.");
        if (resultSlotPrefab == null) Debug.LogWarning("GachaUI: resultSlotPrefab não atribuído.");
        if (resultsScrollRect == null) Debug.LogWarning("GachaUI: resultsScrollRect não atribuído.");
    }

    void Update()
    {
        if (gemsText != null && currency != null)
            gemsText.text = $"Gems: {currency.gems}";
    }

    // ------------------------
    // Botões públicos (OnClick)
    // ------------------------
    public void OnPullOnce()
    {
        if (currency == null || gacha == null) return;

        if (!currency.Spend(costPerPull))
        {
            Debug.Log("GachaUI: gems insuficientes para 1 pull.");
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
            Debug.Log("GachaUI: gems insuficientes para 10 pulls.");
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
            Debug.Log("GachaUI: Nenhum resultado retornado.");
            return;
        }

        // adiciona (append) resultados ao UI sem limpar os anteriores
        AppendResultsUI(results);

        // exibe previews 3D para esta leva
        ShowResults3D(results);

        // adiciona ao inventário, se houver
        if (inventory != null)
        {
            foreach (var r in results)
                inventory.Add(r);
        }
    }

    // ------------------------
    // UI: adiciona slots ao container (append)
    // ------------------------
    void AppendResultsUI(List<CharacterData> results)
    {
        if (resultsContainer == null || resultSlotPrefab == null)
        {
            Debug.LogWarning("AppendResultsUI: resultsContainer ou resultSlotPrefab não atribuídos.");
            return;
        }

        for (int i = 0; i < results.Count; i++)
        {
            var data = results[i];

            var go = Instantiate(resultSlotPrefab);
            // preserva local transforms do prefab ao setar parent
            go.transform.SetParent(resultsContainer, false);
            go.transform.localScale = Vector3.one;

            // tenta usar ResultSlot (recomendado)
            var slotComp = go.GetComponent<ResultSlot>();
            if (slotComp != null)
            {
                slotComp.SetData(data);
            }
            else
            {
                // fallback: preenche Image/Text diretamente
                var img = go.GetComponentInChildren<Image>();
                var txt = go.GetComponentInChildren<Text>();
                if (txt != null) txt.text = data?.displayName ?? "(sem nome)";
                if (img != null && data?.sprite != null) img.sprite = data.sprite;
            }

            // se houver botão no slot, liga Show3DPreview ao clique
            var btn = go.GetComponentInChildren<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                CharacterData captured = data;
                btn.onClick.AddListener(() => Show3DPreview(captured));
            }
        }

        // força rebuild do layout do container (GridLayoutGroup / ContentSizeFitter)
        var rect = resultsContainer as RectTransform;
        if (rect != null)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        // rola o scroll para baixo para mostrar itens novos (se houver ScrollRect)
        if (resultsScrollRect != null)
            StartCoroutine(ScrollToBottomNextFrame());
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        // espera um frame para o layout aplicar
        yield return null;
        if (resultsScrollRect == null) yield break;
        // move para o fim: verticalNormalizedPosition = 0f (0 = bottom quando content anchored top)
        resultsScrollRect.verticalNormalizedPosition = 0f;
        // small extra frame to ensure visibility
        yield return null;
    }

    // ------------------------
    // 3D previews: instância modelos em world space e parenta preservando posição
    // ------------------------
    void ShowResults3D(List<CharacterData> results)
    {
        if (results == null || results.Count == 0) return;

        Camera cam = Camera.main;
        if (cam == null)
            Debug.LogWarning("ShowResults3D: Camera.main é nula.");

        if (results3DParent != null && results3DParent.localScale == Vector3.zero)
            Debug.LogWarning("ShowResults3D: results3DParent com localScale=(0,0,0). Ajuste para (1,1,1).");

        // limpa previews antigos (somente filhos do parent)
        if (results3DParent != null)
        {
            for (int i = results3DParent.childCount - 1; i >= 0; i--)
                Destroy(results3DParent.GetChild(i).gameObject);
        }

        // calcula centro/direções para distribuir os previews
        Vector3 center;
        Vector3 right;
        Vector3 up;

        if (previewSpawnPoint != null)
        {
            center = previewSpawnPoint.position;
            right = previewSpawnPoint.right;
            up = previewSpawnPoint.up;
        }
        else if (cam != null)
        {
            center = cam.transform.position + cam.transform.forward * 3f;
            right = cam.transform.right;
            up = Vector3.up;
        }
        else
        {
            center = Vector3.zero;
            right = Vector3.right;
            up = Vector3.up;
        }

        if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
        if (up.sqrMagnitude < 0.0001f) up = Vector3.up;

        int count = results.Count;
        float totalWidth = (count - 1) * previewSpacing;
        float startOffset = -totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var r = results[i];
            if (r == null || r.prefab3D == null)
            {
                Debug.Log($"ShowResults3D: resultado {i} sem prefab3D (ignorado).");
                continue;
            }

            Vector3 spawnPos = center + right * (startOffset + i * previewSpacing) + up * previewLift;
            Quaternion spawnRot = Quaternion.identity;

            // instantiate em world space - garante posição absoluta independente do parent
            var instance = Instantiate(r.prefab3D, spawnPos, spawnRot);

            // parenta preservando world position (true)
            if (results3DParent != null)
                instance.transform.SetParent(results3DParent, true);

            // corrige escala para previewScale (não herdar escala do parent)
            instance.transform.localScale = Vector3.one * previewScale;

            // faz olhar para a câmera (apenas eixo Y)
            if (cam != null)
            {
                Vector3 dir = cam.transform.position - instance.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) instance.transform.rotation = Quaternion.LookRotation(dir);
            }

            // desativa colisores para evitar interações
            var cols = instance.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.enabled = false;

            // anima pop-in
            instance.transform.localScale = Vector3.zero;
            StartCoroutine(PopIn(instance.transform, Vector3.one * previewScale, 0.12f + i * 0.03f));
        }

        Debug.Log($"ShowResults3D: instanciados {count} previews com center={center}, spacing={previewSpacing}");
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

    // ------------------------
    // Preview único (quando clica no slot)
    // ------------------------
    public void Show3DPreview(CharacterData data)
    {
        // fallback: se não tiver previewSpawnPoint, usa Camera.main em frente
        Transform spawn = previewSpawnPoint;
        Camera cam = Camera.main;

        Vector3 spawnPos;
        Quaternion spawnRot = Quaternion.identity;

        if (spawn == null)
        {
            if (cam == null)
            {
                Debug.LogWarning("Show3DPreview: previewSpawnPoint e Camera.main nulos, não é possível mostrar preview.");
                return;
            }
            spawnPos = cam.transform.position + cam.transform.forward * 3f;
        }
        else
        {
            spawnPos = spawn.position;
        }

        if (data == null || data.prefab3D == null)
        {
            Debug.Log("Show3DPreview: dados nulos ou prefab3D ausente.");
            return;
        }

        // limpa preview atual
        if (currentPreviewInstance != null) Destroy(currentPreviewInstance);

        // instancia (em world space)
        currentPreviewInstance = (results3DParent != null)
            ? Instantiate(data.prefab3D, spawnPos, spawnRot, results3DParent)
            : Instantiate(data.prefab3D, spawnPos, spawnRot);

        currentPreviewInstance.transform.localScale = Vector3.one * previewScale;

        // desativa colisores
        var cols = currentPreviewInstance.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;

        // look to camera
        if (cam != null)
        {
            Vector3 look = cam.transform.position - currentPreviewInstance.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f) currentPreviewInstance.transform.rotation = Quaternion.LookRotation(look);
        }

        // pop-in
        currentPreviewInstance.transform.localScale = Vector3.zero;
        StartCoroutine(PopIn(currentPreviewInstance.transform, Vector3.one * previewScale, 0.2f));
    }

    // ------------------------
    // Clear / cleanup
    // ------------------------
    public void Clear3DPreview()
    {
        if (currentPreviewInstance != null) Destroy(currentPreviewInstance);

        if (results3DParent != null)
        {
            for (int i = results3DParent.childCount - 1; i >= 0; i--)
                Destroy(results3DParent.GetChild(i).gameObject);
        }
    }
}