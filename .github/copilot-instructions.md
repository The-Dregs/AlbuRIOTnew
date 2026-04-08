# Copilot Instructions for AlbuRIOT

Last audited: 2026-04-05

## Project Overview
AlbuRIOT is a Photon PUN 2 co-op survival game (up to 4 players) with procedural maps, quest progression, combat abilities, and mythology-themed enemies.

Core pillars:
- multiplayer-safe player lifecycle and scene transitions
- procedural map generation and spawn marker placement
- quest + inventory integration with mixed shared/per-player objective authority
- combat, movesets, debuffs, and enemy AI

## Non-Negotiable Development Rules
- read the full target script before editing
- keep all gameplay changes multiplayer-safe (`PhotonView.IsMine`, master authority where required)
- avoid duplicate spawn/teleport logic; use shared coordinator flows
- preserve event subscription safety patterns (especially inventory and quest systems)
- keep comments short, lowercase, and only where clarity is needed
- when a code change requires Unity setup, include concrete inspector/prefab assignment steps

## Verified Codebase Map
Primary script root: `Assets/Scripts/`

Top-level folders currently in use:
- `Camera/`
- `Combat/`
- `Data/`
- `Editor/`
- `Encyclopedia/`
- `Enemies/`
- `Equipments/`
- `Inventory/`
- `Items/`
- `Managers/`
- `Map/`
- `NPC/`
- `Player/`
- `UI/`
- `VFX/`

Important path corrections:
- `ItemManager` is in `Assets/Scripts/Items/ItemManager.cs` (not `Managers/`)
- `PlayerCombat` is in `Assets/Scripts/Combat/PlayerCombat.cs` (not `Player/`)
- there is no `NunoManager` class in this repo
- `NunoShopManager` exists in `Assets/Scripts/NPC/NunoShopManager.cs`

## Core Multiplayer And Authority Conventions
- only local-owned player objects should be moved/input-controlled (`PhotonView.IsMine`)
- master remains authoritative for shared quest/transition states
- avoid stale buffered state across scenes; use buffered RPCs intentionally
- prefer `PlayerRegistry.All` for player iteration
- use `PlayerRegistry.GetLocalPlayerTransform()` to resolve local player references

## Spawn And Transition Architecture
Single source of truth for spawn placement:
- `Assets/Scripts/Managers/PlayerSpawnCoordinator.cs`

Primary transition callers that must route through coordinator logic:
- `Assets/Scripts/Managers/MapTransitionManager.cs`
- `Assets/Scripts/Managers/ProceduralMapLoader.cs`
- `Assets/Scripts/Managers/CutsceneManager.cs` (player setup flow)

Placement behavior to preserve:
1. `PlayerSpawnManager.nextSpawnPosition`
2. scene `SpawnMarker_*`
3. shared generated positions (`MapResourcesGenerator.GetSharedSpawnPositions()`)
4. `TutorialSpawnManager.spawnPoints`
5. `NetworkManager.spawnPoints`
6. terrain/default fallback

Rules:
- do not add map-specific spawn handlers
- do not duplicate spawn selection and teleport rules in transition scripts
- keep `SpawnMarker_#` naming deterministic

## Inventory, Equipment, And Quest Contract
Critical files:
- `Assets/Scripts/Inventory/Inventory.cs`
- `Assets/Scripts/Equipments/EquipmentManager.cs`
- `Assets/Scripts/Managers/QuestManager.cs`
- `Assets/Scripts/Items/ItemPickup.cs`
- `Assets/Scripts/Items/NetworkItemPickup.cs`

Required patterns:
- inventory is per-player and synchronized (`MonoBehaviourPun`, `IPunObservable`)
- use `Inventory.FindLocalInventory()` before fallback discovery
- `QuestManager.EnsurePlayerInventory()` owns inventory event wiring
- do not manually subscribe quest logic to inventory events outside `EnsurePlayerInventory()`
- preserve `lastInventoryRef` subscribe/unsubscribe guard behavior
- preserve backup reconciliation flow via `ScheduleInventoryReconcile()`
- before scene transition, cache local inventory/equipment and restore after spawn

