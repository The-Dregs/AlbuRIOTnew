using Photon.Pun;
using UnityEngine;

[
RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviourPun
{
	public bool CanAttack => controller != null && controller.isGrounded;
	public float moveSpeed = 6f;
	public float runSpeed = 11f;
	public float rotationSpeed = 12f;
	public float jumpHeight = 2f;
	public float gravity = -15f;
	public Transform cameraPivot; // assign the CameraRig (the camera orbit root)

	// roll / slide settings
	[Header("roll / slide")]
	public KeyCode rollKey = KeyCode.LeftControl;
	public KeyCode alternateRollKey = KeyCode.C;
	public float rollSpeed = 14f;
	public float rollDuration = 0.45f;
	public float rollCooldown = 0.6f;
	public bool allowRightCtrlAlso = true; // convenience input
	public int rollStaminaCost = 15;

	[Header("jump settings")]
	public int jumpStaminaCost = 10;

	[Header("running stamina usage")]
	[Tooltip("Stamina drained per second while running (Left Shift + forward). Set small value, e.g., 2-5.")]
	public float runningStaminaDrainPerSecond = 3f;
	private float runningStaminaDrainAccumulator = 0f;

	[Header("water blocking")]
	[Tooltip("If enabled, touching water teleports player back to last grounded dry position.")]
	public bool blockWalkingOnWater = true;
	[Tooltip("Layer mask used to detect water colliders.")]
	public LayerMask waterBlockLayer;
	[Tooltip("Short delay after water teleport before re-checking water overlap.")]
	public float waterTeleportGraceSeconds = 0.2f;

	[Header("debug flight")]
	[Tooltip("hidden debug hotkey for toggle flight")]
	public KeyCode flightToggleKey = KeyCode.F7;
	[Tooltip("vertical speed while flight is enabled")]
	public float flightVerticalSpeed = 8f;
	[Tooltip("horizontal movement multiplier while flight is enabled")]
	public float flightHorizontalSpeedMultiplier = 1.5f;

	private CharacterController controller;
	private Vector3 verticalVelocity;
	private Animator animator;
	private bool isJumping = false;
	private bool isCrouched = false; // placeholder, add crouch logic if needed
	private bool attackPressed = false;
	private bool isRunning = false; // centralized running state
	public bool IsRunning => isRunning;
	public bool IsRolling => isRolling;
	private PlayerCombat combat;

	// roll state
	private bool isRolling = false;
	private float rollTimer = 0f;
	private float rollCooldownTimer = 0f;
	private Vector3 rollDirection = Vector3.zero;

	private bool canMove = true;
	private bool canControl = true;
	[SerializeField] private bool enableDebugLogs = false;
	private float inputH;
	private float inputV;
	private bool hasIsGroundedParam;
	private bool hasIsRollingParam;
	private bool hasRollTriggerParam;
	private ThirdPersonCameraOrbit cachedCameraOrbit;
	private Vector3 lastGroundedDryPosition;
	private bool hasLastGroundedDryPosition = false;
	private float waterTeleportGraceTimer = 0f;
	private bool isFlying = false;
	public bool IsFlying => isFlying;

	// when true, prevent the character from rotating to face movement
	// REMOVE rotationLocked and SetRotationLocked; always allow facing movement direction

	public void SetCanMove(bool value)
	{
		canMove = value;
	}

	public void SetCanControl(bool value)
	{
		canControl = value;
		canMove = value;
	}

	public void SetFlying(bool value)
	{
		isFlying = value;
		if (!isFlying)
		{
			bool grounded = controller != null && controller.isGrounded;
			verticalVelocity.y = grounded ? -2f : 0f;
			isJumping = false;
		}
	}

	void Awake()
	{
		controller = GetComponent<CharacterController>();
		animator = GetComponent<Animator>();
		if (animator != null)
		{
			hasIsGroundedParam = AnimatorHasParameter(animator, "IsGrounded");
			hasIsRollingParam = AnimatorHasParameter(animator, "IsRolling");
			hasRollTriggerParam = AnimatorHasParameter(animator, "Roll");
		}
	}


	private PlayerStats playerStats;
	private EffectsManager effectsManager;

	private EquipmentManager equipmentManager;
	private PlayerAudioManager audioManager;

	void Start()
	{
		playerStats = GetComponent<PlayerStats>();
		combat = GetComponent<PlayerCombat>();
		equipmentManager = GetComponent<EquipmentManager>();
		effectsManager = GetComponent<EffectsManager>();
		if (effectsManager == null) effectsManager = GetComponent<VFXManager>();
		audioManager = GetComponent<PlayerAudioManager>();

		// Disable root motion - using manual movement
		if (animator != null)
		{
			animator.applyRootMotion = false;
		}

		// Only enable camera/audio for local player
		if (photonView != null && !photonView.IsMine)
		{
			Camera[] cameras = GetComponentsInChildren<Camera>(true);
			for (int i = 0; i < cameras.Length; i++)
			{
				if (cameras[i] != null)
					cameras[i].enabled = false;
			}

			AudioListener[] listeners = GetComponentsInChildren<AudioListener>(true);
			for (int i = 0; i < listeners.Length; i++)
			{
				if (listeners[i] != null)
					listeners[i].enabled = false;
			}
		}
		else
		{
			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;
		}

		// initialize fallback for water blocking:
		// only record a position if it's both dry and grounded.
		lastGroundedDryPosition = transform.position;
		hasLastGroundedDryPosition = controller != null && controller.isGrounded && !IsCapsuleOverlappingWaterAtCurrentPosition();

	}

	void Update()
{
	if (Photon.Pun.PhotonNetwork.InRoom && photonView != null && !photonView.IsMine)
		return; // Only allow local player to control

	inputH = Input.GetAxisRaw("Horizontal");
	inputV = Input.GetAxisRaw("Vertical");

	if (Input.GetKeyDown(flightToggleKey))
	{
		SetFlying(!isFlying);
		if (enableDebugLogs) Debug.Log($"flight mode: {(isFlying ? "enabled" : "disabled")}");
	}

		// always tick roll timers and stamina regen block regardless of movement gating
		TickRollAndStaminaRegenBlock();

	// block movement input if PauseMenu is open
	bool blockMovementInput = LocalUIManager.Instance != null && LocalUIManager.Instance.IsOwner("PauseMenu");
	if (canControl)
	{
		if (canMove)
		{
			HandleMovement();
		}
		else
		{
			// keep vertical simulation active so the player does not freeze mid-air while input is locked
			ApplyVerticalOnlyMovement();
			// cancel rolling state if any
			isRolling = false;
			rollTimer = 0f;
			isRunning = false;
		}
	}
	UpdateAnimator();
}

	private void ApplyVerticalOnlyMovement()
	{
		if (controller == null) return;

		if (!isFlying && controller.isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f;
			isJumping = false;
		}

		if (isFlying)
		{
			verticalVelocity.y = 0f;
		}
		else
		{
			verticalVelocity.y += gravity * Time.deltaTime;
		}
		controller.Move(new Vector3(0f, verticalVelocity.y, 0f) * Time.deltaTime);
	}
	void UpdateAnimator()
	{
		if (animator == null) return;

		// Speed: use horizontal movement magnitude (ignore vertical). If input is blocked, force 0.
		float speed = canMove ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
		animator.SetFloat("Speed", speed);

	    // IsWalking: moving (forward, backward, or sideways) but not running
		// use centralized running state to keep animator consistent with movement/stamina
		bool isWalking = speed > 0.1f && !isRunning && canMove;
		animator.SetBool("IsWalking", isWalking);

		// IsRunning: reflects actual run state (considering stamina, rolling, etc.)
		// override to false during attack so animator can transition to attack even while player keeps moving fast
		bool animatorRunning = canMove && isRunning;
		if (combat != null && combat.IsAttacking)
		{
			animatorRunning = false;
		}
		animator.SetBool("IsRunning", animatorRunning);

		// IsJumping: set true when jumping, false when grounded
		animator.SetBool("IsJumping", isJumping);

		// IsGrounded: drive directly from CharacterController
		if (hasIsGroundedParam)
		{
			bool groundedNow = controller != null && controller.isGrounded;
			animator.SetBool("IsGrounded", groundedNow);
		}

		// IsCrouched: placeholder, set to false (add crouch logic if needed)
		animator.SetBool("IsCrouched", isCrouched);

		// optional: sync rolling flag if animator has it
		if (hasIsRollingParam)
		{
			animator.SetBool("IsRolling", isRolling);
		}
	}

	void HandleMovement()
	{
		float h = inputH;
		float v = inputV;


		// movement is always relative to camera, not player facing
		Vector3 move = Vector3.zero;
		if (cameraPivot == null)
		{
			// cameraPivot lost (can happen after scene transition) — resolve from hierarchy
			var orbit = GetComponentInChildren<ThirdPersonCameraOrbit>();
			if (orbit != null)
			{
				cameraPivot = orbit.transform;
				cachedCameraOrbit = orbit;
			}
		}
		if (cameraPivot != null)
		{
			if (cachedCameraOrbit == null || cachedCameraOrbit.transform != cameraPivot)
				cachedCameraOrbit = cameraPivot.GetComponent<ThirdPersonCameraOrbit>();
			if (cachedCameraOrbit != null)
			{
				Vector3 camForward = cameraPivot.forward;
				camForward.y = 0f;
				camForward.Normalize();
				Vector3 camRight = cameraPivot.right;
				camRight.y = 0f;
				camRight.Normalize();
				move = camForward * v + camRight * h;
			}
		}
		if (move.sqrMagnitude > 1f) move.Normalize();

		// Check if player is attacking - prevent movement/actions during attacks
		bool isAttacking = combat != null && combat.IsAttacking;

		// try start roll if ctrl or 'c' is pressed and we have a move direction (but not while attacking or exhausted)
		if (!isRolling && !isAttacking && controller.isGrounded)
		{
			bool exhausted = playerStats != null && playerStats.IsExhausted;
			bool rollPressed = Input.GetKeyDown(rollKey) || (allowRightCtrlAlso && Input.GetKeyDown(KeyCode.RightControl)) || Input.GetKeyDown(alternateRollKey);
			bool rooted = playerStats != null && playerStats.IsRooted;
			bool silenced = playerStats != null && playerStats.IsSilenced;
			bool stunned = playerStats != null && playerStats.IsStunned;
			if (rollPressed && !rooted && !stunned && !silenced && !exhausted && move.sqrMagnitude > 0.001f && rollCooldownTimer <= 0f)
			{
				// stamina check
				bool hasStats = playerStats != null;
				int finalCost = rollStaminaCost;
				if (hasStats)
				{
					finalCost = Mathf.Max(1, rollStaminaCost + playerStats.staminaCostModifier);
				}
				if (!hasStats || playerStats.UseStamina(finalCost))
				{
					isRolling = true;
					rollTimer = rollDuration;
					rollDirection = move.normalized;
					// face roll direction instantly
					if (rollDirection.sqrMagnitude > 0.001f)
					{
						Quaternion lookRot = Quaternion.LookRotation(rollDirection, Vector3.up);
						transform.rotation = lookRot;
					}
					// animator trigger if available
					if (animator != null && hasRollTriggerParam)
					{
						animator.SetTrigger("Roll");
					}
					if (enableDebugLogs) Debug.Log($"player roll start, stamina cost: {finalCost}");
					
					// Play roll sound (prefer EffectsManager)
					if (effectsManager != null)
						effectsManager.PlayRollSound();
					else if (audioManager != null)
						audioManager.PlayRollSound();
				}
				else
				{
					if (enableDebugLogs) Debug.Log("not enough stamina to roll!");
				}
			}
		}

		// face movement direction during walk/run (not while rolling or attacking)
		if (!isRolling && !isAttacking && move.sqrMagnitude > 0.0001f)
		{
			Quaternion targetRot = Quaternion.LookRotation(move.normalized, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
		}

		Vector3 horizontal = Vector3.zero;
		
		// Stop all horizontal movement while attacking
		if (isAttacking)
		{
			horizontal = Vector3.zero;
		}
		else if (isRolling)
		{
			// while rolling, override horizontal movement
			if (rollDirection.sqrMagnitude > 0.0001f)
			{
				// Lock rotation to roll direction
				Quaternion lookRot = Quaternion.LookRotation(rollDirection, Vector3.up);
				transform.rotation = lookRot;
				
				float currentRollSpeed = rollSpeed;
				if (playerStats != null) currentRollSpeed += Mathf.Max(0f, playerStats.speedModifier);
				horizontal = rollDirection * currentRollSpeed;
			}
			else
			{
				horizontal = Vector3.zero;
			}
		}
		else
		{
			// running: if run state is true, use runSpeed for movement
			float currentSpeed = moveSpeed;
			if (playerStats != null)
			{
				currentSpeed += playerStats.speedModifier;
				// apply slow percentage to both walk and run
				currentSpeed *= (1f - Mathf.Clamp01(playerStats.slowPercent));
			}
			// Block running when exhausted - force walking speed only
			bool exhausted = playerStats != null && playerStats.IsExhausted;
			if (isRunning && !exhausted)
			{
				currentSpeed = runSpeed;
				if (playerStats != null)
				{
					currentSpeed += playerStats.speedModifier;
					currentSpeed *= (1f - Mathf.Clamp01(playerStats.slowPercent));
				}
			}
			horizontal = move * currentSpeed;
		}

		if (isFlying)
		{
			float boostedSpeed = Mathf.Max(1f, flightHorizontalSpeedMultiplier);
			horizontal *= boostedSpeed;
		}

		// grounding and jumping
		bool isGrounded = controller.isGrounded;
		if (!isFlying && isGrounded && verticalVelocity.y < 0f)
		{
			verticalVelocity.y = -2f;
		}
		bool canJump = !isFlying && !isRolling && !isAttacking && Input.GetKeyDown(KeyCode.Space) && isGrounded;
		if (playerStats != null && (playerStats.IsRooted || playerStats.IsStunned || playerStats.IsExhausted)) canJump = false;
		if (canJump)
		{
			// stamina check for jumping
			int finalJumpCost = jumpStaminaCost;
			if (playerStats != null)
				finalJumpCost = Mathf.Max(1, jumpStaminaCost + playerStats.staminaCostModifier);
			bool canSpend = playerStats == null || playerStats.UseStamina(finalJumpCost);
			if (canSpend)
			{
				verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
				isJumping = true;
				if (enableDebugLogs) Debug.Log($"player jump, stamina cost: {finalJumpCost}");
				
				// Play jump sound (prefer EffectsManager)
				if (effectsManager != null)
					effectsManager.PlayJumpSound();
				else if (audioManager != null)
					audioManager.PlayJumpSound();
			}
			else
			{
				if (enableDebugLogs) Debug.Log("not enough stamina to jump!");
			}
		}
		if (!isFlying && isGrounded && verticalVelocity.y <= 0f)
		{
			if (isJumping)
			{
				// Just landed - play land sound (prefer EffectsManager)
				if (effectsManager != null)
					effectsManager.PlayLandSound();
				else if (audioManager != null)
					audioManager.PlayLandSound();
			}
			isJumping = false;
		}

		if (isFlying)
		{
			bool flyUp = Input.GetKey(KeyCode.Space);
			bool flyDown = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			if (flyUp == flyDown)
			{
				verticalVelocity.y = 0f;
			}
			else
			{
				verticalVelocity.y = flyUp ? flightVerticalSpeed : -flightVerticalSpeed;
			}
			isJumping = false;
		}
		else
		{
			// apply gravity
			verticalVelocity.y += gravity * Time.deltaTime;
		}

		// move
		Vector3 finalMove = horizontal + verticalVelocity;
		controller.Move(finalMove * Time.deltaTime);

		// hard block water entry by snapping back to last grounded dry position.
		if (blockWalkingOnWater && !isFlying && waterBlockLayer.value != 0)
		{
			if (waterTeleportGraceTimer > 0f)
			{
				waterTeleportGraceTimer -= Time.deltaTime;
			}
			else if (IsCapsuleOverlappingWaterAtCurrentPosition())
			{
				bool hasValidFallback = hasLastGroundedDryPosition && !IsCapsuleOverlappingWaterAtPosition(lastGroundedDryPosition);
				if (hasValidFallback)
				{
					TeleportControllerTo(lastGroundedDryPosition);
					isJumping = false;
					isRolling = false;
					verticalVelocity = new Vector3(0f, -2f, 0f);
					waterTeleportGraceTimer = Mathf.Max(0f, waterTeleportGraceSeconds);
				}
				// If no valid dry fallback exists, do not lock movement here.
			}
			else
			{
				// only refresh fallback while grounded to avoid saving mid-jump positions
				if (controller.isGrounded)
				{
					lastGroundedDryPosition = transform.position;
					hasLastGroundedDryPosition = true;
				}
			}
		}

		// do not override Esc here; handled by PauseMenuController to avoid camera drifting
	}
	

	// utility: check animator has parameter to avoid warnings
	private bool AnimatorHasParameter(Animator anim, string paramName)
	{
		if (anim == null) return false;
		foreach (var p in anim.parameters)
		{
			if (p.name == paramName) return true;
		}
		return false;
	}

	// tick timers and stamina regen block every frame, independent of movement gating
	private void TickRollAndStaminaRegenBlock()
	{
		// roll timer and cooldown
		if (isRolling)
		{
			rollTimer -= Time.deltaTime;
			if (rollTimer <= 0f)
			{
				isRolling = false;
				rollCooldownTimer = rollCooldown;
				if (enableDebugLogs) Debug.Log("player roll end");
			}
		}
		if (rollCooldownTimer > 0f)
		{
			rollCooldownTimer -= Time.deltaTime;
		}

		// stamina regen blocking: block when running (any direction with shift) or when rolling
		if (playerStats != null)
		{
			bool moving = (Mathf.Abs(inputH) > 0.5f || Mathf.Abs(inputV) > 0.5f);
			bool exhausted = playerStats.IsExhausted;
			// Block running when exhausted - player can only walk
			bool running = (canControl && canMove) && !isFlying && !exhausted && Input.GetKey(KeyCode.LeftShift) && moving && !isRolling && (playerStats == null || playerStats.currentStamina > 0);
			isRunning = running;
			bool blockRegen = isRunning || isRolling;
			playerStats.SetStaminaRegenBlocked(blockRegen);
		}
	}

	// Checks whether the character capsule currently overlaps water.
	private bool IsCapsuleOverlappingWaterAtCurrentPosition()
	{
		if (controller == null) return false;
		if (waterBlockLayer.value == 0) return false;

		Vector3 centerWorld = transform.TransformPoint(controller.center);
		float radius = Mathf.Max(0.01f, controller.radius * Mathf.Abs(transform.lossyScale.x));
		float height = Mathf.Max(radius * 2f, controller.height * Mathf.Abs(transform.lossyScale.y));
		float half = Mathf.Max(0f, (height * 0.5f) - radius);
		Vector3 top = centerWorld + Vector3.up * half;
		Vector3 bottom = centerWorld - Vector3.up * half;
		Collider[] hits = Physics.OverlapCapsule(top, bottom, radius, waterBlockLayer, QueryTriggerInteraction.Collide);
		return hits != null && hits.Length > 0;
	}

	private bool IsCapsuleOverlappingWaterAtPosition(Vector3 worldPosition)
	{
		if (controller == null) return false;
		if (waterBlockLayer.value == 0) return false;

		Vector3 worldOffset = worldPosition - transform.position;
		Vector3 centerWorld = transform.TransformPoint(controller.center) + worldOffset;
		float radius = Mathf.Max(0.01f, controller.radius * Mathf.Abs(transform.lossyScale.x));
		float height = Mathf.Max(radius * 2f, controller.height * Mathf.Abs(transform.lossyScale.y));
		float half = Mathf.Max(0f, (height * 0.5f) - radius);
		Vector3 top = centerWorld + Vector3.up * half;
		Vector3 bottom = centerWorld - Vector3.up * half;
		Collider[] hits = Physics.OverlapCapsule(top, bottom, radius, waterBlockLayer, QueryTriggerInteraction.Collide);
		return hits != null && hits.Length > 0;
	}

	// Safely teleports a CharacterController without generating bad collision state.
	private void TeleportControllerTo(Vector3 position)
	{
		if (controller == null)
		{
			transform.position = position;
			return;
		}

		bool wasEnabled = controller.enabled;
		if (wasEnabled) controller.enabled = false;
		transform.position = position;
		if (wasEnabled) controller.enabled = true;
	}

	// apply running stamina drain after Update to avoid racing with attack spending
	void LateUpdate()
	{
		if (Photon.Pun.PhotonNetwork.InRoom && photonView != null && !photonView.IsMine)
			return;
		if (playerStats == null) return;
		if (isRunning && runningStaminaDrainPerSecond > 0f)
		{
			float rate = Mathf.Max(0f, runningStaminaDrainPerSecond + playerStats.staminaCostModifier);
			runningStaminaDrainAccumulator += rate * Time.deltaTime;
			if (runningStaminaDrainAccumulator >= 1f)
			{
				int drainInt = Mathf.FloorToInt(runningStaminaDrainAccumulator);
				int available = Mathf.Max(0, playerStats.currentStamina);
				int toDrain = Mathf.Min(drainInt, available);
				if (toDrain > 0)
				{
					playerStats.UseStamina(toDrain);
					runningStaminaDrainAccumulator -= toDrain;
				}
			}
		}
	}
}




