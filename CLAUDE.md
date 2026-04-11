# CLAUDE.md — AlbuRIOT

## Project
Co-op Unity survival RPG inspired by Philippine mythology.  
Core stack: Unity (C#), Photon PUN 2, TextMeshPro, Unity Input System, ScriptableObjects + JSON-driven content.

**Instruction policy:** keep this file as the canonical repository guidance; keep smaller agent/rule files as pointers or condensed summaries.

## CRITICAL RULES — Never Violate
1. **Never edit generated/vendor paths** unless explicitly asked: `Library/`, `Temp/`, `obj/`, `bin/`, `Packages/`, `Assets/Photon/`, `Assets/TextMesh Pro/`, `Assets/Imported Assets/`, `*.meta`
2. **No scene/prefab guessing** — when a change needs inspector assignment, prefab wiring, or scene hierarchy setup, provide explicit Unity setup steps
3. **Photon ownership must be respected** — only owning clients (`photonView.IsMine`) mutate owned state; non-owners are read-only
4. **Network instantiate/destroy correctly** — use `PhotonNetwork.Instantiate()` / `PhotonNetwork.Destroy()` in-room, never plain `Instantiate/Destroy` for networked entities
5. **Master-authoritative shared systems** — AI and shared world/quest authority run on `PhotonNetwork.IsMasterClient` (or offline)
6. **No `FindWithTag("Player")` for gameplay logic** — use `PlayerRegistry.All` and `PlayerRegistry.GetLocalPlayerTransform()`
7. **Use centralized spawn flow** — do not duplicate spawn logic outside `PlayerSpawnCoordinator`
8. **No broad refactors or renames by default** — keep edits minimal and localized unless requested

## Quick Start
```bash
# Unity project root
# Open project in Unity Hub, then use one of:
npm test                 # if JS tooling/tests are part of requested task
npm run lint             # repository JS/TS lint path if requested
```

## Architecture (Unity Gameplay)
- **Managers** orchestrate core loops (`GameManager`, `NetworkManager`, `QuestManager`, `CutsceneManager`, loaders/transitions)
- **Player systems** are per-player instances (`PlayerStats`, `PlayerCombat`, `Inventory`, `EquipmentManager`, UI relays)
- **Enemy systems** use base AI + creature-specific scripts, with master-client authority
- **Data layer** is ScriptableObject-first with JSON loaders for quest/trade definitions
- **UI flow** relies on local ownership and lock systems (`LocalInputLocker`, `LocalUIManager`)

## Key Files And Directories
| Path | Purpose |
|------|---------|
| `Assets/Scripts/Managers/` | Core orchestration, networking, cutscenes, transitions |
| `Assets/Scripts/Player/` | Player control, stats, combat/UI relays |
| `Assets/Scripts/Combat/` | Damage/status relays, abilities, progression |
| `Assets/Scripts/Enemies/` + `Assets/Enemies/` | Base AI, BT nodes, per-creature logic |
| `Assets/Scripts/Map/` | Procedural terrain/resources, spawn markers |
| `Assets/Scripts/Inventory/` + `Assets/Scripts/Equipments/` | Inventory/equipment runtime systems |
| `Assets/Scripts/UI/` | Player-facing gameplay UI and overlays |
| `Assets/Resources/` | Runtime-loaded data (ScriptableObjects/JSON dependencies) |

## Photon Multiplayer Rules
- Gate state mutation by ownership (`photonView.IsMine`) and authority (`PhotonNetwork.IsMasterClient`) where applicable
- Prefer RPC-based synchronization for gameplay events; avoid unsynchronized local-only mutations for shared state
- Ensure local-only control of local player movement/input/camera
- Prevent duplicate local spawns by verifying existing local player presence first
- Be careful with buffered RPC usage to avoid stale state across scene transitions

## Spawn And Transition Rules
- Single source of truth: `Assets/Scripts/Managers/PlayerSpawnCoordinator.cs`
- Required callers: `MapTransitionManager`, `ProceduralMapLoader`, `CutsceneManager` scene-entry setup
- Use `EnsureLocalPlayerAtSpawn(...)` for scene-entry placement
- Marker naming convention: `SpawnMarker_#`
- Fallback order:
  1. `PlayerSpawnManager.nextSpawnPosition`
  2. scene `SpawnMarker_*`
  3. `MapResourcesGenerator.GetSharedSpawnPositions()`
  4. `TutorialSpawnManager.spawnPoints`
  5. `NetworkManager.spawnPoints`
  6. terrain center fallback

## Quest / Inventory / Equipment Invariants
- `QuestManager` remains authoritative for shared progression; `Collect` and `TalkTo` are per-player completion gates
- Avoid manual inventory event hooks outside `QuestManager.EnsurePlayerInventory()` to prevent duplicate subscriptions
- Preserve scene transition caches (`Inventory.CacheLocalInventory()`, `EquipmentManager.CacheLocalEquipment()`) when changing load/spawn flow

## Enemy AI Rules
- Base AI contracts (`isBusy`, `BeginAction()`, `EndAction()`, coroutine-based attacks) must remain consistent
- Guard AI `Update` logic behind master-authority checks in multiplayer sessions
- Keep per-creature AI scripts in creature folders; shared behavior belongs in common/base systems

## Unity Workflow
- Do not create editor tooling or custom inspectors unless explicitly requested
- Do not create new documentation files unless explicitly requested
- If a change requires inspector wiring, list exact GameObjects/components/field assignments in a short setup checklist

## Validation Checklist
For gameplay/network/transition changes, verify:
- project compiles cleanly in Unity
- host/client both complete transition correctly
- local player spawns exactly once at expected location
- input/camera control is restored after cutscenes/transitions
- quest progress behavior remains correct for both shared and per-player objectives
- inventory/equipment UI updates occur without duplicate event subscriptions
