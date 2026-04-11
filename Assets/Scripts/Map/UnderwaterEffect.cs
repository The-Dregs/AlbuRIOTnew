using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// Simple underwater visual effect based on camera height relative to a water surface.
/// Attach this to the main Camera and assign the water plane transform.
/// </summary>
public class UnderwaterEffect : MonoBehaviour
{
	[Header("Water reference")]
	[Tooltip("Transform of the water surface (e.g. your Water plane). Optional if you use auto-find by layer.")]
	public Transform waterSurface;

	[Tooltip("If true and waterSurface is not set, the script will search for a renderer or collider on the given waterLayer and use its Y position as the water level.")]
	public bool autoFindWaterByLayer = true;

	[Tooltip("Layer mask used to auto-detect water objects (e.g. set this to your 'Water' layer).")]
	public LayerMask waterLayer;

	[Tooltip("Optional offset added to waterSurface Y (use small negative value if effect should start slightly below the surface).")]
	public float waterLevelOffset = -0.2f;

	[Header("Underwater fog settings")]
	public Color underwaterFogColor = new Color(0f, 0.35f, 0.7f, 1f);
	[Tooltip("Fog density while underwater. Higher = more murky.")]
	public float underwaterFogDensity = 0.06f;

	[Header("Optional background tint")]
	[Tooltip("If true, the camera background color will also be tinted underwater.")]
	public bool tintCameraBackground = true;

	[Header("Optional UI overlay")]
	[Tooltip("Full-screen Image used as a blue overlay (similar to the red damage overlay).")]
	public Image underwaterOverlayImage;
	[Tooltip("Child GameObject name to auto-find if underwaterOverlayImage is not assigned.")]
	public string overlayChildName = "UnderwaterOverlay";
	[Range(0f, 1f)]
	[Tooltip("Maximum alpha the overlay reaches when fully underwater.")]
	public float overlayMaxAlpha = 0.65f;
	[Tooltip("How fast the overlay fades in per second when underwater.")]
	public float overlayFadeInPerSecond = 3f;
	[Tooltip("How fast the overlay fades out per second when above water.")]
	public float overlayFadeOutPerSecond = 2f;

	private bool _cachedDefaults = false;
	private Color _defaultBackgroundColor;
	private Camera _cam;
	private float _overlayCurrentAlpha = 0f;
	private PhotonView _parentPhotonView;
	private bool _wasUnderwaterLastFrame;

	void Start()
	{
		_cam = GetComponent<Camera>();
		_parentPhotonView = GetComponentInParent<PhotonView>();
		TryFindWaterSurface();
		ResolveOverlayImage();
		CacheDefaults();
	}

	void CacheDefaults()
	{
		if (_cachedDefaults) return;

		if (_cam != null)
		{
			_defaultBackgroundColor = _cam.backgroundColor;
		}

		_cachedDefaults = true;
	}

	/// <summary>
	/// Attempts to find a water surface transform using the configured waterLayer.
	/// Called at Start and can be called again later if needed.
	/// </summary>
	public void TryFindWaterSurface()
	{
		if (!autoFindWaterByLayer || waterSurface != null) return;
		if (waterLayer == 0) return; // no layer assigned

		// Look for any renderer or collider on the water layer.
		float bestY = float.NegativeInfinity;
		Transform bestTransform = null;

		// Search renderers
		var renderers = FindObjectsOfType<Renderer>();
		foreach (var r in renderers)
		{
			if (((1 << r.gameObject.layer) & waterLayer.value) == 0) continue;
			float y = r.bounds.center.y;
			if (y > bestY)
			{
				bestY = y;
				bestTransform = r.transform;
			}
		}

		// Fallback: search colliders if nothing found via renderers
		if (bestTransform == null)
		{
			var colliders = FindObjectsOfType<Collider>();
			foreach (var c in colliders)
			{
				if (((1 << c.gameObject.layer) & waterLayer.value) == 0) continue;
				float y = c.bounds.center.y;
				if (y > bestY)
				{
					bestY = y;
					bestTransform = c.transform;
				}
			}
		}

		if (bestTransform != null)
		{
			waterSurface = bestTransform;
		}
	}

