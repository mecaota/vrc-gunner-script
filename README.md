# Gunner

A generic projectile shooting library for VRChat worlds (UdonSharp).

- Emitters, aiming, single shots and streams
- Synced hit records (world, moving objects, players) delivered to hit receivers
- Receivers derive from `GunnerHitReceiver` and override only what they need
- Streams can keep their line unbroken on the first hit (`streamClearOnHit`, on by default = clear as before)

Status: early (0.5.0). APIs will change.

Requires VRChat Worlds SDK 3.8.1 or later.

## How shooting works

1. **Emitters.** An emitter is a `Transform`. Its +Z axis is the shooting direction (the rest direction when nothing aims it). A rig has up to 16 emitters (`GunnerRig.MaxEmitters`). Each emitter can have its own `ParticleSystem` in `shotParticles` (same index, World simulation space) for the visible shot.
2. **The rig.** `GunnerRig` owns everything else: aiming, hit tests, the synced hit records and the receiver list. It uses Manual sync, so it cannot share a GameObject with VRC Object Sync (put it on a child). Shooting takes ownership of the rig.
3. **Single shots.** `Fire()` shoots once from every emitter in the aim set (all by default). `_FireFrom(emitter)` shoots from one emitter, `_FireWith(emitter, payload)` adds an integer payload, and `_FireVolley(payload, mask, offsets)` picks the emitters with a bit mask and adds per-emitter angle offsets. A shot that hits travels at `shotSpeed`; receivers hear about the hit when it arrives.
4. **Streams.** A stream keeps emitting particles while it runs. `StartStream()` / `StopStream()` (or `_StartStreamWith(payload)`) start and stop it. The rig needs a `GunnerStream` in its `stream` field. `StopStream()` only stops a stream this device started; `_ForceStopStream()` stops any stream.
5. **Trigger helpers.** `TriggerDown()` / `TriggerUp()` follow `fireMode`: in Single mode `TriggerDown()` calls `Fire()`; in Stream mode it starts the stream and `TriggerUp()` stops it.
6. **Hit records.** Each hit becomes a record that the rig syncs to everyone. The sync always holds 16 records (`SyncSlots`, a ring); `maxHits` (1-16) only limits how many this device uses, so Android can use fewer and still match PC. Receivers read the current hit from the rig's `hit*` fields: `hitKind` (`KindWorld`, `KindMoving`, `KindPlayer`), `hitIndex` (moving object index or playerId), `hitBone`, `hitPoint`, `hitLocal`, `hitEmitter`, `hitPayload`, `hitStream`, `hitReplay` (restored for a late joiner; skip effects) and `hitResolved`.

### Receivers

Derive from `GunnerHitReceiver`, override only what you need, and add the receiver to the rig's `receivers` list (or call `rig._Register(this)` at runtime).

| Callback | Who | When |
|---|---|---|
| `_OnFire(rig)` | everyone | A single shot leaves an emitter, or a stream starts. Read `fireEmitter`, `fireStream`, `fireReplay`, `firePayload`. |
| `_OnHit(rig)` | everyone | A hit arrives. Read the `hit*` fields. |
| `_OnHitGone(rig)` | everyone | An arrived hit was retired, cleared or overwritten. |
| `_OnOwnerHit(rig)` | owner, projectile only | Just before a record is written. May change `rig.hitPayload`. |
| `_OnOwnerCommit(rig)` | owner, projectile only | Just before records are sent. |
| `_OnGunnerLateStep(rig)` | everyone | Every frame, only when `wantsLateStep` is on. `hitStart`, `hitWorld`, `hitLive` are filled for it. |
| `_OnGunnerOwner(rig)` | everyone | The rig's owner changed. |

The `projectile` field holds the one receiver that may rewrite each hit's payload. The rig never reads the payload itself.

Other useful calls: `StopFire()` (retire shots still in flight, keep arrived hits), `ClearHits()` (clear every record), `_SetAimDirection` / `_SetAimTarget` / `_SetAimAngles` / `ResetAim`, `_SetAimEmitters(mask)`, `BoneFollowOn` / `BoneFollowOff`, `_SetMount(emitter, transform)`, `_SetCollision(on)`, and the `_SolveFor` / `_SolveWithin` / `_SolveFrom` / `_SolveNearest` helpers that find which emitter can reach a point.

### Main fields and defaults

| Field | Default | Meaning |
|---|---|---|
| `fireMode` | Single | What `TriggerDown` / `TriggerUp` do (Single or Stream). |
| `maxHits` | 16 | Hit records this device uses (1-16). |
| `shotSpeed` | 25 | Metres per second. |
| `maxRange` | 30 | Metres. |
| `castRadius` | 0.05 | Hit test thickness (metres). |
| `hitMask` | everything except layer 2 | Layers that stop shots. Player layers are ignored here. |
| `hitPlayers` | on | Whether shots hit players. |
| `excludeOwner` | on | The player on this device is never hit. |
| `playerRadius` | 0.12 | Body radius as a share of the player's eye height. |
| `aimMaxAngle` | 60 | Aim limit in degrees from the rest direction. |
| `aimMaxAngles` | empty | Per-emitter aim limit (0 or empty = `aimMaxAngle`). |
| `streamRate` | 12 | Particles per second per emitter. |
| `streamWidth` | 0.02 | Joined line width in metres. The sample prefab uses 0.08 so the line is easy to see. |
| `streamWidthScales` | empty | Per-emitter multiplier on `streamWidth` (0 or empty = 1x). |
| `streamTaper` | empty | Width along each trail. Used when the curve has 2 or more keys. |
| `streamJoin` | Joined line | Particles, or particles joined into a line by their trails. |
| `streamHitMode` | Stop at first hit | Stop at first hit, or Keep hitting. |
| `streamClearOnHit` | on | Stop at first hit: clear the emitter's flying particles on the first hit. |
| `streamHitInterval` | 0.25 | Keep hitting: seconds between records per emitter. |
| `streamCollide` | on | Initial stream collision state. |
| `boneFollow` | off | Turn linked bones with the aim. |
| `boneStrength` | 1 | Share of the aim the bones take (0-1). |
| `boneYawMax` / `bonePitchUpMax` / `bonePitchDownMax` | 30 / 30 / 15 | Per-bone limits in degrees. |
| `boneSmooth` / `boneMinSpeed` / `boneMaxSpeed` | 10 / 30 / 0 | Follow speed. `boneMaxSpeed` 0 = no limit. |

