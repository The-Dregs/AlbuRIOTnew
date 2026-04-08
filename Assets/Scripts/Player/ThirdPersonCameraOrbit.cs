using UnityEngine;

public class ThirdPersonCameraOrbit : MonoBehaviour
{
	// Call this to rotate camera to look at a target position (e.g., NPC)
	public void LookAtTarget(Vector3 targetPosition)
	{
		Vector3 direction = targetPosition - transform.position;
		direction.y = 0f;
		if (direction != Vector3.zero)
		{
			yaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
		}
	}

	public Transform target; // player
	public Transform cameraTransform; // camera
	public float mouseSensitivity = 200f;
	public float minPitch = -30f;
	public float maxPitch = 60f;
	public float followDistance = 3.5f;
	public float followHeight = 1.0f;

	[Header("Collision Settings")]
	public LayerMask collisionMask = ~0; // collide with everything by default
	public float collisionRadius = 0.2f;
	public float collisionBuffer = 0.1f;
	public float followSmooth = 10f;
	public float returnSmooth = 7f;

	private float yaw;
	private float pitch;
	private bool isFreeLook = false;
	public bool cameraControlActive = true; // set by controller
	private bool rotationLocked = false; // when true, camera follows position but ignores mouse rotation
	private Vector3 smoothPositionVelocity; // For camera rig position smoothing
	private Vector3 smoothCameraPositionVelocity; // For camera position smoothing
	private Photon.Pun.PhotonView parentPhotonView;

	public void SetCameraControlActive(bool value)
	{
		cameraControlActive = value;
	}

	public void SetRotationLocked(bool value)
	{
		rotationLocked = value;
	}

	void Start()
	{
		parentPhotonView = GetComponentInParent<Photon.Pun.PhotonView>();
		if (cameraTransform == null)
		{
			Camera cam = GetComponentInChildren<Camera>();
			if (cam != null) cameraTransform = cam.transform;
		}
		Vector3 angles = transform.eulerAngles;
		yaw = angles.y;
		pitch = 12f; // look slightly down at the player, but not too high
	}

	void LateUpdate()
	{
		// only control camera for local player in multiplayer
		if (parentPhotonView != null && !parentPhotonView.IsMine) return;
		if (target == null || cameraTransform == null) return;
		if (!cameraControlActive) return;

		// block ALL camera rotation input (mouse, WASD, etc) when rotationLocked is true
		bool rightMouseHeld = Input.GetMouseButton(1);
		float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
		float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

		if (!rotationLocked)
		{
			if (rightMouseHeld)
			{
				isFreeLook = true;
				yaw += mouseX;
				pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
			}
			else
			{
				// normal: rotate camera with mouse
				yaw += mouseX;
				pitch = Mathf.Clamp(pitch - mouseY, minPitch, maxPitch);
				isFreeLook = false;
			}
		}

		// Smooth camera rig position to prevent jitter during fast movement
		Vector3 targetRigPosition = target.position + Vector3.up * followHeight;
		transform.position = Vector3.SmoothDamp(transform.position, targetRigPosition, ref smoothPositionVelocity, 1f / followSmooth, Mathf.Infinity, Time.deltaTime);
		
		// Set camera rig rotation
		transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

		// Calculate desired camera position with collision
		Vector3 camOffset = transform.rotation * new Vector3(0, 0, -followDistance);
		Vector3 desiredCamPos = transform.position + camOffset;
		Vector3 pivotPos = transform.position;
		Vector3 dir = (desiredCamPos - pivotPos).normalized;
		float distance = followDistance;
		Ray ray = new Ray(pivotPos, dir);
		if (Physics.SphereCast(ray, collisionRadius, out RaycastHit hit, followDistance, collisionMask, QueryTriggerInteraction.Ignore))
		{
			distance = hit.distance - collisionBuffer;
			if (distance < 0.1f) distance = 0.1f;
		}
		
		// Smooth camera position movement to prevent spazzing
		Vector3 targetCamPos = pivotPos + dir * distance;
		cameraTransform.position = Vector3.SmoothDamp(cameraTransform.position, targetCamPos, ref smoothCameraPositionVelocity, 1f / returnSmooth, Mathf.Infinity, Time.deltaTime);
		cameraTransform.LookAt(transform.position + Vector3.up * 0.5f);
	}

	// helper to set camera targets after spawn
	public void AssignTargets(Transform playerTarget, Transform camTransform)
	{
		target = playerTarget;
		cameraTransform = camTransform;
	}

	/// <summary>
	/// Instantly positions camera rig and camera at the correct follow position.
	/// Call after teleporting the player to avoid smooth-damp "swoosh" on first frames.
	/// </summary>
	public void SnapToTarget()
	{
		if (target == null) return;

		// reset smooth damp velocities so there's no momentum from a previous position
		smoothPositionVelocity = Vector3.zero;
		smoothCameraPositionVelocity = Vector3.zero;

		// snap rig to target immediately
		Vector3 rigPos = target.position + Vector3.up * followHeight;
		transform.position = rigPos;
		transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

		// snap camera to correct orbit position
		if (cameraTransform != null)
		{
			Vector3 camOffset = transform.rotation * new Vector3(0, 0, -followDistance);
			cameraTransform.position = rigPos + camOffset;
			cameraTransform.LookAt(rigPos + Vector3.up * 0.5f);
		}
	}
}