	private void ResolveOverlayImage()
	{
		if (underwaterOverlayImage != null) return;

		// Try by child name first
		if (!string.IsNullOrEmpty(overlayChildName))
		{
			foreach (var t in GetComponentsInChildren<Transform>(true))
			{
				if (t.name == overlayChildName)
				{
					var img = t.GetComponent<Image>();
					if (img != null)
					{
						underwaterOverlayImage = img;
						Color c = underwaterOverlayImage.color;
						c.a = 0f;
						underwaterOverlayImage.color = c;
						return;
					}
				}
			}
		}

		// Fallback: any image in children
		var anyImage = GetComponentInChildren<Image>(true);
		if (anyImage != null)
		{
			underwaterOverlayImage = anyImage;
			Color c = underwaterOverlayImage.color;
			c.a = 0f;
			underwaterOverlayImage.color = c;
		}
	}

	private void SetOverlayAlpha(float a)
	{
		_overlayCurrentAlpha = Mathf.Clamp01(a);
		if (underwaterOverlayImage == null) ResolveOverlayImage();
		if (underwaterOverlayImage == null) return;

		if (!underwaterOverlayImage.gameObject.activeSelf)
			underwaterOverlayImage.gameObject.SetActive(true);
		if (!underwaterOverlayImage.enabled)
			underwaterOverlayImage.enabled = true;

		Color c = underwaterOverlayImage.color;
		c.a = _overlayCurrentAlpha;
		underwaterOverlayImage.color = c;
	}

	void OnDisable()
	{
		// Keep camera-local visuals clean.
		if (_cam != null)
			_cam.backgroundColor = _defaultBackgroundColor;

		// Hand global fog control back to day/night manager if we were underwater.
		if (_wasUnderwaterLastFrame && DayNightCycleManager.Instance != null)
			DayNightCycleManager.Instance.ForceApplyEnvironmentNow();
		_wasUnderwaterLastFrame = false;

		// Reset overlay to transparent when this effect is disabled
		SetOverlayAlpha(0f);
	}

	void LateUpdate()
	{
		// Only run visuals for the local player's camera in multiplayer
		if (_parentPhotonView != null && PhotonNetwork.InRoom && !_parentPhotonView.IsMine)
		{
			return;
		}

		if (waterSurface == null)
		{
			// try again in case water spawned after this component
			TryFindWaterSurface();
		}

		if (waterSurface == null)
		{
			return;
		}

		CacheDefaults();

		float waterLevel = waterSurface.position.y + waterLevelOffset;
		bool isUnderwater = transform.position.y < waterLevel;

		if (isUnderwater)
		{
			_wasUnderwaterLastFrame = true;
			RenderSettings.fog = true;
			RenderSettings.fogColor = underwaterFogColor;
			RenderSettings.fogDensity = underwaterFogDensity;

			if (tintCameraBackground && _cam != null)
			{
				_cam.backgroundColor = underwaterFogColor;
			}

			// Fade overlay toward max alpha
			float target = overlayMaxAlpha;
			_overlayCurrentAlpha = Mathf.MoveTowards(_overlayCurrentAlpha, target, overlayFadeInPerSecond * Time.deltaTime);
			SetOverlayAlpha(_overlayCurrentAlpha);
		}
		else
		{
			if (_wasUnderwaterLastFrame)
			{
				// Let day/night instantly restore the correct network-driven fog profile.
				if (DayNightCycleManager.Instance != null)
					DayNightCycleManager.Instance.ForceApplyEnvironmentNow();
				_wasUnderwaterLastFrame = false;
			}

			if (tintCameraBackground && _cam != null)
			{
				_cam.backgroundColor = _defaultBackgroundColor;
			}

			// Fade overlay out
			_overlayCurrentAlpha = Mathf.MoveTowards(_overlayCurrentAlpha, 0f, overlayFadeOutPerSecond * Time.deltaTime);
			SetOverlayAlpha(_overlayCurrentAlpha);
		}
	}
}

