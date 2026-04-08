using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
	[SerializeField] private BaseEnemyAI enemy;
	[SerializeField] private Transform healthBarRoot; // optional; defaults to child named "HealthBar"
	[SerializeField] private Slider slider; // optional; defaults to first Slider under root
	[SerializeField] private float hideDelaySeconds = 3f;

	private float hideTimer;
	private TextMeshProUGUI nameText;
	private GameObject nameTextGameObject;
    private float lastHealthFraction = -1f;

	private void OnValidate()
	{
		if (enemy == null) enemy = GetComponentInParent<BaseEnemyAI>();
		if (healthBarRoot == null)
		{
			var t = transform.Find("HealthBar");
			if (t == null && transform.parent != null)
				t = transform.parent.Find("HealthBar");
			if (t == null && transform.name == "HealthBar")
				t = transform; // Script is on HealthBar root (e.g. enemies without Name)
			healthBarRoot = t;
		}
		if (slider == null && healthBarRoot != null)
			slider = healthBarRoot.GetComponentInChildren<Slider>(true);
	}

	private void Awake()
	{
		if (enemy == null) enemy = GetComponentInParent<BaseEnemyAI>();
		if (healthBarRoot == null)
		{
			var t = transform.Find("HealthBar");
			if (t == null && transform.parent != null)
				t = transform.parent.Find("HealthBar");
			if (t == null && transform.name == "HealthBar")
				t = transform; // Script is on HealthBar root (e.g. enemies without Name)
			healthBarRoot = t;
		}
		if (slider == null && healthBarRoot != null)
			slider = healthBarRoot.GetComponentInChildren<Slider>(true);
		ResolveNameText();
	}

	private void ResolveNameText()
	{
		if (healthBarRoot == null) return;
		var nameTransform = healthBarRoot.Find("Name");
		if (nameTransform != null)
		{
			nameText = nameTransform.GetComponent<TextMeshProUGUI>();
			if (nameText == null)
				nameText = nameTransform.GetComponentInChildren<TextMeshProUGUI>(true);
			nameTextGameObject = nameTransform.gameObject;
		}
		else
		{
			var allTmp = healthBarRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
			foreach (var tmp in allTmp)
			{
				if (tmp != null && tmp.gameObject.name == "Name")
				{
					nameText = tmp;
					nameTextGameObject = tmp.gameObject;
					break;
				}
			}
		}
	}

	private void OnEnable()
	{
		if (enemy != null)
		{
			enemy.OnEnemyTookDamage += HandleDamaged;
			enemy.OnEnemyDied += HandleDied;
		}
        Refresh(false);
        if (healthBarRoot != null) healthBarRoot.gameObject.SetActive(false);
	}

	private void OnDisable()
	{
		if (enemy != null)
		{
			enemy.OnEnemyTookDamage -= HandleDamaged;
			enemy.OnEnemyDied -= HandleDied;
		}
	}

	private void Update()
	{
		if (healthBarRoot == null || slider == null || enemy == null) return;

        // safety net: always keep slider in sync with actual health so
        // health bars still work even if damage events fail for some enemies
        float currentFrac = enemy.MaxHealth > 0 ? Mathf.Clamp01(enemy.HealthPercentage) : 0f;
        if (!Mathf.Approximately(currentFrac, lastHealthFraction))
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = currentFrac;
            UpdateNameDisplay();
            lastHealthFraction = currentFrac;

            // auto-show bar whenever health drops below max
            if (currentFrac < 1f && !enemy.IsDead)
            {
                healthBarRoot.gameObject.SetActive(true);
                hideTimer = hideDelaySeconds;
            }
        }

        if (hideTimer > 0f)
        {
            hideTimer -= Time.deltaTime;
            if (hideTimer <= 0f && !enemy.IsDead)
            {
                healthBarRoot.gameObject.SetActive(false);
            }
        }

		// face main camera
		var cam = Camera.main;
		if (cam != null)
		{
			var fwd = cam.transform.rotation * Vector3.forward;
			var up = cam.transform.rotation * Vector3.up;
			healthBarRoot.rotation = Quaternion.LookRotation(fwd, up);
		}
	}

	private void HandleDamaged(BaseEnemyAI _, int __)
	{
		Refresh(true);
	}

	private void HandleDied(BaseEnemyAI _)
	{
		Refresh(true);
	}

	private void Refresh(bool show)
	{
		if (enemy == null || slider == null || healthBarRoot == null) return;
		float frac = enemy.MaxHealth > 0 ? Mathf.Clamp01(enemy.HealthPercentage) : 0f;
		slider.minValue = 0f;
		slider.maxValue = 1f;
		slider.value = frac;
        lastHealthFraction = frac;
		UpdateNameDisplay();
		if (show)
		{
			healthBarRoot.gameObject.SetActive(true);
			hideTimer = hideDelaySeconds;
		}
	}

	private void UpdateNameDisplay()
	{
		if (nameTextGameObject == null)
		{
			if (nameText == null) ResolveNameText();
			if (nameTextGameObject == null) return; // No Name in hierarchy - healthbar still shows, just no name text
		}
		string displayName = null;
		if (enemy != null && enemy.enemyData != null && !string.IsNullOrWhiteSpace(enemy.enemyData.enemyName))
			displayName = enemy.enemyData.enemyName.ToLowerInvariant();
		if (string.IsNullOrEmpty(displayName) || nameText == null)
		{
			if (nameTextGameObject != null) nameTextGameObject.SetActive(false);
			return;
		}
		nameTextGameObject.SetActive(true);
		nameText.text = displayName;
	}
}


