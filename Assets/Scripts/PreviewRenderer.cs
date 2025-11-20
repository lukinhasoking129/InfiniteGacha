using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// PreviewRenderer: cria uma câmera dedicada + RenderTexture + instancia o prefab3D em layer isolada
/// agora com opção de adicionar um rotator suave ao preview para girar horizontalmente.
public class PreviewRenderer : MonoBehaviour
{
    public static PreviewRenderer Instance { get; private set; }

    [Header("Layer")]
    [Tooltip("Nome da layer que será usada para isolar os objetos de preview. Crie essa layer em Edit > Project Settings > Tags and Layers.")]
    public string previewLayerName = "PreviewUI";

    [Header("World placement")]
    [Tooltip("Distância (em unidades mundo) entre previews posicionados para evitar que câmeras capturem outros modelos.")]
    public float worldSpacing = 8f;

    [Header("Camera framing")]
    [Tooltip("Fator vertical: 0 = olhar exatamente para o centro dos bounds; >0 eleva o look target.")]
    public float verticalOffsetFactor = 0f;

    [Header("Rotation")]
    [Tooltip("Velocidade de rotação do modelo de preview em graus por segundo. 0 = sem rotação.")]
    public float rotationSpeed = 15f;

    // lista de handles ativos (permite posicionar cada preview em offset único)
    readonly List<PreviewHandle> activeHandles = new List<PreviewHandle>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public class PreviewHandle
    {
        public RenderTexture rt;
        public Camera cam;
        public GameObject instance;
        public RawImage targetRaw;
        public Vector3 worldOffset;
    }

    public PreviewHandle CreatePreview(GameObject prefab3D, RawImage targetRaw, int size = 256, float modelScale = 1f, Color? bg = null)
    {
        if (prefab3D == null || targetRaw == null)
        {
            Debug.LogWarning("PreviewRenderer.CreatePreview: prefab3D ou targetRaw é null.");
            return null;
        }

        int previewLayer = LayerMask.NameToLayer(previewLayerName);
        if (previewLayer == -1)
        {
            Debug.LogWarning($"PreviewRenderer: layer '{previewLayerName}' não existe. Crie-a em Edit > Project Settings > Tags and Layers.");
            return null;
        }

        // Criar RenderTexture
        var rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32);
        rt.name = $"RT_preview_{prefab3D.name}";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;

        // Criar camera dedicada como filho deste PreviewRenderer (organiza a cena)
        GameObject camGO = new GameObject($"PreviewCam_{prefab3D.name}_{activeHandles.Count}");
        camGO.transform.SetParent(transform, false);
        camGO.hideFlags = HideFlags.HideInHierarchy;
        Camera cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg ?? new Color(0,0,0,0);
        cam.targetTexture = rt;
        cam.cullingMask = 1 << previewLayer;
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.orthographic = false;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;

        // Instanciar prefab (sem parent para evitar heranças)
        GameObject inst = Instantiate(prefab3D);
        inst.name = $"preview_{prefab3D.name}_{activeHandles.Count}";
        inst.transform.SetParent(null, true);

        // Aplica layer recursivamente
        SetLayerRecursively(inst, previewLayer);

        // Remove scripts se necessário (placeholder)
        RemoveRuntimeScripts(inst);

        // Desativa sombras
        var renderers = inst.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Calcula bounds e reposiciona inst para centralizar
        Bounds b = CalculateBounds(inst);

        // calcula um offset único em world space baseado na quantidade de previews ativos
        Vector3 offset = Vector3.right * (activeHandles.Count * worldSpacing);

        // move a instância para que seu centro local fique em offset (não na origem comum)
        inst.transform.position = offset - b.center;

        // Ajusta câmera para enquadrar o objeto na posição correta (leva em conta offset)
        FitCameraToBounds(cam, b, modelScale, offset);

        // Adiciona rotator se rotationSpeed != 0
        if (Mathf.Abs(rotationSpeed) > 0.0001f)
        {
            var rot = inst.AddComponent<PreviewRotator>();
            rot.speed = rotationSpeed;
            rot.axis = Vector3.up; // rotação horizontal
        }

        // Aplica RenderTexture ao RawImage do slot
        targetRaw.texture = rt;
        targetRaw.color = Color.white;
        targetRaw.material = null;

        var handle = new PreviewHandle { rt = rt, cam = cam, instance = inst, targetRaw = targetRaw, worldOffset = offset };

        activeHandles.Add(handle);

        Debug.Log($"PreviewRenderer: criado preview '{inst.name}' com offset {offset} (count={activeHandles.Count}).");

        return handle;
    }

    public void ReleasePreview(PreviewHandle handle)
    {
        if (handle == null) return;

        if (handle.targetRaw != null && handle.targetRaw.texture == handle.rt)
            handle.targetRaw.texture = null;

        if (handle.cam != null) Destroy(handle.cam.gameObject);
        if (handle.instance != null) Destroy(handle.instance);
        if (handle.rt != null)
        {
            handle.rt.Release();
            Destroy(handle.rt);
        }

        if (activeHandles.Contains(handle))
            activeHandles.Remove(handle);

        Debug.Log($"PreviewRenderer: liberado preview, remaining handles: {activeHandles.Count}");
    }

    Bounds CalculateBounds(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        Bounds total = new Bounds();
        bool inited = false;
        foreach (var r in rends)
        {
            if (!inited)
            {
                total = r.bounds;
                inited = true;
            }
            else total.Encapsulate(r.bounds);
        }
        if (!inited) return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 localCenter = go.transform.InverseTransformPoint(total.center);
        return new Bounds(localCenter, total.size);
    }

    void FitCameraToBounds(Camera cam, Bounds b, float modelScale, Vector3 worldOffset)
    {
        if (cam == null) return;

        if (b.size.sqrMagnitude < 0.0001f)
        {
            cam.transform.position = worldOffset + new Vector3(0, 0, -2f);
            cam.transform.rotation = Quaternion.identity;
            return;
        }

        Vector3 centerLocal = -b.center;
        Vector3 centerWorld = worldOffset + centerLocal;
        float maxSize = Mathf.Max(b.size.x, b.size.y, b.size.z) * Mathf.Max(0.01f, modelScale);
        float fov = cam.fieldOfView;
        float distance = maxSize / (2f * Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f));
        distance *= 1.2f;

        Vector3 lookTarget = centerWorld + Vector3.up * (b.size.y * verticalOffsetFactor);

        cam.transform.position = lookTarget + Vector3.back * distance;
        cam.transform.LookAt(lookTarget);
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform t in obj.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    void RemoveRuntimeScripts(GameObject go)
    {
        // Intencionalmente vazio por segurança.
    }
}