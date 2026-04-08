# Bakunawa Boss Fight Setup Guide

This guide helps you set up the Bakunawa boss in your TESTING scene.

## Damage Zones (GameObject-Based Damage)

Bakunawa uses **damage zone GameObjects** instead of guessed radii. Each attack spawns a prefab with a trigger Collider—when players enter it, they take damage. You define the hit area by shaping and scaling the prefab.

### Assign Damage Zone Prefabs
In BakunawaAI Inspector, assign:
- **Head Slam Damage Zone Prefab** – bite hitbox (sphere/box in front)
- **Tail Whip Damage Zone Prefab** – tail sweep hitbox
- **Tsunami Roar Damage Zone Prefab** – tidal wave hitbox

A default prefab is at `Assets/Enemies/Bakunawa/DamageZones/BakunawaDamageZone.prefab` (sphere, radius 3). You can:
- Use it for all three (adjust **Zone Offset** and **Zone Scale** per attack)
- Duplicate and customize: change Collider shape (BoxCollider for cones), size, position
- Add a child MeshRenderer (disabled at runtime) for debug visualization

### How It Works
When an attack lands, `SpawnDamageZone()` instantiates the prefab at the boss position + offset, applies your **Zone Scale**, and calls `Initialize(owner, damage, lifetime)`. The zone applies damage on `OnTriggerEnter` (one hit per player), then destroys itself after `lifetime`.

---

## Scene Setup (Model_Bakunawa)

Your Bakunawa has **separate Head, Body, and Tail**—this is ideal for boss fights because:
- Each part can play its own attack animation (head slams, tail whips, body for tsunami)
- More cinematic and readable attacks

### 1. Root GameObject (manual setup)

Use the **root** Bakunawa GameObject (e.g. `Model_Bakunawa`). Add or ensure these components and settings:

| Component / setting | What to do |
|--------------------|-------------|
| **CharacterController** | Add if missing. Set **Radius** = 4, **Height** = 12, **Center** = (0, 6, 0). Adjust to fit the head/neck; the green capsule in Scene view shows the hitbox. |
| **BakunawaAI** | Add if missing. Assign **Enemy Data** (see step 2). |
| **Enemy health** | Handled by `BaseEnemyAI` (health comes from Enemy Data). No separate `EnemyHealth` needed. |
| **PhotonView** | Add if using multiplayer. |
| **PhotonTransformView** | Add if using multiplayer; enable Position and Rotation sync. |
| **Tag** | Set to **Enemy**. |

Optional: reparent the root under a `CENTER` GameObject in your scene if your hierarchy uses one.

### 2. Assign Enemy Data
- In BakunawaAI, set **Enemy Data** = `Assets/Enemies/2STATS/Bakunawa.asset`

### 3. Multi-Part Animators (which controller goes where)

In **BakunawaAI** Inspector, assign:

| BakunawaAI field   | Animator controller to use | Used for                |
|--------------------|-----------------------------|--------------------------|
| **Body Animator**  | `C_Body.controller`         | Tsunami Roar (and Speed if you use it) |
| **Head Animator**  | `C_Head.controller`         | Head Slam, Beam Spit     |
| **Tail Animator**  | `C_Tail.controller`         | Tail Whip                |

Controller files live in `Assets/Enemies/Bakunawa/`. If a field is left empty, the main **Animator** on the root is used for that part.

### 3b. Segment follow (body/tail lag behind head)

So the whole serpent doesn’t turn as one block, the body and tail can **smoothly follow** the head with a delay. In **BakunawaAI** Inspector, under **Segment Follow**:

- **Body Segment** – drag the **Transform** of the body GameObject (the one with the body Animator). Its rotation will lag behind the root (head) when turning.
- **Tail Segment** – drag the **Transform** of the tail GameObject (the one with the tail Animator). Its rotation will lag behind the body (or head if Body Segment is empty).
- **Body Follow Speed** – how fast the body catches up (e.g. 3). Lower = more lag/slither.
- **Tail Follow Speed** – how fast the tail catches up (e.g. 2). Lower = more lag.

Leave Body/Tail Segment empty if you want the old behavior (everything turns together). Works with body/tail as children of the root or with tail as child of body.

### 4. Animator parameters and transitions (manual)

BakunawaAI fires **triggers** with these exact names. Add them in each controller and wire transitions.

#### C_Head.controller (Head Animator)

The head uses **Idle**, **Exhausted** (post-attack), and the two attack states. Flow: **Idle** → (trigger) → **Attack** → **Exhausted** → **Idle**.

1. Open **C_Head** in the Animator window (Window > Animation > Animator; select the GameObject that has this controller).
2. In the **Parameters** panel, add (click **+** > **Trigger**):
   - **HeadSlam**
   - **BeamSpit**
