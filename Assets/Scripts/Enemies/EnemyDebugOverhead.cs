using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class EnemyDebugOverhead : MonoBehaviour
{
	/// <summary>Global toggle for all enemy debug overlays. F8 toggles this.</summary>
	public static bool GlobalShowDebugOverlay { get; private set; } = false;

	[SerializeField] private BaseEnemyAI enemy;
	[SerializeField] private bool enable = true;
	[SerializeField] private float yOffset = 2.2f;
	[SerializeField] private Color color = Color.yellow;
	[SerializeField] private int fontSize = 3;

	private static int _lastF8Frame = -1;
	private TextMeshPro tmp;

	/// <summary>Enable or disable this enemy's debug overlay (still requires F8 to show).</summary>
	public void SetEnable(bool value) { enable = value; }

	private void Awake()
	{
		if (enemy == null) enemy = GetComponentInParent<BaseEnemyAI>();
	}

	private void Update()
	{
		// F8 toggles global overlay (only process once per frame)
		if (Input.GetKeyDown(KeyCode.F8) && Time.frameCount != _lastF8Frame)
		{
			_lastF8Frame = Time.frameCount;
			GlobalShowDebugOverlay = !GlobalShowDebugOverlay;
		}

		if (!enable || !GlobalShowDebugOverlay || enemy == null)
		{
			if (tmp != null) tmp.gameObject.SetActive(false);
			return;
		}
		EnsureTMP();
		if (tmp != null) tmp.gameObject.SetActive(true);
		UpdateBillboard();
	}

	private void EnsureTMP()
	{
		if (tmp != null) return;
		var go = new GameObject("DebugOverhead");
		go.transform.SetParent(enemy.transform);
		go.transform.localPosition = new Vector3(0f, yOffset, 0f);
		tmp = go.AddComponent<TextMeshPro>();
		tmp.fontSize = fontSize;
		tmp.alignment = TextAlignmentOptions.Center;
		tmp.color = color;
		tmp.text = string.Empty;
		tmp.textWrappingMode = TextWrappingModes.NoWrap;
	}

	private System.Text.StringBuilder textBuilder = new System.Text.StringBuilder(256);
	
	private void UpdateBillboard()
	{
		if (tmp == null) return;
		var cam = Camera.main;
		if (cam != null)
		{
			tmp.transform.rotation = Quaternion.LookRotation(tmp.transform.position - cam.transform.position);
		}
		float hp = enemy.HealthPercentage;
		var target = enemy.Target;
		float dist = target != null ? Vector3.Distance(enemy.transform.position, target.position) : -1f;
		string state = enemy.GetEffectiveStateForDebug();
		Vector3 tgtPos = target != null ? target.position : Vector3.zero;
		float basicCd = Mathf.Max(enemy.BasicCooldownRemaining, enemy.BasicCooldownTime);
		
		textBuilder.Clear();
		textBuilder.Append(state).Append("  ");
		if (target != null)
		{
			textBuilder.Append("tgt:").Append(dist.ToString("F1")).Append("  ");
		}
		textBuilder.Append("hp:").Append(hp.ToString("P0"));
		string header = textBuilder.ToString();

		textBuilder.Clear();
		textBuilder.Append(header).Append("\n").Append(enemy.DebugDetailString).Append("\nCD basic:").Append(basicCd.ToString("F1"));
		
		var amo = enemy as AmomongoAI;
		if (amo != null)
		{
			textBuilder.Append("  slam:").Append(amo.SlamCooldownRemaining.ToString("F1"))
				.Append("  berserk:").Append(amo.BerserkCooldownRemaining.ToString("F1"))
				.Append("\nbuffs Dmg:").Append(amo.IsBerserk ? "on" : "off")
				.Append("(").Append(amo.BerserkTimeRemaining.ToString("F1")).Append(")  Spd:")
				.Append(amo.IsBerserk ? "on" : "off")
				.Append("(").Append(amo.BerserkTimeRemaining.ToString("F1")).Append(")  Sta:off(-)  Hp:off(-)");
		}
		else
		{
			var bung = enemy as BungisngisAI;
			if (bung != null)
			{
				textBuilder.Append("  laugh:").Append(bung.LaughCooldownRemaining.ToString("F1"))
					.Append("  pound:").Append(bung.PoundCooldownRemaining.ToString("F1"));
			}
			else
			{
				var kapre = enemy as KapreAI;
				if (kapre != null)
				{
					textBuilder.Append("  vanish:").Append(kapre.VanishCooldownRemaining.ToString("F1"))
						.Append("  treeslam:").Append(kapre.TreeSlamCooldownRemaining.ToString("F1"));
				}
				else
				{
					var shadowDiwata = enemy as ShadowTouchedDiwataAI;
					if (shadowDiwata != null)
					{
						textBuilder.Append("  eclipseveil:").Append(shadowDiwata.EclipseVeilCooldownRemaining.ToString("F1"))
							.Append("  lament:").Append(shadowDiwata.LamentCooldownRemaining.ToString("F1"));
					}
					else
					{
						var aswang = enemy as AswangUnitAI;
						if (aswang != null)
						{
							textBuilder.Append("  pounce:").Append(aswang.PounceCooldownRemaining.ToString("F1"));
						}
						else
						{
							var aswangQueen = enemy as AswangQueenAI;
							if (aswangQueen != null)
							{
								textBuilder.Append("  pounce:").Append(aswangQueen.PounceCooldownRemaining.ToString("F1"))
									.Append("  swarm:").Append(aswangQueen.SwarmCooldownRemaining.ToString("F1"));
							}
							else
							{
								var manananggal = enemy as ManananggalAI;
								if (manananggal != null)
								{
									textBuilder.Append("  dive:").Append(manananggal.DiveCooldownRemaining.ToString("F1"));
								}
								else
								{
									var tiyanak = enemy as TiyanakAI;
									if (tiyanak != null)
									{
										textBuilder.Append("  lunge:").Append(tiyanak.LungeCooldownRemaining.ToString("F1"));
									}
									else
									{
										var sigbin = enemy as SigbinAI;
										if (sigbin != null)
										{
											textBuilder.Append("  backstep:").Append(sigbin.BackstepCooldownRemaining.ToString("F1"));
											textBuilder.Append("\nspd:").Append(sigbin.DebugMoveSpeed.ToString("F1"));
											textBuilder.Append(" vel:").Append(enemy.DebugVelocityMagnitude.ToString("F1"));
											if (enemy.DebugIsMoveBlocked) textBuilder.Append(" [").Append(enemy.DebugBlockReason).Append("]");
											if (target != null) textBuilder.Append("\ntgt:").Append(tgtPos.ToString("F0"));
											if (sigbin.DebugHasActiveAbility) textBuilder.Append("  [BACKSTEP]");
											if (sigbin.DebugHasBasicRoutine) textBuilder.Append("  [BASIC]");
										}
										else
										{
											var sirena = enemy as SirenaAI;
											if (sirena != null)
											{
												textBuilder.Append("  burst:").Append(sirena.BurstCooldownRemaining.ToString("F1"));
											}
											else
											{
												var busaw = enemy as BusawAI;
												if (busaw != null)
												{
													textBuilder.Append("  grasp:").Append(busaw.GraspCooldownRemaining.ToString("F1"));
												}
												else
												{
													var wakwak = enemy as WakwakAI;
													if (wakwak != null)
													{
														textBuilder.Append("  descent:").Append(wakwak.DescentCooldownRemaining.ToString("F1"));
													}
													else
													{
														var berberoka = enemy as BerberokaAI;
														if (berberoka != null)
														{
															textBuilder.Append("  vortex:").Append(berberoka.VortexCooldownRemaining.ToString("F1"))
																.Append("  flood:").Append(berberoka.FloodCooldownRemaining.ToString("F1"));
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}
			}
		}
		
		tmp.text = textBuilder.ToString();
	}

	private void OnDisable()
	{
		DestroyIfExists();
	}

	private void DestroyIfExists()
	{
		if (tmp != null)
		{
			Destroy(tmp.gameObject);
			tmp = null;
		}
	}
}


