# AI Game Developer MCP Skills

Skills for the Unity MCP server (`ai-game-developer` at localhost:57989). Use when automating Unity Editor tasks.

## Most Used

| Skill | Purpose |
|-------|---------|
| `gameobject-find` | Find GameObject by name/ID in scene or opened prefab |
| `gameobject-create` | Create GameObject (with optional primitive type) |
| `gameobject-set-parent` | Reparent a GameObject |
| `gameobject-component-add` | Add component to GameObject |
| `gameobject-component-modify` | Change component properties |
| `scene-open` | Open scene (use `assets-find` first to get path) |
| `scene-save` | Save current scene |
| `script-read` | Read C# script file |
| `script-update-or-create` | Create or overwrite script with C# code |
| `assets-find` | Find asset by name/guid |
| `assets-prefab-open` | Open prefab for editing |
| `assets-prefab-save` | Save opened prefab |
| `editor-selection-set` | Select GameObject in hierarchy |

## By Category

**GameObject:** gameobject-find, gameobject-create, gameobject-destroy, gameobject-duplicate, gameobject-modify, gameobject-set-parent

**Components:** gameobject-component-add, gameobject-component-destroy, gameobject-component-get, gameobject-component-list-all, gameobject-component-modify

**Scenes:** scene-create, scene-get-data, scene-list-opened, scene-open, scene-save, scene-set-active, scene-unload

**Assets:** assets-copy, assets-create-folder, assets-delete, assets-find, assets-find-built-in, assets-get-data, assets-material-create, assets-modify, assets-move, assets-prefab-close, assets-prefab-create, assets-prefab-instantiate, assets-prefab-open, assets-prefab-save, assets-refresh, assets-shader-list-all

**Scripts:** script-delete, script-execute, script-read, script-update-or-create

**Editor:** editor-application-get-state, editor-application-set-state, editor-selection-get, editor-selection-set

**Other:** console-get-logs, object-get-data, object-modify, package-add, package-list, package-remove, package-search, reflection-method-call, reflection-method-find, screenshot-camera, screenshot-game-view, screenshot-scene-view, tests-run, type-get-json-schema

## Usage

Each skill has full docs in its folder. MCP server must be running (Unity Editor with AI Game Developer plugin). Tools are called via HTTP at `http://localhost:57989/api/tools/<tool-name>`.