`streamRate`, `streamWidth`, `streamWidthScales`, `streamTaper`, `streamJoin` and `streamHitMode` are read once when the rig starts; changing them at runtime has no effect. `streamClearOnHit` is read at every hit. `streamWidth`, `streamWidthScales` and `streamTaper` only take effect when `streamJoin` is "Joined line".

## Quick start

1. Right-click in the Hierarchy and choose **GameObject > Gunner > Add Gunner Rig**. This creates a rig with one emitter, its particles and a `GunnerStream`.
2. Select the emitter and turn it so +Z points where you want to shoot. Use **Add Emitter** or **Add Emitter At Body Part** for more.
3. Add a receiver: **Add Hit Splash** (particles at the hit point), **Add Gunner Sounds** (fire and impact sounds), or your own `GunnerHitReceiver`.
4. Call `TriggerDown()` / `TriggerUp()` from your own script, or use **Place Hand Controller** for a ready-made hand-held ball.
5. Open **Tools > Gunner > Check Window** and fix anything it lists.

A sample prefab (`Prefab/GunnerRig.prefab`, `Prefab/GunnerHandController.prefab`) and scene (`Scene/Gunner.unity`) are included.

## Editor tools

Menus (Hierarchy right-click, under `GameObject/Gunner/`):

- **Add Gunner Rig**: a new rig (or a child rig on an object with VRC Object Sync), with one emitter and a `GunnerStream`.
- **Add Emitter**: an emitter with its own particles under the selected object.
- **Add Emitter At Body Part**: right or left index fingertip, both fingertips, right or left palm, or mouth (needs a humanoid Animator).
- **Place Hand Controller**: a hand-held ball with VRC Pickup, VRC Object Sync, `GunnerPickupInput`, `GunnerStickAim`, `GunnerMoveHold` and `GunnerHandController`.
- **Add Hit Splash**: a `GunnerHitSplash` under the rig's `Parts` child.
- **Add Gunner Sounds**: a `GunnerSounds` with its audio sources (also in `Tools/Gunner/`).

**Tools > Gunner > Check Window** lists problems in every UdonSharp component of the open scenes. Click a row to select the object; **Fix** repairs what it can. The same checks also show in each inspector.

### JSON values

The `GunnerRig` inspector has a **JSON (values)** section, and the component's right-click menu has **Values JSON > Import... / Export...**.

- Export writes the number fields to a JSON file: `{ "type": "GunnerRig", "version": 1, "values": { ... } }`.
- Import reads them back. References, curves and layers are not included. Fields missing from the JSON keep their values. If there is any error, nothing is written.
- The linked JSON is remembered in this machine's user settings, not in the scene. The checks warn when the JSON changed after import.
- Save the scene after importing.

### Language

The inspectors and the Check Window have a **Language** popup at the bottom: Follow the editor, Japanese, or English. The choice is saved in EditorPrefs.

## Extending from other packages

All three are in `Mecaota.Gunner.EditorTools`. Call them from an `[InitializeOnLoad]` static constructor.

- `GunnerChecks.Register(IGunnerCheckProvider)`: add checks for your components. They show in the inspectors and the Check Window.
- `GunnerValueSchemas.Register(typeof(MyComponent), customize)`: allow JSON export and import for a component type (exact type match). `customize` can exclude fields and add ranges or lengths.
- `GunnerL10n.AddTable("Packages/<your.package>/Editor/strings.tsv")`: add your own text table (tab-separated columns `key`, `ja`, `en`; `<key>.tip` is the tooltip). Later tables override earlier keys.

## Changes in 0.5.0

- `streamWidthScales`: per-emitter multiplier on the stream line width (empty = 1x, same as before).
- `streamClearOnHit`: with "Stop at first hit", choose whether the first hit clears that emitter's flying particles and trails (default `true`, same as before; `false` keeps the running line unbroken).

## Changes in 0.4.0

- Extra linked bones: `linkExtraBones` / `linkExtraEmitter` add parent bones to a link; the aim angle is split evenly so every bone turns by the same amount. `chainYawLimit` / `chainPitchUpLimit` / `chainPitchDownLimit` cap the whole chain.
- Per-emitter aim limit: `aimMaxAngles`.
- Emitter sets: `_SetAimEmitters(mask)` chooses which emitters aim, stream and volley; `GunnerHandController.useEmitters` sets it while the ball is held.
- `_FireVolley(payload, mask, offsets)`: a volley with an emitter mask and per-emitter angle offsets.
- `firePayload` in `_OnFire`.
- `_SolveWithin`, `_SolveFrom`, `_SolveNearest` and `solveCasts`.
- Smooth follow: `boneMaxSpeed` limits the follow turning speed.
- JSON export and import of number fields.

License: MIT (see `LICENSE`).
