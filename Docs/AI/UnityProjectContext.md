# Unity Project Context

<!-- unity-onboarding:generated:start -->

## Project Summary

- Project root: `C:\Users\Daniel\OneDrive\Desktop\Unity\TAD`
- Last audited: 2026-09-30
- Purpose: **Tower Attack or Defend (TAD)**, a single-player, top-down 2D/2.5D farm tower-defence game. The player chooses the attacker or defender role; the other role is AI-controlled.

## Confirmed Environment

- Unity `6000.6.2f1` with URP's 2D Renderer.
- Unity Input System and uGUI are used for desktop, controller, Android, and iOS input/UI.
- Supported build targets are Android, iOS, and Windows/Steam.
- First-party runtime code is isolated in `Assets/TAD/Scripts/TAD.Runtime.asmdef`.

## Project Layout

| Path | Purpose |
| --- | --- |
| `Assets/Scenes/MainMenu.unity` | Main entry scene and role/level selection UI. |
| `Assets/Scenes/Levels/Level1.unity`–`Level3.unity` | The three playable farm maps. |
| `Assets/TAD/Data` | Data-driven attacker, weapon/projectile, visual, and level definitions. |
| `Assets/TAD/Prefabs` | Reusable attackers, farmhands, projectiles, environment, UI, pooling, navigation, and wave prefabs. |
| `Assets/TAD/Scripts` | Runtime gameplay, UI, AI, navigation, economy, pooling, and progression code. |
| `Assets/TAD/Editor` | Asset-generation and Scene-view authoring tools. |
| `Assets/TAD/Tests` | EditMode and PlayMode test assemblies. |

`MainMenu` is the first build scene. `LevelCatalog` exposes Level 1–3 to the UI. `SampleScene` remains only as Unity's configured new-scene template; it is deliberately excluded from player builds and is not part of the level catalogue.

## Current Gameplay Architecture

| Area | Current implementation |
| --- | --- |
| Attacker content | Twelve data-driven animal definitions: three tiers each of Basic, Fast, Strong, and Flying archetypes. They include combat rating, send cost/size, income, kill reward, experience, size, speed, health, and visual data. |
| Defender content | Farmhands start unarmed, gain experience, level up to 10, choose free Stick/Stone branches at level 2, paid evolutions at level 5, and final upgrades at level 8. Weapon, projectile, reload, status effect, and targeting data are reusable definitions. |
| Combat | Pooled projectiles support melee, ranged, rolling, trap, bleed, poison, stun, slow, piercing, bounce, area effects, reloads, and flying-target rules. |
| Navigation | `FarmRouteMapper` builds camera-bounded, size-aware fastest paths around solid cover. Flying attackers may cross half cover; terrain tiles contribute mud/ice route costs while flying animals ignore terrain effects. |
| Environment | Full and half cover, food barn/food depletion visuals, grass, woodland spawn backdrop, props, mud, and ice are reusable prefabs. Colliders follow the visible sprite footprint. |
| Waves and roles | Preparation/day cycle, attacker queue/income, pooled spawns, role-specific UI, restart role selection, attacker AI, and defender AI are in place. |
| Economy and progression | Separate attacker/defender banks, kill rewards, assist/kill experience, per-match experience ledger, saved player profile/settings, and player-level progression are implemented. |
| UI | Attacker/defender/combined HUDs, details panels, health/experience bars, settings, loading, pause, win/loss, and thin-wood farm styling are present. |

## Testing And Validation

- `Assets/TAD/Tests/Editor/TAD.Tests.Editor.asmdef` covers data, economy, combat, pool reset, placement, balance, visuals, and progression checks.
- `Assets/TAD/Tests/PlayMode/TAD.Tests.PlayMode.asmdef` covers gameplay flows including food damage, waves, UI, scene transitions, roles, AI, and pause/restart behaviour.
- Run Unity's EditMode and PlayMode suites before every push, then verify Main Menu → each catalogued level in a Game-view play test.

## Important Constraints

- Do not hand-edit scene, prefab, ScriptableObject, or `.meta` YAML while Unity is open; use Unity APIs, editor tools, or the editor UI.
- Preserve Unity GUID metadata alongside imported assets and source scripts.
- The project uses Git LFS rules for Blender and exported art assets.
- The prototype has no monetization. Ads and in-app purchases are later-release work.
- Before every Git push, capture a real Game-view screenshot and add it with a concise note to the TAD development catalogue.

<!-- unity-onboarding:generated:end -->
