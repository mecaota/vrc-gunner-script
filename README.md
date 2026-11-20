# Gunner

A generic projectile shooting library for VRChat worlds (UdonSharp).

- Emitters, aiming, single shots and streams
- Synced hit records (world, moving objects, players) delivered to hit receivers
- Receivers derive from `GunnerHitReceiver` and override only what they need
- Built-in fire and impact sounds and impact particles on the rig, and `GunnerAutoAim` to aim at and shoot players inside a trigger box

Status: early (0.13.0). APIs will change.

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
3. **Single shots.** `Fire()` shoots once from every Single emitter in the aim set (all by default). `_FireWith(emitter, payload)` shoots a single shot from one emitter, whatever its fire mode, with an integer payload, and `_FireVolley(payload, mask, offsets)` picks the emitters with a bit mask and adds per-emitter angle offsets. A shot that hits travels at its emitter's `shotSpeeds` value; receivers hear about the hit when it arrives.
4. **Streams.** A stream keeps emitting particles while it runs. `StartStream()` / `StopStream()` (or `_StartStreamWith(payload)`) start and stop it on the Stream emitters in the aim set. With no Stream emitter in the aim set, `StartStream()` does nothing. The rig needs a `GunnerStream` in its `stream` field. `StopStream()` only stops a stream this device started.
5. **Trigger helpers.** Each emitter has a fire mode (`fireModes`: Single or Stream). `TriggerDown()` calls `StartStream()` and then `Fire()`. `TriggerUp()` calls `StopStream()`.
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

Other useful calls: `StopFire()` (retire shots still in flight, keep arrived hits), `ClearHits()` (clear every record), `_SetAimTarget` / `ResetAim`, `_SetAimEmitters(mask)`, `BoneFollowOn` / `BoneFollowOff` and `_SetCollision(on)`. To make an emitter follow another Transform, make it a child of that Transform or use a VRC Parent Constraint.

### Main fields and defaults