3. Create or name these states and assign clips:
   - **Idle** – your head idle clip. Set as **Default State** (right‑click state → Set as Layer Default State).
   - **Exhausted** – your head exhausted clip (plays after each head attack).
   - **Head Slam** – head slam attack clip.
   - **Beam** (or similar) – beam spit attack clip.
4. Add transitions from **Any State** to each attack state:
   - **Any State** → Head Slam state: condition **HeadSlam** (Trigger).
   - **Any State** → Beam state: condition **BeamSpit** (Trigger).
5. **Attack → Exhausted → Idle:**
   - **Head Slam** state → **Exhausted** state: **Has Exit Time** (e.g. Exit Time = 1), no condition. Uncheck "Fixed Duration" if you want transition at end of clip.
   - **Beam** state → **Exhausted** state: same (exit at end of beam clip).
   - **Exhausted** state → **Idle** state: **Has Exit Time** (e.g. Exit Time = 1) so when the exhausted clip finishes it returns to Idle.

Result: when the AI fires HeadSlam or BeamSpit, the head plays the attack, then exhausted, then idle.

#### C_Tail.controller (Tail Animator)

1. Open **C_Tail** in the Animator window.
2. Add parameter: **TailWhip** (Trigger).
3. Add a state for the tail whip clip (or use the existing motion).
4. **Any State** → tail whip state: condition **TailWhip** (Trigger).
5. Transition from tail whip state back to default/idle when done.

#### C_Body.controller (Body Animator)

Body handles **movement (Idle / Swim)** and **Tsunami Roar**. BaseEnemyAI already sets the **Speed** float on the main animator when the boss moves or stops; use it to blend Idle ↔ Swim.

1. Open **C_Body** in the Animator window.
2. Add parameters:
   - **TsunamiRoar** (Trigger).
   - **Speed** (Float) – used for Idle vs Swim (0 = idle, > 0 = moving).
3. **Idle and Swim (movement):**
   - **Idle** state – your body idle clip. Set as **Default State**.
   - **Swim** state – your swim/moving clip.
   - **Idle** → **Swim**: transition with condition **Speed** Greater than **0.1** (or a small threshold). Uncheck "Has Exit Time" so it reacts immediately when the boss starts moving.
   - **Swim** → **Idle**: condition **Speed** Less than **0.1**. Uncheck "Has Exit Time" so when the boss stops it goes back to Idle.
4. **Tsunami Roar:**
   - Add a state for the tsunami/tidal wave clip.
   - **Any State** → tsunami state: condition **TsunamiRoar** (Trigger).
   - Tsunami state → back to **Idle** (or **Swim** if you prefer) when the clip finishes (Has Exit Time).

Result: when the boss is not moving, body plays Idle; when it moves, it transitions to Swim. Tsunami Roar still interrupts from Any State via the trigger.

**Note:** BaseEnemyAI sets **Speed** on the **root** GameObject’s Animator. If the body mesh is on a child with its own Animator (C_Body), either assign C_Body as the root’s main Animator so it gets Speed, or add a small script that copies `animator.GetFloat("Speed")` to the body animator each frame so the Idle/Swim blend works there.

### 5. Animation clips

Assign your clips to the correct states in each controller.

**C_Head (head-only):**

| State     | Use for            | Suggested clip / note        |
|-----------|--------------------|------------------------------|
| Idle      | Default head pose  | Your head idle clip          |
| Exhausted | After head attacks | Your head exhausted clip     |
| Head Slam | Head slam attack   | e.g. `bakunawa head slam feb`|
| Beam      | Beam spit attack   | e.g. `bakunawa spit anim`    |

**C_Body (movement + tsunami):**

| State        | Use for           | Suggested clip / note     |
|--------------|-------------------|---------------------------|
| Idle         | Not moving       | Your body idle clip       |
| Swim         | Moving           | Your swim animation       |
| Tsunami Roar | Tidal wave attack| e.g. `bakunawa tidal wave`|

**Other controllers:**

| Attack        | Suggested clip / state        |
|---------------|------------------------------|
| Tail Whip     | e.g. `bakunawa tail whip feb` or tail whip clip |

### 6. Hitboxes (Receiving Damage from the Player)

The **CharacterController** capsule is for movement and only covers the head/neck. Player attacks use `Physics.OverlapSphere` and resolve the hit target via `GetComponentInParent<IEnemyDamageable>`, so any collider under the Bakunawa **root** on the **Enemy** layer will route damage to the same boss health (and only once per attack).

To get accurate hitboxes along the whole body:

1. **Add hitbox colliders as children of the root**  
   Create empty GameObjects under `Model_Bakunawa` (or under your existing Head/Body/Tail segment transforms so they move with the model):
   - **Head hitbox** – e.g. under the head segment; use a **BoxCollider** or **CapsuleCollider** that fits the head/neck (you can keep relying on the CharacterController capsule here if it’s enough).
   - **Body hitbox** – under the middle segment; **BoxCollider** or **CapsuleCollider** sized to the torso.
   - **Tail hitbox** – under the tail segment; **BoxCollider** or **CapsuleCollider** along the tail.

2. **Collider settings**
   - **Is Trigger** = false (solid hitboxes work with OverlapSphere).
   - **Layer** = **Enemy** (same as your root, or inherit from parent).  
   No script is needed on these objects; damage is applied to the root that has `BakunawaAI`.

3. **Optional**
   - You can leave the root’s CharacterController as-is (it will still register hits on the head); add the extra colliders only for body and tail.
   - Or add a dedicated “Head” hitbox child and shrink/remove the CharacterController’s capsule if you want the head hitbox to match the mesh more closely (movement may need tuning).

Result: attacks that overlap the head, body, or tail will all register as hitting the same Bakunawa and apply damage once per attack to the boss.

## Moveset

| Attack       | Damage | Range   | Description                      |
|-------------|--------|---------|----------------------------------|
| Head Slam   | 35     | 6m      | Basic bite (cone in front)       |
| Tail Whip   | 45     | 8m cone | Wide sweep behind/side          |
| Tsunami Roar| 40     | 14m cone| Tidal wave AoE in front          |
| Beam Spit   | 30     | Projectile | Fires projectile at player  |

## VFX (Optional)
Assign prefabs in the Inspector:
- **Tidal wave** – `bakunawa tidal wave iMPORVED GRAOHICS.prefab` for Tsunami Roar
- **Beam** – `Bakunawa beam(sfx-vfx).prefab` for Beam Spit

## Beam Projectile (Beam Spit)
The beam prefab contains both VFX and damage zone. Add `EnemyDamageZone` + trigger Collider to the prefab (e.g. on Water_Beam). BakunawaAI initializes it at runtime and spawns the beam.

- **Beam Projectile Prefab** – Beam prefab with VFX + EnemyDamageZone. Drag your BakunawaBeam prefab here.
- **Beam Projectile Resource Path** – For multiplayer: the beam prefab must be in a Resources folder.

**EnemyDamageZone on prefab:** Owner, Damage, Lifetime are set by `Initialize()` at runtime—you don't need to set them in the Inspector. Keep **One Hit Per Player** checked so each player takes damage only once per beam pass.

## Tail Whip (Submerge → Emerge → Tornado → Exhausted)

Tail Whip flow: **Submerge** → **Emerge backwards** (at distance, tail toward players) → **Tail whip** (damage zone + tornado) → **Exhausted** → **End**.

### Animator (Head only)

Add these triggers to the **Head Animator** (C_Head.controller):

- **Submerge** – plays submerge clip (Bakunawa goes into ground)
- **Emerge** – plays emerge clip (Bakunawa rises, already facing away from players)

Wire transitions: **Any State** → Submerge state (condition **Submerge**), Submerge → Emerge (or a short wait), Emerge → Idle.

### Damage Zone (DZ_TailWhip)

Assign **Tail Whip Damage Zone Prefab** (e.g. `DZ_TailWhip`). This spawns when the tail whip happens (right after emerging backwards), deals damage to players in the zone, then despawns. The tornado spawns immediately after and does its own tick damage.

### Tornado Prefab

1. Create or use a tornado prefab (e.g. `BAKUNAWA TORNADO.prefab` or `Projectile_Tornado`).
2. Add **TornadoFollower** to the root (handles movement toward players).
3. Add **EnemyDamageZone** + a **trigger Collider** (SphereCollider, BoxCollider, etc.) on the same GameObject or a child. Size the collider to control the damage area—BakunawaAI sets damage and lifetime at runtime; EnemyDamageZone uses tick mode (damage every N seconds while in zone).
4. For multiplayer: place the prefab in a **Resources** folder (e.g. `Resources/Enemies/Bakunawa/`) and set **Tail Whip Tornado Resource Path** in BakunawaAI to `Enemies/Bakunawa/BAKUNAWA TORNADO` (no `.prefab`).

### BakunawaAI Inspector

