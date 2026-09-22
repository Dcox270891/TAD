# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `C:\Users\Daniel\OneDrive\Desktop\Unity\TAD`
- Last analyzed: 2026-09-22
- Last analyzed commit: `8300c69` (`Initial TAD project with wall prefabs`).
- Purpose: Tower Attack or Defend (TAD), a single-player 2D/2.5D farm tower-defence game.

## Confirmed Environment

- Unity version: 6000.6.2f1 (`770e33f6875c`)
- Render pipeline: Universal Render Pipeline with the 2D Renderer.
- Input system: Unity Input System package is installed and selected.
- Target platforms: Android, iOS, and Windows/Steam; their Editor build-support modules are installed.

## Important Packages And Frameworks

| Area | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Rendering | URP 17.6.0 and a 2D Renderer asset | Confirmed | `Packages/manifest.json`, `Assets/Settings/Renderer2D.asset` |
| Input | Input System 1.20.0 | Confirmed | `Packages/manifest.json`, `ProjectSettings/ProjectSettings.asset` |
| UI | Unity uGUI 2.6.0 | Confirmed | `Packages/manifest.json` |
| Tests | Unity Test Framework 1.8.0 | Confirmed | `Packages/manifest.json` |
| Tile tools | Unity 2D Tilemap and Tilemap Extras | Confirmed | `Packages/manifest.json` |

## Directory Structure

| Path | Purpose | Confidence | Evidence |
| --- | --- | --- | --- |
| `Assets/Scenes` | Game scenes; currently contains the starter scene | Confirmed | `Assets/Scenes/SampleScene.unity` |
| `Assets/Settings` | URP renderer and graphics settings | Confirmed | `Assets/Settings` |
| `Assets/Welcome` | Unity starter/tutorial content | Confirmed | `Assets/Welcome` |
| `Assets/TAD` | First-party TAD gameplay code and reusable prefabs | Confirmed | `Assets/TAD` |

## Assembly Boundaries

- No first-party assemblies exist yet. Runtime code currently uses Unity's default assembly.

## Scenes And Startup Flow

- Build scenes: `Assets/Scenes/SampleScene.unity`.
- Likely startup scene: `Assets/Scenes/SampleScene.unity`.
- Scene loading flow: no custom scene-loading code exists yet.

## Architecture

| Pattern | Finding | Confidence | Evidence |
| --- | --- | --- | --- |
| Environment data | `FarmWall` owns one wall's cover and hole configuration | Confirmed | `Assets/TAD/Scripts/Environment/FarmWall.cs` |
| Food objective | `FoodStore` begins with 200 food, consumes food on attacker arrival, and exposes food-state events | Confirmed | `Assets/TAD/Scripts/Environment/FoodStore.cs` |
| Attacker data | `AttackerDefinition` owns configurable attacker stats; `ChickenDefinition` is the first data asset | Confirmed | `Assets/TAD/Scripts/Attackers/AttackerDefinition.cs` |
| Attacker runtime | `BasicAttacker` moves in its assigned direction, has health, and returns itself to a pool on defeat or food arrival | Confirmed | `Assets/TAD/Scripts/Attackers/BasicAttacker.cs` |
| Reuse | `SpawnPool` has attacker, defender, and projectile prefab inputs, prewarms five objects per configured type, and expands when exhausted | Confirmed | `Assets/TAD/Scripts/Pooling/SpawnPool.cs` |
| Gameplay architecture | Reusable environment, attacker, and pool components are established; waves, defenders, combat rewards, and economy systems are not yet implemented | Confirmed | `Assets/TAD` |
| Data-driven content | Attacker stats are data-driven through `AttackerDefinition`; weapons and other content are not implemented yet | Confirmed | `Assets/TAD/Data/Attackers/ChickenDefinition.asset` |

## Coding Conventions

- First-party code uses the `TAD.<Feature>` namespace style with private serialized fields and public read-only gameplay queries.

## Testing And Validation

- EditMode tests: no project tests found.
- PlayMode tests: no project tests found.
- CI/build validation: no CI configuration found.

## Available Unity Tooling

| Capability | Status | Evidence |
| --- | --- | --- |
| `unity.connection.status` | available | `com.unity.pipeline` 0.7.0-exp.1; Editor connection is ready |
| `unity.editor.version` | available from project metadata | `ProjectSettings/ProjectVersion.txt` |
| `unity.buildsettings.read` | available from serialized project settings | `ProjectSettings/EditorBuildSettings.asset` |
| `unity.package.read` | available from project files | `Packages/manifest.json` |
| `unity.scene.list` | available | Live Pipeline connection |
| `unity.scene.inspect` | available | Live Pipeline connection |
| `unity.console.read` | available | Live Pipeline connection |
| `unity.asset.search` | available | Live Pipeline connection |
| `unity.tests.list` | unverified | No live Pipeline connection |
| `unity.tests.run` | unverified | No live Pipeline connection |
| `unity.playmode.read` | unverified | No live Pipeline connection |
| `unity.profiler.read` | unverified | No live Pipeline connection |

## Important Constraints

- Do not manually edit scene, prefab, or ScriptableObject YAML while a live Editor connection is available.
- Git is configured locally with Git LFS rules for Blender and exported art.
- Do not enable monetization in the prototype; ads and an ad-removal purchase are later-release work.

## Unknowns And Confidence

- The intended low-poly Blender asset import conventions, camera angle, and lighting setup are not established.
- No navigation, defender combat, economy, or upgrade implementation exists yet. `BasicAttacker` currently moves in its authorable starting direction until the navigation system supplies path directions.
- The wall hole is authoring and pathfinding data only for now; its visual opening and split collision geometry will be added with the navigation system.

## Source Files Inspected

- `ProjectSettings/ProjectVersion.txt`
- `ProjectSettings/ProjectSettings.asset`
- `ProjectSettings/EditorBuildSettings.asset`
- `Packages/manifest.json`
- `Packages/packages-lock.json`
- `Assets/Settings/Renderer2D.asset`
- `Assets/TAD/Scripts/Environment/FarmWall.cs`
- `Assets/TAD/Scripts/Environment/FoodStore.cs`
- `Assets/TAD/Scripts/Attackers/AttackerDefinition.cs`
- `Assets/TAD/Scripts/Attackers/BasicAttacker.cs`
- `Assets/TAD/Scripts/Pooling/SpawnPool.cs`
- `Assets/TAD/Scripts/Pooling/PooledInstance.cs`
- `Assets/TAD/Prefabs/Environment/FullCoverWall.prefab`
- `Assets/TAD/Prefabs/Environment/HalfCoverWall.prefab`
- `Assets/TAD/Prefabs/Environment/FoodStore.prefab`
- `Assets/TAD/Data/Attackers/ChickenDefinition.asset`
- `Assets/TAD/Prefabs/Attackers/Chicken.prefab`
- `Assets/TAD/Prefabs/Pooling/SpawnPool.prefab`

<!-- unity-onboarding:generated:end -->
