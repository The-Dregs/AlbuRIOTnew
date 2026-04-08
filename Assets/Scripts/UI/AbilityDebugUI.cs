using TMPro;
using UnityEngine;

namespace AlbuRIOT.UI
{
    [DisallowMultipleComponent]
    public class AbilityDebugUI : MonoBehaviour
    {
        [Header("target & text")]
        public Transform followTarget; // for world-space canvases
        public TextMeshProUGUI text;   // assign your TMP text on a Canvas

        [Header("position (world-space only)")]
        public Vector3 offset = new Vector3(0, 2.2f, 0);


        void Awake()
        {
            // ability controller logic removed; now handled by new system
        }

        void LateUpdate()
        {
            // only reposition for world-space canvas
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && followTarget != null)
            {
                transform.position = followTarget.position + offset;
            }
            UpdateText();
        }

        private void UpdateText()
        {
            if (text == null) return;
            // update debug text to reflect new system (PowerStealManager/PlayerSkillSlots)
            text.text = "ability debug: see PlayerSkillSlots";
        }
    }
}