## Singletons And Lifecycle Notes
Common persistent singletons (`Instance` + `DontDestroyOnLoad`) include:
- `QuestManager`
- `GameManager`
- `SceneLoader` (when `persist` is enabled)
- `NetworkManager`
- `MapTransitionManager`
- `ProceduralMapLoader`
- `MemoryCleanupManager`
- `MultiplayerScalingManager`
- `AlbuRIOTIntegrationManager`
- `ItemManager`
- `EncyclopediaManager`
- `NunoShopManager`
- `LocalUIManager`
- `LocalInputLocker`
- `ScreenFader`
- `NunoDialogueBarUI`

Scene-level or non-singleton reminders:
- `DialogueManager` is not a global singleton
- `LoadingScreenManager` uses `DontDestroyOnLoad` but has no `Instance` property
- `FirstMapLoadingScreen` is scene-scoped despite having static access

## Major Systems Quick Reference
- combat/damage routing:
  - `Assets/Scripts/Combat/DamageRelay.cs`
  - `Assets/Scripts/Combat/EnemyDamageRelay.cs`
  - `Assets/Scripts/Combat/StatusEffectRelay.cs`
- abilities and movesets:
  - `Assets/Scripts/Combat/Abilities/AbilityBase.cs`
  - `Assets/Scripts/Managers/MovesetManager.cs`
  - `Assets/Scripts/Items/MovesetData.cs`
  - `Assets/Scripts/Items/SpecialMoveData.cs`
- enemies and spawning:
  - `Assets/Scripts/Enemies/BaseEnemyAI.cs`
  - `Assets/Scripts/Managers/EnemyManager.cs`
  - `Assets/Scripts/Managers/MapEnemyDirector.cs`
  - `Assets/Scripts/Managers/PerlinEnemySpawner.cs`
  - `Assets/Scripts/Managers/EnemyCampEncounter.cs`
- map generation:
  - `Assets/Scripts/Map/TerrainGenerator.cs`
  - `Assets/Scripts/Map/MapResourcesGenerator.cs`
- npc/shop/dialogue:
  - `Assets/Scripts/NPC/NPCDialogueManager.cs`
  - `Assets/Scripts/NPC/NunoShopManager.cs`
  - `Assets/Scripts/Managers/DialogueManager.cs`

## MapResourcesGenerator Safety Rules
- avoid `PhotonNetwork.Destroy` for scene/local PhotonViews (`ViewID <= 0`)
- non-owner clients should not network-destroy generated objects
- clear/reconcile `SpawnMarker_*` safely when replaying buffered generation state
- keep deterministic naming for generated guide/spawn objects

## Input And UI Locking Rules
- use `LocalInputLocker.Ensure()` with named lock owners
- use `LocalUIManager.Ensure()` for exclusive panel ownership
- after transitions/cutscenes, release stale locks and force gameplay cursor state

## Documentation Alignment
Prefer references that exist in this repo:
- `Assets/docs/CHAPTER 1 - 3.md`
- `Assets/docs/01_GAME_DESIGN.md`
- `Assets/docs/02_TECHNICAL_DOCUMENTATION.md`
- `Assets/docs/03_SETUP_GUIDES.md`
- `Assets/docs/TESTING.md`

Do not reference removed or missing docs (for example, root `storyline.txt` or `alburiot.txt` unless they are reintroduced).

## Validation Checklist For Gameplay/Transition Changes
- no compile errors in Unity/Problems panel
- host and client both complete scene transition correctly
- local player is spawned exactly once and placed at expected marker
- loading UI shows and hides at correct lifecycle points
- input/camera control is restored after transition/cutscene
- inventory events fire once (no duplicate subscriptions)
- collect/talk objectives remain per-player and gated correctly in multiplayer

## Maintenance Rule For Instructions
When major systems are added, moved, or removed, update this file and keep key path references accurate before merging.