- **Tail Whip Windup** – seconds to wait after emerging before damage zone and tornado spawn (e.g. 0.5)
- **Tail Whip Damage Zone Damage** – damage for the tail whip hit (e.g. 45)
- **Tail Whip Tornado Damage** – damage per tick for the tornado (e.g. 10)
- **Tail Whip Submerge Trigger** – animator trigger name (default `Submerge`)
- **Tail Whip Emerge Trigger** – animator trigger name (default `Emerge`)
- **Tail Whip Submerge Time** – seconds for body/tail to move down (e.g. 1.2)
- **Tail Whip Submerge Depth** – how far down body and tail go (e.g. 8)
- **Tail Whip Stay Submerged Time** – seconds Bakunawa stays submerged before emerging (e.g. 1)
- **Tail Whip Emerge Time** – seconds for body/tail to move back up (e.g. 1)
- **Tail Whip Emerge Distance** – distance from player when emerging backwards (e.g. 6)
- **Tail Whip Exhausted Time** – seconds exhausted after tornado (e.g. 1.5)
- **Tail Whip Tornado Prefab** – assign for single-player; leave empty if using Resource Path
- **Tail Whip Tornado Resource Path** – for Photon (e.g. `Enemies/Bakunawa/BAKUNAWA TORNADO`)
- **Tail Whip Tornado Speed** – how fast tornado moves toward players (e.g. 3)
- **Tail Whip Tornado Lifetime** – how long tornado lasts (e.g. 5)
- **Tail Whip Tornado Tick Interval** – seconds between damage ticks (e.g. 0.4)

**Tail Whip VFX:**
- **Tail Whip Submerge VFX** – spawned when Bakunawa submerges (dives down)
- **Tail Whip Submerge VFX Offset** – offset from submerge position
- **Tail Whip Emergence Indicator VFX** – telegraph at emerge position before Bakunawa surfaces (spawned during stay submerged)
- **Tail Whip Emergence Indicator Offset** – offset from emerge position
- **Tail Whip Emerge VFX** – spawned when Bakunawa emerges
- **Tail Whip Emerge VFX Offset** – offset from emerge position
- **Tail Whip Windup VFX** – when tail whip windup starts
- **Tail Whip Impact VFX** – when damage zone spawns
- **Tail Whip VFX Offset** – used for windup and impact

## Boss Health Bar (Minecraft Wither Style)

A screen-space boss bar at the top of the screen, visible to all players when Bakunawa is active.

### Setup

1. **Create the UI hierarchy** (in your HUD Canvas or a dedicated boss bar Canvas):
   - Add an empty GameObject (e.g. `BossHealthBar`) as a child of your **Screen Space - Overlay** Canvas.
   - Position it at the top center: Anchor = Top Center, Pivot = (0.5, 1), Y offset ≈ -40.

2. **Add the bar background**:
   - Create a child **Image** (e.g. `Background`). Set a dark color (e.g. black/grey), full rect stretch.
   - Size: ~600×30 or similar.

3. **Add the fill Image**:
   - Create a child of Background: **Image** named `Fill`. Same rect as parent.
   - Set **Image Type** = **Filled**, **Fill Method** = **Horizontal**, **Fill Origin** = **Left**.
   - Set **Color** to red (or your health bar color).

4. **Optional text**:
   - Add **Text** or **TextMeshPro - Text** for the boss name (e.g. "Bakunawa") above the bar.
   - Add another for health numbers (e.g. "450 / 500") below or beside the bar.

5. **Add BossHealthBarUI**:
   - Add the `BossHealthBarUI` component to the root `BossHealthBar` GameObject.
   - Assign **Fill Image** = the Fill Image.
   - Assign **Name Text Object** and **Health Text Object** if you added them (the GameObject with Text/TMP component).
   - Set **Root Object** = the root (or leave null to use this GameObject).
   - Set **Boss Display Name** = "Bakunawa".
   - Leave **Boss** empty to auto-find `BakunawaAI` in the scene.

6. **Multiplayer**: The bar is on each player's HUD Canvas, so it shows for all players. Ensure the boss bar is part of the shared HUD (e.g. under the main game HUD Canvas, not per-player).

### Prefab (optional)

Save `BossHealthBar` as a prefab and add it to your TESTING (or boss) scene's HUD Canvas so it appears when the boss fight loads.

## Swim Splash VFX

Splash GameObjects under Head, Body, and Tail are enabled when the Bakunawa is moving and disabled when idle.

### Setup

1. **Add Splash objects** – Place your splash VFX as children of Head, Body, and Tail in the hierarchy (they move with each segment).

2. **Assign in BakunawaAI Inspector**:
   - **Swim Splash Head** – drag the Splash under Head
   - **Swim Splash Body** – drag the Splash under Body
   - **Swim Splash Tail** – drag the Splash under Tail
   - **Swim Splash Min Speed** – minimum movement speed to show splash (default 0.5)

3. **Behavior** – Enabled when moving; disabled when idle or exhausted.

## Testing
1. Enter Play mode in the TESTING scene
2. Move the player near the Bakunawa
3. It should detect, move, and cycle through Head Slam, Tail Whip, Tsunami Roar, and Beam Spit based on distance
4. The boss health bar should appear at the top when Bakunawa is active and hide shortly after it dies
