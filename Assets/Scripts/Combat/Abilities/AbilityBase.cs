using UnityEngine;

namespace AlbuRIOT.Abilities
{
    // base type for player abilities (ScriptableObject so we can configure data)
    public abstract class AbilityBase : ScriptableObject
    {
        [Header("meta")]
        public string abilityName = "Ability";
        [TextArea] public string description;
        public Sprite icon;

        [Header("cooldown")]
        public float cooldown = 5f;

        [HideInInspector] public float lastUseTime = -999f;

        public bool IsReady => Time.time >= lastUseTime + cooldown;

        // called by controller when player activates the slot
    public abstract bool Execute(GameObject user);

        // helper for cooldown start
        protected void MarkUsed()
        {
            lastUseTime = Time.time;
        }
    }
}
