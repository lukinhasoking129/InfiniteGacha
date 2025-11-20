using System.Text;
using UnityEngine;

public class DebugVerifier : MonoBehaviour
{
    [Header("Objetos a verificar (arraste do Inspector)")]
    public GameObject gachaSystemGO; // o GameObject que tem GachaSystem
    public GameObject uiManagerGO;   // GameObject com GachaUI
    public GameObject managersGO;    // GameObject com CurrencyManager / InventoryManager
    public ScriptableObject gachaPoolAsset; // arraste o GachaPool asset (opcional)

    void Start()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DebugVerifier START ===");
        sb.AppendLine($"Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        sb.AppendLine($"Time: {System.DateTime.Now}");

        // GachaSystem
        if (gachaSystemGO == null) sb.AppendLine("GachaSystemGO: NULL");
        else
        {
            var gs = gachaSystemGO.GetComponent<MonoBehaviour>();
            sb.AppendLine($"GachaSystemGO: {gachaSystemGO.name} (component count: {gachaSystemGO.GetComponents<Component>().Length})");
            var comp = gachaSystemGO.GetComponent("GachaSystem");
            sb.AppendLine($" - GachaSystem component present: {(comp!=null)}");
            if (comp != null)
            {
                var poolField = comp.GetType().GetField("pool");
                if (poolField != null)
                {
                    var poolVal = poolField.GetValue(comp) as Object;
                    sb.AppendLine($" - pool assigned: {(poolVal!=null ? poolVal.name : "NULL")}");
                }
            }
        }

        // GachaUI
        if (uiManagerGO == null) sb.AppendLine("UIManagerGO: NULL");
        else
        {
            var gu = uiManagerGO.GetComponent("GachaUI");
            sb.AppendLine($"UIManagerGO: {uiManagerGO.name} (component present: {(gu!=null)})");
            if (gu != null)
            {
                var t = gu.GetType();
                string[] fields = { "gacha", "currency", "inventory", "gemsText", "resultsContainer", "resultSlotPrefab", "results3DParent", "previewSpawnPoint" };
                foreach (var f in fields)
                {
                    var fi = t.GetField(f);
                    if (fi != null)
                    {
                        var val = fi.GetValue(gu) as Object;
                        sb.AppendLine($" - {f}: {(val!=null ? val.name : "NULL")}");
                    }
                }
            }
        }

        // Managers
        if (managersGO == null) sb.AppendLine("ManagersGO: NULL");
        else
        {
            sb.AppendLine($"ManagersGO: {managersGO.name}");
            var cm = managersGO.GetComponent("CurrencyManager");
            var im = managersGO.GetComponent("InventoryManager");
            sb.AppendLine($" - CurrencyManager present: {(cm!=null)}");
            if (cm!=null)
            {
                var gemsField = cm.GetType().GetField("gems");
                if (gemsField!=null) sb.AppendLine($"   - gems field exists");
            }
            sb.AppendLine($" - InventoryManager present: {(im!=null)}");
        }

        // GachaPool asset quick check
        if (gachaPoolAsset == null) sb.AppendLine("GachaPool asset (inspector): NULL");
        else
        {
            sb.AppendLine($"GachaPool asset provided: {gachaPoolAsset.name}");
        }

        sb.AppendLine("=== DebugVerifier END ===");
        Debug.Log(sb.ToString());
    }
}