using Photon.Pun;
using UnityEngine;

// syncs essential animator params over the network so remote players see correct animations
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public class PlayerAnimatorSync : MonoBehaviourPun, IPunObservable
{
    private Animator _anim;
    private CharacterController _cc;
    private ThirdPersonController _tpc;
    private PlayerCombat _combat;
    private PlayerStats _stats;

    // local-only derived values
    private int _attackSeq = 0;
    private bool _prevIsAttacking = false;
    private int _rollSeq = 0;
    private bool _prevIsRolling = false;
    private int _lastComboIndex = -1;
    private bool _lastIsArmed = false;
    private bool hasIsArmedStanceParam;
    private bool hasIsUnarmedStanceParam;
    private bool hasIsDeadParam;
    private bool hasIsExhaustedParam;
    private bool hasIsCrawlingParam;
    private bool hasIsGroundedParam;
    private bool hasIsRollingParam;
    private bool hasComboIndexParam;
    private bool hasIsArmedParam;
    private bool hasAttackTriggerParam;
    private bool hasRollTriggerParam;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        _cc = GetComponent<CharacterController>();
        _tpc = GetComponent<ThirdPersonController>();
        _combat = GetComponent<PlayerCombat>();
        _stats = GetComponent<PlayerStats>();

        if (_tpc == null) _tpc = GetComponentInChildren<ThirdPersonController>(true);
        if (_combat == null) _combat = GetComponentInChildren<PlayerCombat>(true);
        if (_stats == null) _stats = GetComponentInChildren<PlayerStats>(true);
        if (_cc == null) _cc = GetComponentInChildren<CharacterController>(true);

        if (_anim != null)
        {
            hasIsArmedStanceParam = AnimatorHasParameter(_anim, "IsArmedStance");
            hasIsUnarmedStanceParam = AnimatorHasParameter(_anim, "IsUnarmedStance");
            hasIsDeadParam = AnimatorHasParameter(_anim, "IsDead");
            hasIsExhaustedParam = AnimatorHasParameter(_anim, "IsExhausted");
            hasIsCrawlingParam = AnimatorHasParameter(_anim, "IsCrawling");
            hasIsGroundedParam = AnimatorHasParameter(_anim, "IsGrounded");
            hasIsRollingParam = AnimatorHasParameter(_anim, "IsRolling");
            hasComboIndexParam = AnimatorHasParameter(_anim, "ComboIndex");
            hasIsArmedParam = AnimatorHasParameter(_anim, "IsArmed");
            hasAttackTriggerParam = AnimatorHasParameter(_anim, "Attack");
            hasRollTriggerParam = AnimatorHasParameter(_anim, "Roll");
        }
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            bool nowAttacking = _combat != null && _combat.IsAttacking;
            if (nowAttacking && !_prevIsAttacking)
            {
                // rising edge: increment sequence so remotes can set trigger exactly once
                _attackSeq++;
            }
            _prevIsAttacking = nowAttacking;

            bool nowRolling = _tpc != null && _tpc.IsRolling;
            if (nowRolling && !_prevIsRolling)
            {
                _rollSeq++;
            }
            _prevIsRolling = nowRolling;
        }
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // collect local animator state to send
            float speed = 0f;
            if (_cc != null)
            {
                var v = _cc.velocity;
                speed = new Vector3(v.x, 0f, v.z).magnitude;
            }
            bool isWalking = _anim != null && _anim.GetBool("IsWalking");
            bool isRunning = _anim != null && _anim.GetBool("IsRunning");
            bool isJumping = _anim != null && _anim.GetBool("IsJumping");
            bool isCrouched = _anim != null && _anim.GetBool("IsCrouched");
            bool isGrounded = _cc != null && _cc.isGrounded;
            bool isRolling = false;
            if (_tpc != null) isRolling = _tpc.IsRolling;
            
            // Get combo state from PlayerCombat
            int comboIndex = _combat != null ? _combat.CurrentComboIndex : 0;
            bool isArmed = _combat != null ? _combat.IsArmed : false;
            bool isArmedStance = _anim != null && hasIsArmedStanceParam && _anim.GetBool("IsArmedStance");
            bool isUnarmedStance = _anim != null && hasIsUnarmedStanceParam && _anim.GetBool("IsUnarmedStance");
            bool isDead = _anim != null && hasIsDeadParam && _anim.GetBool("IsDead");
            bool isExhausted = _anim != null && hasIsExhaustedParam && _anim.GetBool("IsExhausted");
            bool isCrawling = _anim != null && hasIsCrawlingParam && _anim.GetBool("IsCrawling");
            float animatorSpeed = _anim != null ? _anim.speed : 1f;

            stream.SendNext(speed);
            stream.SendNext(isWalking);
            stream.SendNext(isRunning);
            stream.SendNext(isJumping);
            stream.SendNext(isCrouched);
            stream.SendNext(isGrounded);
            stream.SendNext(isRolling);
            stream.SendNext(_attackSeq);
            stream.SendNext(_rollSeq);
            stream.SendNext(comboIndex);
            stream.SendNext(isArmed);
            stream.SendNext(isArmedStance);
            stream.SendNext(isUnarmedStance);
            stream.SendNext(isDead);
            stream.SendNext(isExhausted);
            stream.SendNext(isCrawling);
            stream.SendNext(animatorSpeed);
        }
        else
        {
            // apply received state to remote animator
            float speed = (float)stream.ReceiveNext();
            bool isWalking = (bool)stream.ReceiveNext();
            bool isRunning = (bool)stream.ReceiveNext();
            bool isJumping = (bool)stream.ReceiveNext();
            bool isCrouched = (bool)stream.ReceiveNext();
            bool isGrounded = (bool)stream.ReceiveNext();
            bool isRolling = (bool)stream.ReceiveNext();
            int attackSeqRemote = (int)stream.ReceiveNext();
            int rollSeqRemote = (int)stream.ReceiveNext();
            int comboIndexRemote = (int)stream.ReceiveNext();
            bool isArmedRemote = (bool)stream.ReceiveNext();
            bool isArmedStanceRemote = (bool)stream.ReceiveNext();
            bool isUnarmedStanceRemote = (bool)stream.ReceiveNext();
            bool isDeadRemote = (bool)stream.ReceiveNext();
            bool isExhaustedRemote = (bool)stream.ReceiveNext();
            bool isCrawlingRemote = (bool)stream.ReceiveNext();
            float animatorSpeedRemote = (float)stream.ReceiveNext();

            if (_anim != null)
            {
                _anim.SetFloat("Speed", speed);
                _anim.SetBool("IsWalking", isWalking);
                _anim.SetBool("IsRunning", isRunning);
                _anim.SetBool("IsJumping", isJumping);
                _anim.SetBool("IsCrouched", isCrouched);
                if (hasIsGroundedParam)
                    _anim.SetBool("IsGrounded", isGrounded);
                if (hasIsRollingParam)
                    _anim.SetBool("IsRolling", isRolling);
                
                // Sync combo parameters
                if (hasComboIndexParam)
                    _anim.SetInteger("ComboIndex", comboIndexRemote);
                if (hasIsArmedParam)
                    _anim.SetBool("IsArmed", isArmedRemote);
                if (hasIsArmedStanceParam)
                    _anim.SetBool("IsArmedStance", isArmedStanceRemote);
                if (hasIsUnarmedStanceParam)
                    _anim.SetBool("IsUnarmedStance", isUnarmedStanceRemote);
                if (hasIsDeadParam)
                    _anim.SetBool("IsDead", isDeadRemote);
                if (hasIsExhaustedParam)
                    _anim.SetBool("IsExhausted", isExhaustedRemote);
                if (hasIsCrawlingParam)
                    _anim.SetBool("IsCrawling", isCrawlingRemote);
                _anim.speed = animatorSpeedRemote;
            }

            // detect new attacks and play the trigger on remote
            if (attackSeqRemote != _lastRecvAttackSeq)
            {
                _lastRecvAttackSeq = attackSeqRemote;
                if (_anim != null && hasAttackTriggerParam)
                {
                    _anim.SetTrigger("Attack");
                }
            }

            // detect roll start and play trigger on remote
            if (rollSeqRemote != _lastRecvRollSeq)
            {
                _lastRecvRollSeq = rollSeqRemote;
                if (_anim != null && hasRollTriggerParam)
                {
                    _anim.SetTrigger("Roll");
                }
            }
        }
    }

    private int _lastRecvAttackSeq = 0;
    private int _lastRecvRollSeq = 0;

    private static bool AnimatorHasParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }
}
