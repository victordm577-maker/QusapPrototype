# Qusap Air Combat Mechanics

## Inspection baseline (before implementation)

- Combat state machine: `QusapCombatController`, one fixed-update state machine with `Idle`, `Startup`, `Active`, and `Recovery`.
- Input: `QusapInputReader` consumes the existing `Gameplay/WeakKick`, `Gameplay/StrongKick`, and `Gameplay/Headbutt` actions once per fixed tick. Bindings remain unchanged:
  - Player 1: `J`, `K`, `L`.
  - Player 2: gamepad West, North, East.
- Ground source: `QusapGroundSensor.IsGrounded`, based on existing collision contacts and `minimumGroundNormalY`; no new ground query is added.
- Dash source: `QusapDashMotor.IsDashing`. Combat already rejected attack startup during Dash. The motor now also observes the combat controller's read-only dive lock.
- Wall slide/jump: `QusapWallSensor`, `QusapVerticalMotor.IsWallSliding`, and `WallJumpSequence`. Air combat does not query or change them.
- Hitbox/hurtbox: one `QusapAttackHitbox` per player uses `Physics.OverlapBox`; `QusapHurtbox` resolves the reliable owning `QusapHitReceiver`. The per-window target set already prevents repeat hits.
- Hitstun: `QusapHitstunController.EnterHitstun` calls `CancelAttack`, resets Dash, clears buffered input, and temporarily disables the movement motors.
- Respawn: `QusapRespawnController` reset hitstun/Dash and Rigidbody state. It now also cancels combat, reloads the dive, and clears accumulated damage.
- Scene/prefab: `CombatPlayground.unity` is the functional 1v1 scene with a `QusapCombatArenaController` referencing two instances of `QusapCombatPlayer.prefab`.
- Scene overrides: `MovementPlayground.unity` overrides the ground headbutt to match the combat prefab values below. The air-combat tool identifies a 1v1 through component references, not GameObject names.
- Before this change, attacks were not gated by Grounded and therefore the same terrestrial definition could start in the air.
- Before this change, there was no damage/health statistic, hitstop, cooldown, death state, or attack ScriptableObject. Damage is now carried by air definitions/hit info and accumulated read-only by the receiver; no health/death policy was invented.

## Preserved terrestrial values

| Attack | Startup | Active | Recovery | Hitbox size | Hitbox offset | Depth | H knockback | V knockback | Hitstun | Lock X |
|---|---:|---:|---:|---|---|---:|---:|---:|---:|---|
| WeakKickGround | 0.08 | 0.08 | 0.14 | (1.00, 0.60) | (0.75, -0.35) | 1.00 | 4.00 | 1.00 | 0.12 | No |
| StrongKickGround | 0.18 | 0.10 | 0.32 | (1.35, 0.75) | (0.90, -0.25) | 1.00 | 7.00 | 3.00 | 0.24 | Yes |
| HeadbuttGround | 0.35 | 0.08 | 0.50 | (1.20, 0.80) | (0.80, 0.45) | 1.00 | 16.50 | 4.00 | 0.40 | Yes |

These are asserted directly against `QusapCombatPlayer.prefab` by EditMode tests. The legacy system had no damage field, so its effective terrestrial damage remains zero.

## Initial air values

| Attack | Startup | Active/max | Recovery | Landing recovery | Damage | Hitbox size | Hitbox offset | Depth | H knockback | V knockback | Hitstun | X retention |
|---|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---:|
| WeakKickAir | 0.07 | 0.07 | 0.12 | 0.08 | 4 | (1.00, 0.55) | (0.75, 0.00) | 1.00 | 3.50 | 0.75 | 0.10 | 1.00 |
| StrongKickAir | 0.16 | 0.10 | 0.30 | 0.20 | 9 | (1.35, 0.75) | (0.90, -0.45) | 1.00 | 7.50 | -2.25 | 0.24 | 0.92 |
| DiveHeadbuttAir | 0.12 | 0.65 max | 0.14 | 0.42 | 12 | (0.90, 0.95) | (0.00, -0.85) | 1.00 | 2.00 | -9.00 | 0.35 | 1.00 |

Dive-only defaults: brake `0.06 s`, vertical brake multiplier `0.25`, downward speed `13`, horizontal control multiplier `0.25`, bounce speed `5.5`, and Dash blocked while the dive/recovery owns the action. All hitboxes use the existing box shape. Hitstop and cooldown are absent because the current architecture has neither system.

## Signals for future animation

`QusapCombatController` exposes `CurrentAttackVariant`, `CurrentPhase`, `AttackVariantStarted`, `AttackPhaseChanged`, and `AttackEnded(variant, canceled)`. Variants are `None`, `WeakKickGround`, `WeakKickAir`, `StrongKickGround`, `StrongKickAir`, `HeadbuttGround`, and `DiveHeadbuttAir`. No Animator parameter, controller, clip, FBX, model, or root motion was changed.

## Manual test scene

Run `Tools > Qusap > Create Air Combat Mechanics Test` while Unity is not in Play Mode. The tool does not run automatically. It finds the most recently modified functional 1v1 scene through `QusapCombatArenaController` references, copies it to `Assets/_Qusap/Scenes/Qusap_AirCombatMechanicsTest_v1.unity`, installs only inline air-definition overrides, adds a debug HUD to that copy, saves, and opens it.

After entering Play Mode:

1. Player 1 uses movement/jump as already configured and presses `J`, `K`, or `L` on the ground for the unchanged three ground attacks.
2. Jump, then press `J` for `WeakKickAir`; velocity should remain under normal locomotion/gravity control.
3. Jump, then press `K` for `StrongKickAir`; its hitbox is forward/down and horizontal speed retains 92% at startup.
4. Jump, then press `L` for `DiveHeadbuttAir`; it brakes briefly, dives, and bounces once on a player hit.
5. Press `L` again before a real landing: the dive remains consumed. Wall slide, wall jump, and Dash do not reload it; landing and respawn do.
6. Miss the dive into a platform: its hitbox turns off immediately and the HUD shows ground recovery before returning to `None/Idle`.

The HUD reports Grounded, Dashing, exact attack variant, phase, and dive availability independently for each referenced player.

## Verification note

Unity 6000.5.6f1 is installed and was invoked in batch mode without running the menu tool. This machine's headless Editor license rejected the session before import/compilation (`No valid Unity Editor license`, internal return code 198). Runtime, Editor-tool, EditMode-test, and PlayMode-test sources were therefore compiled separately with the installed Roslyn compiler against the Unity 6000.5.6f1 reference assemblies; the Unity Test Runner still needs to be run once an Editor license is active.
