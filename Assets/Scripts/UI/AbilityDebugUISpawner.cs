using UnityEngine;

namespace AlbuRIOT.UI
{
    // deprecated: no longer spawns UI. Please remove this component from prefabs and use AbilityHUDText on a Canvas TMP instead.
    [System.Obsolete("AbilityDebugUISpawner is deprecated. Use a Canvas TextMeshProUGUI with AbilityHUDText.")]
    [DisallowMultipleComponent]
    public class AbilityDebugUISpawner : MonoBehaviour
    {
        void Start()
        {
            Debug.LogWarning("AbilityDebugUISpawner is deprecated and does nothing. Remove this component and add AbilityHUDText to a Canvas TMP.");
        }
    }
}
