# Fase 1 — Bottom Left Quadrant Blockout

- Playable extent: 64 × 42 Unity units on the XY plane; gameplay Z remains 0.
- Start: protected room at the bottom-left, with the single active `PlayerStart_BottomLeft` at `(4, 1.05, 0)`.
- Center connection: `CenterObjectiveEntrance_TopRight` at `(63.5, 36, 0)` through the opening in the right boundary.
- Main route: 17 staged one-way platforms plus solid combat shelves. The marked west ascent alternates 1.5-1.7-unit rises after the measured 0.1-unit corrections.
- Legendary deviations: gem room at the top-left and chest room at the bottom-right; both have a separate return to the main route.
- Lower chest access: `LowerRouteGate_Start` and `LegendaryChestAccessGate` close the flat outer-floor shortcut. Each is 3.25 units tall, joined to the floor, and has an existing approach platform 1.5 units below its top.
- Future loot and hazard objects are visual-only markers without colliders, rigidbodies, or gameplay scripts. Hazard markers use vertical orange beacons so they cannot be mistaken for playable platforms.
- The reusable prefab intentionally contains no players or camera.
- The materialized scene contains one `Player1_Qusap`, keeps its `Player1Keyboard` input slot, and uses the existing single-target `QusapCameraFollow` component.
- Geometry count: 45 solid collider blocks and 42 instances of `QusapOneWayPlatform.prefab`.

## Future reward markers

- Common: `(23, 5.65)`, `(14, 15.15)`, `(42, 13.65)`, `(47, 30)`.
- Rare: `(10, 24.8)`, `(33, 16.9)`, `(37, 26.55)`, `(57.5, 12.6)`.
- Legendary: top-left gem `(5, 32.2)` and bottom-right chest `(59, 1.6)`.

## Copy preparation

Create the other quadrants from the reusable hierarchy without negative parent scale. Reorient every one-way platform explicitly so its pass-through direction remains correct. The upper quadrants need route and spacing adjustments: gravity makes their outward/downward traversal easier than the bottom quadrants' inward/upward traversal.

## Play Mode validation still required

With the scene open, press Play and use the keyboard player to sample solid and one-way platforms in the lower, middle, and upper routes. Then measure the target route times, traverse every reconnect, and verify the optional dash/wall-jump shortcuts before treating the blockout as tuned.
