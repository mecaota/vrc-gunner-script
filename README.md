# Gunner

A generic projectile shooting library for VRChat worlds (UdonSharp).

- Emitters, aiming, single shots and streams
- Synced hit records (world, moving objects, players) delivered to hit receivers
- Receivers derive from `GunnerHitReceiver` and override only what they need
- Built-in fire and impact sounds and impact particles on the rig, and `GunnerAutoAim` to aim at and shoot players inside a trigger box

Status: early (0.10.0). APIs will change.

Requires Unity 2022.3 and VRChat Worlds SDK 3.10.0 or later. UdonSharp is included in the SDK.

## Install

### From VCC (VRChat Creator Companion) / ALCOM

1. Open the [distribution page](https://github.pito.run/vrc-gunner-scripts/) and click "Add to VCC" at the top to add the repository.
   - If the button does nothing, open VCC `Settings` → `Packages` → `Add Repository` and paste `https://github.pito.run/vrc-gunner-scripts/index.json`.
2. Open your world project with "Manage Project" and add "Gunner".

### Manual install

Download `com.mecaota.gunner-x.y.z.zip` from [Releases](https://github.com/mecaota/vrc-gunner-scripts/releases) and extract it to `Packages/com.mecaota.gunner/` in your Unity project (a `.unitypackage` is also available).

## How shooting works

1. **Emitters.** An emitter is a `Transform`. Its +Z axis is the shooting direction (the rest direction when nothing aims it). A rig has up to 16 emitters (`GunnerRig.MaxEmitters`). Each emitter can have its own `ParticleSystem` in `shotParticles` (same index, World simulation space) for the visible shot.
2. **The rig.** `GunnerRig` owns everything else: aiming, hit tests, the synced hit records and the receiver list. It uses Manual sync, so it cannot share a GameObject with VRC Object Sync (put it on a child). Shooting takes ownership of the rig.
3. **Single shots.** `Fire()` shoots once from every emitter in the aim set (all by default). `_FireWith(emitter, payload)` shoots from one emitter with an integer payload, and `_FireVolley(payload, mask, offsets)` picks the emitters with a bit mask and adds per-emitter angle offsets. A shot that hits travels at `shotSpeed`; receivers hear about the hit when it arrives.
4. **Streams.** A stream keeps emitting particles while it runs. `StartStream()` / `StopStream()` (or `_StartStreamWith(payload)`) start and stop it. The rig needs a `GunnerStream` in its `stream` field. `StopStream()` only stops a stream this device started.
5. **Trigger helpers.** `TriggerDown()` / `TriggerUp()` follow `fireMode`: in Single mode `TriggerDown()` calls `Fire()`; in Stream mode it starts the stream and `TriggerUp()` stops it.
6. **Hit records.** Each hit becomes a record that the rig syncs to everyone. The sync always holds 16 records (`SyncSlots`, a ring); `maxHits` (1-16) only limits how many this device uses, so Android can use fewer and still match PC. Receivers read the current hit from the rig's `hit*` fields: `hitKind` (`KindWorld`, `KindMoving`, `KindPlayer`), `hitIndex` (moving object index or playerId), `hitBone`, `hitPoint`, `hitLocal`, `hitEmitter`, `hitPayload`, `hitStream`, `hitReplay` (restored for a late joiner; skip effects) and `hitResolved`.

### Receivers

Derive from `GunnerHitReceiver`, override only what you need, and add the receiver to the rig's `receivers` list (or call `rig._Register(this)` at runtime).

| Callback | Who | When |
|---|---|---|
| `_OnFire(rig)` | everyone | A single shot leaves an emitter, or a stream starts. Read `fireEmitter`, `fireStream`, `fireReplay`, `firePayload`. |
| `_OnHit(rig)` | everyone | A hit arrives. Read the `hit*` fields. |
| `_OnHitGone(rig)` | everyone | An arrived hit was retired, cleared or overwritten. |
| `_OnGunnerLateStep(rig)` | everyone | Every frame, only when `wantsLateStep` is on. `hitStart`, `hitWorld`, `hitLive` are filled for it. |
| `_OnGunnerOwner(rig)` | everyone | The rig's owner changed. |

The rig never reads the payload itself; receivers decide what it means.

Other useful calls: `StopFire()` (retire shots still in flight, keep arrived hits), `ClearHits()` (clear every record), `_SetAimTarget` / `_SetAimAngles` / `ResetAim`, `_SetAimEmitters(mask)`, `BoneFollowOn` / `BoneFollowOff` and `_SetCollision(on)`. To make an emitter follow another Transform, make it a child of that Transform or use a VRC Parent Constraint.

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
| `streamJoin` | Joined line | Particles, or particles joined into a line by their trails. |
| `streamHitMode` | Stop at first hit | Stop at first hit (the first hit also clears the emitter's flying particles), or Keep hitting. |
| `streamHitInterval` | 0.25 | Keep hitting: seconds between records per emitter. |
| `streamCollide` | on | Initial stream collision state. |
| `boneFollow` | off | Turn linked bones with the aim. |
| `boneStrength` | 1 | Share of the aim the bones take (0-1). |
| `boneYawMax` / `bonePitchUpMax` / `bonePitchDownMax` | 30 / 30 / 15 | Per-bone limits in degrees. |
| `boneSmooth` / `boneMinSpeed` / `boneMaxSpeed` | 10 / 30 / 0 | Follow speed. `boneMaxSpeed` 0 = no limit. |

`streamRate`, `streamJoin` and `streamHitMode` are read once when the rig starts; changing them at runtime has no effect. The joined line's width is each particle system's own **Trails > Width over Trail**; the rig does not change it.

### Built-in sounds and impact particles

`GunnerRig` can play a fire sound and an impact sound, and burst particles at the hit point, by itself. The clips, volumes and impact particles are set per emitter on each emitter's card; the other fields are in the **Sounds** section and shared by all emitters. Every device plays them from the fire and hit events; nothing extra is synced.

| Field | Default | Meaning |
|---|---|---|
| `fireClips` / `impactClips` | empty | Per emitter. Empty = silent. |
| `fireVolumes` / `impactVolumes` | empty = 1 | Per emitter. Source volume (0-1). 0 = silent. |
| `pitchJitter` | 0.05 | Random pitch change per sound. 0 = off. |
| `fireVoices` / `impactVoices` | empty | AudioSources for each sound. Several can play at once; when all are busy, the oldest is stopped and reused. |
| `impactParticles` | empty | Per emitter. Particles burst at the hit point (World simulation space, +Z = splash direction). Empty = none. |
| `impactBursts` | empty = 1 | Per emitter. Particles per hit (at least 1). |
| `fireMinInterval` / `impactMinInterval` | 0.05 / 0.08 | Sounds closer together in time than this are dropped, whichever emitter they come from. Unless `fireMinInterval` is 0, only the first emitter with a configured sound (a clip and a volume above 0) plays its fire sound in a volley. |
| `soundOnPlayers` / `soundOnWorld` / `soundOnMoving` | on / on / on | Which hits play the impact sound and burst the impact particles. |
| `soundCullDistance` | 0 | Sounds farther than this from the listener's head are not played. 0 = the source's Max Distance. |

- The fire sound plays at the emitter that fired (a stream start uses the first valid emitter). The impact sound and particles play at the hit point. Records restored for a late joiner (`fireReplay`, `hitReplay`) and hits whose target is gone (`!hitResolved`) stay silent and show no particles.
- There is no repeated fire sound during a stream; only the stream start plays the fire sound.
- To shoot two kinds of shots from one place (for example a bullet and a web from the mouth), add two emitters there as two separate Transforms and set each card. Do not register the same Transform as two emitters; they overwrite each other's aim every frame.
- `Fire()`, `TriggerDown()` and the hand controller's `emitter = -1` shoot from every emitter in the aim set at once. To shoot one kind only, use `_FireWith(emitter, payload)`, or narrow the aim set with `_SetAimEmitters(mask)` / `GunnerHandController.useEmitters`.
- Udon cannot add `VRC_SpatialAudioSource` at runtime. Add one to every AudioSource in the editor and match its Far to the AudioSource's Max Distance. The Check Window does not check the rig's sources.

## Auto aim (`GunnerAutoAim`)

`GunnerAutoAim` makes a rig aim at and shoot players inside a box.

Setup:

1. Make an empty GameObject (for example `SenseZone`, a child of the rig is fine). Do not use the rig's own GameObject.
2. Add a `BoxCollider` with **Is Trigger** on, and `GunnerAutoAim` on the **same GameObject** (the trigger events only reach scripts on the collider's GameObject). Use a layer that collides with Player and PlayerLocal (Default does). Set the size with the BoxCollider's Size rather than the Transform scale.
3. **Place the box so it contains the players' feet, and extend its bottom below the floor.** The position recheck uses each player's feet (`GetPosition()`); a box that floats above the floor, or whose bottom is exactly at floor level, can count a standing player as outside.
4. Set `rig`. Leave `zone` empty to use the BoxCollider on the same GameObject.
5. If the zone moves with an animated object, add a kinematic Rigidbody to it.
6. **Use a rig that only `GunnerAutoAim` drives.** If something else (a hand-held controller, another script) takes ownership of the rig, `GunnerAutoAim` on that device starts aiming and shooting automatically.

| Field | Default | Meaning |
|---|---|---|
| `rig` | empty | The `GunnerRig` to drive. |
| `zone` | empty = the BoxCollider on this GameObject | The box. |
| `interval` | 0.2 | Seconds between target and aim updates. |
| `fireInterval` | 3 | Seconds between shots. |
| `aimHeight` | 0.7 | Aim height as a share of the target's eye height. |
| `switchWait` | 0.5 | After switching emitters, seconds before it may switch again. |
| `recheckInterval` | 1 | Seconds between position rechecks of the list. 0 = no periodic recheck. |

How it works:

- Every device keeps the list of players in the zone: `OnPlayerTriggerEnter` adds, `OnPlayerTriggerExit` and `OnPlayerLeft` remove.
- Only the rig's owner aims and shoots (`_FireWith` takes ownership of the rig, so letting every device shoot would make them fight over it). Every `interval` it picks the listed player nearest to the zone centre, then the emitter nearest to that player's aim point (walls and range are not checked), and calls `_SetAimEmitters(1 << emitter)`, `_SetAimTarget(point)` and, every `fireInterval`, `_FireWith(emitter, 0)`. With nobody listed it calls `ResetAim()` and `_SetAimEmitters(-1)` once, so the emitters return to rest.
- When this device becomes the rig's owner, it rechecks the list once, waits `fireInterval` before its first shot, and carries on. A device that is not the owner never shoots or takes the rig; when it stops being the owner it only resets its own local aim (`ResetAim()`, `_SetAimEmitters(-1)`, which neither take the rig nor sync), so the new owner's synced aim shows.
- Teleporting, respawning, joining and sitting in a station can skip the enter and exit events. Every `recheckInterval` seconds each device fixes its list from positions: a listed player whose feet are more than 0.75 m outside the box is removed, and a player whose feet are inside the box is added. The margin is only for removing, because the trigger counts a player as inside as soon as the body capsule touches the box.
- Respawn: `OnPlayerRespawn` only reaches the player who respawned, and that device removes the player at once. On the other devices (including the rig's owner) the player is removed by `OnPlayerTriggerExit` if it arrives; if it does not, the recheck removes them within `recheckInterval`, and only when the respawn point is more than 0.75 m outside the box. With `recheckInterval` 0, other devices do not remove them in that case.
- A seated player whose feet are inside the box is added back by the recheck and can be targeted.
- With the rig's `excludeOwner` on, the rig's owner is never hit, so `GunnerAutoAim` does not choose the owner (the local player on the owner's device) as a target. Turn `excludeOwner` off if the owner should be targeted and hit too.
- The aim is synced only while the rig syncs it (bone follow on with linked bones, or a stream started on this device). With bone follow off, other devices see the emitters stay at rest; the shots still fly to the recorded points.

## Quick start

1. Right-click in the Hierarchy and choose **GameObject > Gunner > Add Gunner Rig**. This creates a rig with one emitter, its particles and a `GunnerStream`.
2. Select the emitter and turn it so +Z points where you want to shoot. Use **Add Emitter** or **Add Emitter At Body Part** for more.
3. For fire and impact sounds and impact particles, set the clips and impact particles on each emitter's card, and the AudioSources in the rig's **Sounds** section. For anything else, add your own `GunnerHitReceiver`.
4. Call `TriggerDown()` / `TriggerUp()` from your own script, or use **Place Hand Controller** for a ready-made hand-held ball.
5. Open **Tools > Gunner > Check Window** and fix anything it lists.

A sample prefab (`Prefab/GunnerRig.prefab`, `Prefab/GunnerHandController.prefab`) and scene (`Scene/Gunner.unity`) are included.

## Editor tools

Menus (Hierarchy right-click, under `GameObject/Gunner/`):

- **Add Gunner Rig**: a new rig (or a child rig on an object with VRC Object Sync), with one emitter and a `GunnerStream`.
- **Add Emitter**: an emitter with its own particles under the selected object.
- **Add Emitter At Body Part**: right or left index fingertip, both fingertips, right or left palm, or mouth (needs a humanoid Animator).
- **Place Hand Controller**: a hand-held ball with VRC Pickup, VRC Object Sync, `GunnerPickupInput`, `GunnerStickAim`, `GunnerMoveHold` and `GunnerHandController`.

**Tools > Gunner > Check Window** lists problems in every UdonSharp component of the open scenes. Click a row to select the object; **Fix** repairs what it can. The same checks also show in each inspector.

### Language

The inspectors and the Check Window have a **Language** popup at the bottom: Follow the editor, Japanese, or English. The choice is saved in EditorPrefs.

## Extending from other packages

Both are in `Mecaota.Gunner.EditorTools`. Call them from an `[InitializeOnLoad]` static constructor.

- `GunnerChecks.Register(IGunnerCheckProvider)`: add checks for your components. They show in the inspectors and the Check Window.
- `GunnerL10n.AddTable("Packages/<your.package>/Editor/strings.tsv")`: add your own text table (tab-separated columns `key`, `ja`, `en`; `<key>.tip` is the tooltip). Later tables override earlier keys.

## Changes

See [CHANGELOG.md](CHANGELOG.md) for every version.

## Changes in 0.10.0

Breaking: the fire and impact sounds, their volumes and the impact particles are now set per emitter (`fireClips`, `impactClips`, `fireVolumes`, `impactVolumes`, `impactParticles`, `impactBursts`). Values set in 0.9.0 are lost on update; set them again on each emitter's card (see CHANGELOG.md).

## Changes in 0.8.0

Removals and clean-up (see CHANGELOG.md for the full list). `GunnerSounds` is removed; use the rig's built-in sounds. The stream line width fields are removed; set each particle system's Trails > Width over Trail instead.

## Changes in 0.6.0

- `GunnerAutoAim`: aim at and shoot players inside a trigger box (see "Auto aim").
- Built-in fire and impact sounds on `GunnerRig` (see "Built-in sounds and impact particles"). Empty clips = silent, the same as before. `GunnerSounds` is unchanged; clips on both play twice.

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
