# TAD development catalogue

## 2026-10-22 — grass-bounded attacker and defender movement

![Level 1 Game-view capture of the authored grass play field](DevelopmentEvidence/2026-10-22-grass-bounded-movement-level1.png)

The authored grass background is now the authoritative playable footprint. Ground attackers and relocating farmhands are clamped and routed within that grass area, so neither can travel into the surrounding presentation background. The food barn may remain visually beyond the grass edge: attackers finish at the last legal grass cell and then resolve their arrival with the Food Store, rather than walking through the barn artwork.

To keep the paths reliable for the whole roster, attacker display size is now separate from route-clearance diameter. Each animal keeps its intended visual scale while using a conservative physical footprint for navigation, including the largest animals. Level 1–4 layouts were not edited during this work.

Validation completed before publication:

- Focused game-basics validation: 40/40 Play Mode tests passed.
- Full Game Basics Edit Mode: 124/124 tests passed.
- Full Game Basics Play Mode: 53/53 tests passed, including the four shipped scenes, grass bounds, barn-edge arrival, pooling, day-cycle, targeting, and farmhand relocation presentation.

## 2026-10-08 — automatic farmhand identities

![Level 2 Game-view capture of pooled farmhands with automatic identities](DevelopmentEvidence/defender-appearance-randomization-2026-10-08.png)

Farmhands now receive an automatic identity whenever they are taken from the defender pool. Each placement combines one of 27 authored visual profiles (three skin tones, three hair/hat choices, and three outfits) with a randomly selected farmhand name. This is fully automatic—players do not choose cosmetics—and the identity remains intact when a defender relocates.

Validation completed before publication:

- Live Level 2 Play Mode: frames advanced normally; three pooled farmhands appeared with distinct names and visual profiles.
- Unity Game Basics Edit Mode: 83/83 tests passed.
- Unity Game Basics Play Mode: 42/42 tests passed.
- The match-experience presentation test now isolates pending upgrade-card state, so a tester's saved account data cannot affect the result.

## 2026-09-30 — verified codebase cleanup

![Level 1 Game-view capture after cleanup](DevelopmentEvidence/codebase-cleanup-level1-2026-09-30.png)

The production Level 1 scene was captured in Unity Play Mode after the cleanup pass. Deprecated compatibility paths, unused starter packages, unreferenced authoring by-products, and the non-production SampleScene build entry were removed while the active game, menus, levels, art pipeline, and test coverage were retained.

Validation completed before publication:

- Unity compilation: clean, with no Console errors or warnings.
- Edit Mode: 43/43 tests passed.
- Play Mode: 33/33 tests passed, including pooling, large-wave, long-game, routing, combat, UI, progression, and scene-transition coverage.
