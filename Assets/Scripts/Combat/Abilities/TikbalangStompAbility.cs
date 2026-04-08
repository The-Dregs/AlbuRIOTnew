using System.Collections;
using Photon.Pun;
using UnityEngine;

namespace AlbuRIOT.Abilities
{
    [CreateAssetMenu(fileName = "TikbalangStomp", menuName = "AlbuRIOT/Abilities/Tikbalang Stomp")] 
    public class TikbalangStompAbility : AbilityBase
    {
        [Header("stomp")] public float stompRadius = 4f; public int stompDamage = 35; public LayerMask enemyLayers;
        [Header("speed boost")] public float speedBoost = 4f; public float boostDuration = 3f;
        [Header("fx (optional)")] public GameObject stompVfxPrefab; public AudioClip stompSfx;
        [Header("animation")] public string stompTrigger = "Stomp";

    public override bool Execute(GameObject user)
        {
            if (user == null) return false;
            // only local player triggers logic; damage relayed per enemy implementation
            var pv = user.GetComponent<PhotonView>();
            if (PhotonNetwork.InRoom && pv != null && !pv.IsMine) return false;

            // trigger stomp animation across network
            // animation logic now handled by new system (PowerStealManager/PlayerSkillSlots)

            // do AOE overlap for enemies
            Vector3 center = user.transform.position;
            Collider[] hits = Physics.OverlapSphere(center, stompRadius, enemyLayers);
            int unique = 0;
            // dedupe by the enemy damageable component (gameobject root)
            var seen = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var c in hits)
            {
                var dmgIf = c.GetComponentInParent<MonoBehaviour>();
                // prefer interface lookup for clarity
                var dmg = c.GetComponentInParent<IEnemyDamageable>();
                if (dmg != null)
                {
                    var mb = dmg as MonoBehaviour;
                    if (mb != null) seen.Add(mb.gameObject);
                }
            }
            foreach (var go in seen) { EnemyDamageRelay.Apply(go, stompDamage, user); unique++; }

            // spawn vfx/sfx locally (optional)
            if (stompVfxPrefab != null)
            {
                Object.Instantiate(stompVfxPrefab, center, Quaternion.identity);
            }
            if (stompSfx != null)
            {
                var src = user.GetComponent<AudioSource>(); if (src != null) src.PlayOneShot(stompSfx);
            }

            // apply temporary speed boost to the user
            var boostRunner = user.GetComponent<SpeedBoostRuntime>();
            if (boostRunner == null) boostRunner = user.AddComponent<SpeedBoostRuntime>();
            boostRunner.Run(speedBoost, boostDuration);

            MarkUsed();
            Debug.Log($"tikbalang stomp used. enemies hit: {unique}, speed +{speedBoost} for {boostDuration}s");
            return true;
        }

        // helper runtime component for timed boost on a player instance
        private class SpeedBoostRuntime : MonoBehaviour
        {
            private Coroutine routine;
            public void Run(float add, float dur)
            {
                if (routine != null) StopCoroutine(routine);
                routine = StartCoroutine(Co(add, dur));
            }

            private IEnumerator Co(float add, float dur)
            {
                var ps = GetComponent<PlayerStats>();
                if (ps != null) ps.speedModifier += add;
                yield return new WaitForSeconds(dur);
                if (ps != null) ps.speedModifier -= add;
                Debug.Log("tikbalang speed boost ended");
                routine = null;
            }
        }
    }
}
