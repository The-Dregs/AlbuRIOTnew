using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Applies local-only hearing attenuation to an exposed AudioMixer parameter.
/// Intended for effects like underwater/muffled hearing.
/// </summary>
public class LocalPlayerHearingController : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string exposedHearingParameter = "PlayerHearingDb";

    [Header("Hearing")]
    [SerializeField] private float baseHearingMultiplier = 1f;
    [SerializeField] private float smoothingSpeed = 8f;

    [Header("Optional Photon")]
    [SerializeField] private PhotonView ownerPhotonView;

    private const float MinLinearVolume = 0.0001f;
    private readonly Dictionary<int, float> zoneMultipliers = new Dictionary<int, float>();
    private float currentMultiplier = 1f;
    private float targetMultiplier = 1f;

    private void Awake()
    {
        if (ownerPhotonView == null)
        {
            ownerPhotonView = GetComponentInParent<PhotonView>();
        }

        baseHearingMultiplier = Mathf.Clamp(baseHearingMultiplier, MinLinearVolume, 1f);
        currentMultiplier = baseHearingMultiplier;
        targetMultiplier = baseHearingMultiplier;
        ApplyMixerValue(currentMultiplier);
    }

    private void Update()
    {
        if (!CanControlLocalAudio()) return;

        targetMultiplier = ResolveTargetMultiplier();
        currentMultiplier = Mathf.MoveTowards(currentMultiplier, targetMultiplier, smoothingSpeed * Time.deltaTime);
        ApplyMixerValue(currentMultiplier);
    }

    public void EnterHearingZone(int zoneId, float zoneMultiplier)
    {
        zoneMultiplier = Mathf.Clamp(zoneMultiplier, MinLinearVolume, 1f);
        zoneMultipliers[zoneId] = zoneMultiplier;
    }

    public void ExitHearingZone(int zoneId)
    {
        zoneMultipliers.Remove(zoneId);
    }

    public void SetBaseHearingMultiplier(float multiplier)
    {
        baseHearingMultiplier = Mathf.Clamp(multiplier, MinLinearVolume, 1f);
    }

    private bool CanControlLocalAudio()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            return true;
        if (ownerPhotonView == null)
            return true;
        return ownerPhotonView.IsMine;
    }

    private float ResolveTargetMultiplier()
    {
        float target = baseHearingMultiplier;
        foreach (float zoneValue in zoneMultipliers.Values)
        {
            target = Mathf.Min(target, zoneValue);
        }
        return Mathf.Clamp(target, MinLinearVolume, 1f);
    }

    private void ApplyMixerValue(float linearValue)
    {
        if (audioMixer == null) return;
        float decibels = Mathf.Log10(Mathf.Clamp(linearValue, MinLinearVolume, 1f)) * 20f;
        audioMixer.SetFloat(exposedHearingParameter, decibels);
    }
}
