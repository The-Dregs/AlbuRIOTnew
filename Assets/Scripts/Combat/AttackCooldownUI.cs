using UnityEngine;
using UnityEngine.UI;

public class AttackCooldownUI : MonoBehaviour
{
    public PlayerCombat playerCombat;
    public Slider cooldownSlider;

    void Update()
    {
        if (playerCombat != null && cooldownSlider != null)
        {
            cooldownSlider.value = 1f - playerCombat.AttackCooldownProgress;
            cooldownSlider.gameObject.SetActive(playerCombat.AttackCooldownProgress > 0f);
        }
    }
}