| Field | Default | Meaning |
|---|---|---|
| `fireModes` | empty = Single | Per emitter. Single or Stream: how the emitter shoots on `TriggerDown` and from the hand controller. |
| `maxHits` | 16 | Hit records this device uses (1-16). |
| `shotSpeeds` | empty = 25 | Per emitter. Metres per second. |
| `maxRanges` | empty = 30 | Per emitter. Metres. |
| `castRadii` | empty = 0.05 | Per emitter. Hit test thickness (metres). |
| `playerRadius` | 0.12 | Body radius as a share of the player's eye height. |
| `aimMaxAngles` | empty = 60 | Per emitter. Aim limit in degrees from the rest direction (0 = 60). |
| `streamRates` | empty = 12 | Per emitter. Particles per second. |
| `streamJoins` | empty = Joined line | Per emitter. Particles, or particles joined into a line by their trails. |
| `streamHitModes` | empty = Stop at first hit | Per emitter. Stop at first hit (the first hit also clears the emitter's flying particles), or Keep hitting. |
| `streamHitIntervals` | empty = 0.25 | Per emitter. Keep hitting: seconds between records. |
| `streamCollides` | empty = On | Per emitter. Whether the stream records hits. `_SetCollision(false)` switches it off for all emitters at runtime. |
| `boneFollow` | off | Initial state: whether linked bones turn with the aim at start. Switch at runtime with `BoneFollowOn` / `BoneFollowOff`. |
| `boneStrengths` | empty = 1 | Per emitter. Share of the aim the bones take (0-1). |
| `boneYawMaxes` / `bonePitchUpMaxes` / `bonePitchDownMaxes` | empty = 30 / 30 / 15 | Per emitter. Per-bone limits in degrees. |
| `boneSmooths` / `boneMinSpeeds` / `boneMaxSpeeds` | empty = 10 / 30 / 0 | Per emitter. Follow speed. `boneMaxSpeeds` 0 = no limit. |

Shots hit the layers in the emitter's particle system's **Collision > Collides With**, even when the Collision module is off; player layers are ignored there. Shots always hit players, including the rig's owner. An emitter without a particle system hits no layers (it can still hit players). `shotSpeeds`, `maxRanges`, `streamRates`, `streamJoins`, `streamHitModes` and `streamHitIntervals` are read by the stream once when the rig starts; changing them at runtime does not change the stream. `streamCollides` is also read once when the rig starts; to switch stream collision at runtime, use `_SetCollision(on)`. The joined line's width is each particle system's own **Trails > Width over Trail**; the rig does not change it.

Emitters with the same lead bone turn that bone together, using the bone fields of the lowest-numbered one; the other cards do not show them. When that card changes (it is removed, or its lead bone is changed), the new lowest-numbered card's values are used.

### Built-in sounds and impact particles

`GunnerRig` can play a fire sound and an impact sound, and burst particles at the hit point, by itself. All of them are set per emitter on each emitter's card. Every device plays them from the fire and hit events; nothing extra is synced.

| Field | Default | Meaning |
|---|---|---|
| `fireSources` / `impactSources` | empty | Per emitter. The AudioSource to play. Set the volume, pitch and range on it. Empty = silent. |
| `fireClips` / `impactClips` | empty | Per emitter. The clip put on the source before it plays. Empty = the clip already on the source (with a source shared by several emitters, the clip put on it last). |
| `fireMinIntervals` / `impactMinIntervals` | empty = 0 | Per emitter. While the sound is playing, the next one is skipped until its playback position (the AudioSource's `time`) reaches this many seconds; once it ends, the next one plays. 0 = restart it every time. For example, 0.08 for impacts keeps a stream from sounding too busy. |
| `impactParticles` | empty | Per emitter. Particles burst at the hit point (World simulation space, +Z = splash direction). Empty = none. |
| `impactBursts` | empty = 1 | Per emitter. Particles per hit (at least 1). |

- The fire sound source plays where it is (a stream start uses the first Stream emitter in the aim set with a fire sound source); place it near the emitter. The impact sound source and particles are moved to the hit point; place impact sound sources outside the rig, or moving them would move part of the rig. A source plays one sound at a time; the next sound restarts it. Records restored for a late joiner (`fireReplay`, `hitReplay`) and hits whose target is gone (`!hitResolved`) stay silent and show no particles.
- There is no repeated fire sound during a stream; only the stream start plays the fire sound.
- To shoot two kinds of shots from one place (for example a bullet and a web from the mouth), add two emitters there as two separate Transforms and set each card. Do not register the same Transform as two emitters; they overwrite each other's aim every frame.
- `TriggerDown()` and the hand controller's `emitter = -1` shoot from every emitter in the aim set at once, each in its own fire mode. `Fire()` shoots only the Single emitters. To shoot one kind only, use `_FireWith(emitter, payload)`, or narrow the aim set with `_SetAimEmitters(mask)` / `GunnerHandController.useEmitters`.
- The hand-held ball aims at the point straight ahead of it, as far as the range (`maxRanges`) of its `emitter`. With `emitter = -1`, emitter 0's range is used.
- Udon cannot add `VRC_SpatialAudioSource` at runtime. Add it to the AudioSources in the editor.

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
| `fireInterval` | 3 | Seconds between shots. It starts aiming one or two ticks (`interval` each) before a shot is due. |
| `aimHeight` | 0.7 | Aim height as a share of the target's eye height. |
| `emitterMask` | -1 | Emitters it may aim with (bit i = emitter i, -1 = all; emitters a and c of a, b, c = 5). With none in the set, it does not shoot. |
| `recheckInterval` | 1 | Seconds between position rechecks of the list. 0 = no periodic recheck. |

How it works:

- Every device keeps the list of players in the zone: `OnPlayerTriggerEnter` adds, `OnPlayerTriggerExit` and `OnPlayerLeft` remove.
- Only the rig's owner aims and shoots (`_FireWith` takes ownership of the rig, so letting every device shoot would make them fight over it). From one or two ticks before a shot is due, it picks the listed player nearest to the zone centre, then the emitter in `emitterMask` nearest to that player's aim point (walls and range are not checked), and calls `_SetAimEmitters(1 << emitter)` and `_SetAimTarget(point)`, then, once `fireInterval` has passed, `_FireWith(emitter, firePayloads[emitter])`. At any other time, and with nobody listed, it calls `ResetAim()` and `_SetAimEmitters(-1)` once, so the emitters return to rest. While the pre-fire animation plays, it keeps the aim.
- When this device becomes the rig's owner, it rechecks the list once, waits `fireInterval` before its first shot, and carries on. A device that is not the owner never shoots or takes the rig; when it stops being the owner it only resets its own local aim (`ResetAim()`, `_SetAimEmitters(-1)`, which neither take the rig nor sync), so the new owner's synced aim shows.
- Teleporting, respawning, joining and sitting in a station can skip the enter and exit events. Every `recheckInterval` seconds each device fixes its list from positions: a listed player whose feet are more than 0.75 m outside the box is removed, and a player whose feet are inside the box is added. The margin is only for removing, because the trigger counts a player as inside as soon as the body capsule touches the box.
- Respawn: `OnPlayerRespawn` only reaches the player who respawned, and that device removes the player at once. On the other devices (including the rig's owner) the player is removed by `OnPlayerTriggerExit` if it arrives; if it does not, the recheck removes them within `recheckInterval`, and only when the respawn point is more than 0.75 m outside the box. With `recheckInterval` 0, other devices do not remove them in that case.
- A seated player whose feet are inside the box is added back by the recheck and can be targeted.
- The rig's owner (the local player on the owner's device) can be chosen as a target and hit too.
- The aim is synced only while the rig syncs it (bone follow on with linked bones, or a stream started on this device). With bone follow off, other devices see the emitters stay at rest; the shots still fly to the recorded points.

## Quick start

1. Right-click in the Hierarchy and choose **GameObject > Gunner > Add Gunner Rig**. This creates a rig with one emitter, its particles and a `GunnerStream`.
2. Select the emitter and turn it so +Z points where you want to shoot. Use **Add Emitter** or **Add Emitter At Body Part** for more.
3. For fire and impact sounds and impact particles, set the AudioSources and impact particles on each emitter's card. For anything else, add your own `GunnerHitReceiver`.
4. Call `TriggerDown()` / `TriggerUp()` from your own script.
5. Open **Tools > Gunner > Check Window** and fix anything it lists.

A sample prefab (`Prefab/GunnerRig.prefab`, `Prefab/GunnerHandController.prefab`) and scene (`Scene/Gunner.unity`) are included.

## Editor tools

Menus (Hierarchy right-click, under `GameObject/Gunner/`):

- **Add Gunner Rig**: a new rig (or a child rig on an object with VRC Object Sync), with one emitter and a `GunnerStream`.
- **Add Emitter**: an emitter with its own particles under the selected object.
- **Add Emitter At Body Part**: right or left index fingertip, both fingertips, right or left palm, or mouth (needs a humanoid Animator).

**Tools > Gunner > Check Window** lists problems in every UdonSharp component of the open scenes. Click a row to select the object; **Fix** repairs what it can. The same checks also show in each inspector.

### Language

The inspectors and the Check Window have a **Language** popup at the bottom: Follow the editor, Japanese, or English. The choice is saved in EditorPrefs.

## Extending from other packages

Both are in `Mecaota.Gunner.EditorTools`. Call them from an `[InitializeOnLoad]` static constructor.

- `GunnerChecks.Register(IGunnerCheckProvider)`: add checks for your components. They show in the inspectors and the Check Window.
- `GunnerL10n.AddTable("Packages/<your.package>/Editor/strings.tsv")`: add your own text table (tab-separated columns `key`, `ja`, `en`; `<key>.tip` is the tooltip). Later tables override earlier keys.

## Changes

See [CHANGELOG.md](CHANGELOG.md) for every version.

## Changes in 0.13.0

Breaking: `fireMode` is replaced by the per-emitter `fireModes`. A rig that had `fireMode` set to Stream must set Stream on each emitter card again (see CHANGELOG.md). `Fire()` and `_FireWith(-1, payload)` no longer shoot from Stream emitters. The hand-held ball now aims where it points; stick aiming, `GunnerStickAim` and `_SetAimAngles` are removed.

## Changes in 0.12.0

Breaking: `hitPlayers`, `excludeOwner`, `aimMaxAngle` and `streamCollide` are removed. Shots always hit players, including the rig's owner. Set the aim limit and stream collision per emitter on its card (see CHANGELOG.md). Each card also takes a fire clip and an impact clip.

## Changes in 0.11.0

Breaking: most rig fields moved to the emitter cards, and old values are lost on update. Note them before updating and set them again (see the migration steps in CHANGELOG.md).

- Sounds: each card takes one fire and one impact AudioSource; the clip, volume and range are set on the AudioSource.
- Shot speed, range, shot radius, the stream fields and the bone link values are set per emitter. Shots hit the layers in each emitter particle system's **Collision > Collides With** (`hitMask` is removed). An emitter without a particle system no longer hits objects. Shot particles made by the setup tools, and those in the sample scene, leave out layers 2 (Ignore Raycast) and 13 (the hand ball); the prefab's leave out only layer 2.
- The emitter cards and linked bones are drawn in frames; emitters are removed with **Remove** on their card.
- JSON export and import of number fields is removed, including `GunnerValueSchemas.Register`.

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
