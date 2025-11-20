using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

[RequireComponent(typeof(GraphicRaycaster))]
public class UIRaycastDebugger : MonoBehaviour
{
    public GraphicRaycaster raycaster; // arraste o GraphicRaycaster do Canvas aqui
    public Camera uiCamera; // se Canvas for Screen Space - Camera ou World Space, arraste a câmera; se Overlay deixe vazio

    void Awake()
    {
        if (raycaster == null)
            raycaster = GetComponent<GraphicRaycaster>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // importa: clique no Game View (foco no Game), não na Scene
            TestAtScreenPosition(Input.mousePosition);
        }
    }

    public void TestAtScreenPosition(Vector2 screenPos)
    {
        // Log info contextual
        Debug.Log($"--- UIRaycastDebugger Test start ---");
        Debug.Log($"EventSystem.current: {(EventSystem.current != null ? EventSystem.current.name : "NULL")}");
        if (EventSystem.current != null)
            Debug.Log($" - IsPointerOverGameObject(): {EventSystem.current.IsPointerOverGameObject()}");
        Debug.Log($"Assigned raycaster: {(raycaster != null ? raycaster.name : "NULL")}");
        if (raycaster != null)
        {
            var canvas = raycaster.GetComponent<Canvas>();
            if (canvas != null)
            {
                Debug.Log($" - Canvas.name: {canvas.name}, renderMode: {canvas.renderMode}, renderCamera: {(canvas.renderMode == RenderMode.ScreenSpaceCamera ? (canvas.worldCamera!=null?canvas.worldCamera.name:"NULL") : "N/A")}");
            }
        }
        Debug.Log($"uiCamera field: {(uiCamera != null ? uiCamera.name : "NULL (using Camera.main)")}");

        // 1) Raycast UI
        PointerEventData ped = new PointerEventData(EventSystem.current);
        ped.position = screenPos;

        List<RaycastResult> results = new List<RaycastResult>();
        if (raycaster != null)
            raycaster.Raycast(ped, results);
        else
            Debug.LogWarning("UIRaycastDebugger: raycaster is NULL. Assign the Canvas GraphicRaycaster to this component.");

        Debug.Log($"UIRaycastDebugger: ScreenPos={screenPos} -> UI hits: {results.Count}");
        foreach (var r in results)
        {
            Debug.Log($" - UI hit: {r.gameObject.name} (module {r.module}), index {r.index}, screen pos {r.screenPosition}, world pos {r.worldPosition}");
        }

        // 2) Raycast Physics (3D) from camera to world
        Camera cam = uiCamera != null ? uiCamera : Camera.main;
        if (cam == null)
        {
            Debug.Log("UIRaycastDebugger: no Camera.main and uiCamera is null -> cannot do physics raycast.");
            Debug.Log("--- UIRaycastDebugger Test end ---");
            return;
        }

        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f))
        {
            Debug.Log($"UIRaycastDebugger: Physics hit: {hit.collider.gameObject.name} at distance {hit.distance}");
        }
        else
        {
            Debug.Log("UIRaycastDebugger: Physics hit: NONE");
        }

        Debug.Log($"--- UIRaycastDebugger Test end ---");
    }
}