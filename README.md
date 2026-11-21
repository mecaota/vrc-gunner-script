# Gunner

A generic projectile shooting library for VRChat worlds (UdonSharp).

- Emitters, aiming, single shots and streams
- Synced hit records (world, moving objects, players) delivered to hit receivers
- Receivers derive from `GunnerHitReceiver` and override only what they need
- Built-in fire and impact sounds and impact particles on the rig, and `GunnerAutoAim` to aim at and shoot players whose feet are inside a box

Status: early (0.15.0). APIs will change.

Requires Unity 2022.3 and VRChat Worlds SDK 3.10.0 or later. UdonSharp is included in the SDK.

## Install

### From VCC (VRChat Creator Companion) / ALCOM

1. Open the [distribution page](https://github.pito.run/vrc-gunner-scripts/) and click "Add to VCC" at the top to add the repository.
   - If the button does nothing, open VCC `Settings` → `Packages` → `Add Repository` and paste `https://github.pito.run/vrc-gunner-scripts/index.json`.
2. Open your world project with "Manage Project" and add "Gunner".

### Manual install

Download `com.mecaota.gunner-x.y.z.zip` from [Releases](https://github.com/mecaota/vrc-gunner-scripts/releases) and extract it to `Packages/com.mecaota.gunner/` in your Unity project (a `.unitypackage` is also available).

## How shooting works

1. **Emitters.** Each emitter is a `GunnerEmitter` component that holds its settings. The +Z axis of its `pivot` Transform is the shooting direction (the rest direction when nothing aims it). The order of the rig's `muzzles` list gives the emitter numbers. A rig has up to 16 emitters (`GunnerRig.MaxEmitters`). Each emitter can have its own `ParticleSystem` in `shotParticle` (World simulation space) for the visible shot. The editor tools place an emitter at `rig/muzzles/<name>`: the `GunnerEmitter` (and a VRC Parent Constraint when it follows a bone) is on `<name>`, and its child `Pivot` is the `pivot`. Do not use a Transform with a constraint as the `pivot`; the constraint overwrites the aim. While the constraint is locked, you cannot move `<name>` in edit mode; unlock it (or move `Pivot`) to adjust.
2. **The rig.** `GunnerRig` owns everything else: aiming, hit tests, the synced hit records and the receiver list. It uses Manual sync, so it cannot share a GameObject with VRC Object Sync (put it on a child). Shooting takes ownership of the rig.
3. **Single shots.** `Fire()` shoots once from every Single emitter in the aim set (all by default). `_FireWith(emitter, payload)` shoots a single shot from one emitter, whatever its fire mode, with an integer payload, and `_FireVolley(payload, mask, offsets)` picks the emitters with a bit mask and adds per-emitter angle offsets. A shot that hits travels at its emitter's `shotSpeed`; receivers hear about the hit when it arrives.
4. **Streams.** A stream keeps emitting particles while it runs. `StartStream()` / `StopStream()` (or `_StartStreamWith(payload)`) start and stop it on the Stream emitters in the aim set. With no Stream emitter in the aim set, `StartStream()` does nothing. The rig needs a `GunnerStream` in its `stream` field. `StopStream()` only stops a stream this device started.
5. **Trigger helpers.** Each emitter has a fire mode (`fireMode`: Single or Stream). `TriggerDown()` calls `StartStream()` and then `Fire()`. `TriggerUp()` calls `StopStream()`.
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

Other useful calls: `StopFire()` (retire shots still in flight, keep arrived hits), `ClearHits()` (clear every record), `_SetAimTarget` / `ResetAim`, `_SetAimEmitters(mask)`, `BoneFollowOn` / `BoneFollowOff` and `_SetCollision(on)`.

### Main fields and defaults

On the rig:

| Field | Default | Meaning |
|---|---|---|
| `muzzles` | empty | The `GunnerEmitter`s. The order gives the emitter numbers (`GunnerPre<n>`, `useEmitters`, `emitterSwitches`). |
| `maxHits` | 16 | Hit records this device uses (1-16). |
| `playerRadius` | 0.12 | Body radius as a share of the player's eye height. |
| `boneFollow` | off | Initial state: whether linked bones turn with the aim at start. Switch at runtime with `BoneFollowOn` / `BoneFollowOff`. |

On each `GunnerEmitter`:

| Field | Default | Meaning |
|---|---|---|
| `pivot` | empty | The Transform the aim turns and shots leave from. |
| `fireMode` | Single | Single or Stream: how the emitter shoots on `TriggerDown` and from the hand controller. |
| `shotSpeed` | 25 | Metres per second. |
| `maxRange` | 30 | Metres. |
| `castRadius` | 0.05 | Hit test thickness (metres). |
| `aimMaxAngle` | 0 | Aim limit in degrees from the rest direction (0 = 60). |
| `streamRate` | 12 | Particles per second. |
| `streamJoin` | Joined line | Particles, or particles joined into a line by their trails. |
| `streamHitMode` | Stop at first hit | Stop at first hit (the first hit also clears the emitter's flying particles), or Keep hitting. |
| `streamHitInterval` | 0.25 | Keep hitting: seconds between records. |
| `streamCollide` | On | Whether the stream records hits. `_SetCollision(false)` switches it off for all emitters at runtime. |
| `linkBones` | empty | Bones the aim turns, root to tip. The last one is the tip; the others must be its ancestors. Empty = no link. |
| `boneStrength` | 1 | Share of the aim the bones take (0-1). |
| `boneYawMax` / `bonePitchUpMax` / `bonePitchDownMax` | 30 / 30 / 15 | Per-bone limits in degrees. |
| `boneSmooth` / `boneMinSpeed` / `boneMaxSpeed` | 10 / 30 / 0 | Follow speed. `boneMaxSpeed` 0 = no limit. |

Shots hit the layers in the emitter's particle system's **Collision > Collides With**, even when the Collision module is off; player layers are ignored there. Shots always hit players, including the rig's owner. An emitter without a particle system hits no layers (it can still hit players). The rig copies the emitters' values when it starts. In Play mode in the editor, a change on a `GunnerEmitter` takes effect at once for single shots, sounds, `castRadius` and the like. The values the stream (`GunnerStream`) reads (`shotSpeed`, `maxRange`, `streamRate`, `streamJoin`, `streamHitMode`, `streamHitInterval`), the fire mode, `streamCollide`, `linkBones` and the rest pose apply from the next Play. `pivot` and `shotParticle` change for single shots at once, but the stream and the rest pose use the old ones until the next Play. To be sure, Play again; to switch stream collision at runtime, use `_SetCollision(on)`. The joined line's width is each particle system's own **Trails > Width over Trail**; the rig does not change it.

Emitters with the same tip bone turn that bone together, using the bone fields of the lowest-numbered one; the others do not show them. When that emitter changes (it is removed from the list, or its tip bone is changed), the new lowest-numbered emitter's values are used.

### Built-in sounds and impact particles

`GunnerRig` can play a fire sound and an impact sound, and burst particles at the hit point, by itself. All of them are set on each `GunnerEmitter`. Every device plays them from the fire and hit events; nothing extra is synced.

| Field | Default | Meaning |
|---|---|---|
| `fireSource` / `impactSource` | empty | The AudioSource to play. Set the volume, pitch and range on it. Empty = silent. |
| `fireClip` / `impactClip` | empty | The clip put on the source before it plays. Empty = the clip already on the source (with a source shared by several emitters, the clip put on it last). |
| `fireMinInterval` / `impactMinInterval` | 0 | While the sound is playing, the next one is skipped until its playback position (the AudioSource's `time`) reaches this many seconds; once it ends, the next one plays. 0 = restart it every time. For example, 0.08 for impacts keeps a stream from sounding too busy. |
| `impactParticle` | empty | Particles burst at the hit point (World simulation space, +Z = splash direction). Empty = none. |
| `impactBurst` | 1 | Particles per hit (at least 1). |

- The fire sound source plays where it is (a stream start uses the first Stream emitter in the aim set with a fire sound source); place it near the emitter. The impact sound source and particles are moved to the hit point; place impact sound sources outside the rig, or moving them would move part of the rig. A source plays one sound at a time; the next sound restarts it. Records restored for a late joiner (`fireReplay`, `hitReplay`) and hits whose target is gone (`!hitResolved`) stay silent and show no particles.
- There is no repeated fire sound during a stream; only the stream start plays the fire sound.
- To shoot two kinds of shots from one place (for example a bullet and a web from the mouth), add two `GunnerEmitter`s there with separate `pivot` Transforms. Do not use the same Transform as the pivot of two emitters; they overwrite each other's aim every frame.
- `TriggerDown()` and the hand controller's `emitter = -1` shoot from every emitter in the aim set at once, each in its own fire mode. `Fire()` shoots only the Single emitters. To shoot one kind only, use `_FireWith(emitter, payload)`, or narrow the aim set with `_SetAimEmitters(mask)` / `GunnerHandController.useEmitters`.
- The hand-held ball aims at the point straight ahead of it, as far as the range (`maxRange`) of its `emitter`, at least 1 m. With `emitter = -1`, emitter 0's range is used. With an emitter number, only that emitter aims and shoots while the ball is held (`useEmitters` is ignored).
- Udon cannot add `VRC_SpatialAudioSource` at runtime. Add it to the AudioSources in the editor.

## Auto aim (`GunnerAutoAim`)

`GunnerAutoAim` makes a rig aim at and shoot players whose feet are inside a box.

Setup:

1. Make an empty GameObject (for example `SenseZone`, a child of the rig is fine). Do not use the rig's own GameObject.
2. Add a `BoxCollider` with **Is Trigger** on (so it does not block players) and `GunnerAutoAim`. Set the size with the BoxCollider's Size rather than the Transform scale.
3. **Place the box so it contains the players' feet, and extend its bottom below the floor.** Only each player's feet (`GetPosition()`) are checked; a box that floats above the floor, or whose bottom is exactly at floor level, can count a standing player as outside.
4. Set `rig`. Leave `zone` empty to use the BoxCollider on the same GameObject.
5. **Use a rig that only `GunnerAutoAim` drives.** If something else (a hand-held controller, another script) takes ownership of the rig, `GunnerAutoAim` on that device starts aiming and shooting automatically.

| Field | Default | Meaning |
|---|---|---|
| `rig` | empty | The `GunnerRig` to drive. |
| `zone` | empty = the BoxCollider on this GameObject | The box. |
| `interval` | 0.2 | Seconds between target and aim updates. |
| `fireInterval` | 3 | Seconds between shots. It starts aiming one or two ticks (`interval` each) before a shot is due. |
| `aimHeight` | 0.7 | Aim height as a share of the target's eye height. |
| `emitterSwitches` | empty | One mark GameObject per emitter, in emitter order. An emitter whose mark is inactive is not used; an empty slot or a missing entry is always used. With no usable emitter, it does not shoot. |

How it works:

- Only the rig's owner aims and shoots (`_FireWith` takes ownership of the rig, so letting every device shoot would make them fight over it). From one or two ticks before a shot is due, it picks, among all players whose feet are inside the box, the one nearest to the zone centre, then the usable emitter (see `emitterSwitches`) nearest to that player's aim point (walls and range are not checked), and calls `_SetAimEmitters(1 << emitter)` and `_SetAimTarget(point)`, then, once `fireInterval` has passed, `_FireWith(emitter, firePayloads[emitter])`. At any other time, and with nobody inside, it calls `ResetAim()` and `_SetAimEmitters(-1)` once, so the emitters return to rest. While the pre-fire animation plays, it keeps the aim.
- When this device becomes the rig's owner, it waits `fireInterval` before its first shot, and carries on. A device that is not the owner never shoots or takes the rig; when it stops being the owner it only resets its own local aim (`ResetAim()`, `_SetAimEmitters(-1)`, which neither take the rig nor sync), so the new owner's synced aim shows.
- Teleported, respawned and seated players count by where their feet are now.
- The rig's owner (the local player on the owner's device) can be chosen as a target and hit too.
- To switch emitters with a switch, put empty GameObjects in `emitterSwitches` and let the switch turn them on and off (for example LuraSwitch2's ModeSwitch with the marks in its `targets`). Use one switch per group of emitters (mouth, spinnerets) and keep one `GunnerAutoAim` per rig. Put the marks where the parent stays active: under a UI panel, for example, closing the panel would turn those emitters off and the auto-aim would stop shooting.
- The aim is synced only while the rig syncs it (bone follow on with linked bones, or a stream started on this device). With bone follow off, other devices see the emitters stay at rest; the shots still fly to the recorded points.

## Quick start

1. Right-click in the Hierarchy and choose **GameObject > Gunner > Add Gunner Rig**. This creates a rig with one emitter, its particles and a `GunnerStream`.
2. Select the emitter's `Pivot` and turn it so +Z points where you want to shoot. Use **Add Emitter** or **Add Emitter At Body Part** for more.
3. For fire and impact sounds and impact particles, set the AudioSources and impact particles on each `GunnerEmitter`. For anything else, add your own `GunnerHitReceiver`.
4. Call `TriggerDown()` / `TriggerUp()` from your own script.
5. Open **Tools > Gunner > Check Window** and fix anything it lists.

A sample prefab (`Prefab/GunnerRig.prefab`, `Prefab/GunnerHandController.prefab`) and scene (`Scene/Gunner.unity`) are included.

## Editor tools

Menus (Hierarchy right-click, under `GameObject/Gunner/`):

- **Add Gunner Rig**: a new rig (or a child rig on an object with VRC Object Sync), with one emitter and a `GunnerStream`.
- **Add Emitter**: an emitter at `rig/muzzles/<name>` (a `GunnerEmitter`, a child `Pivot` and its own particles), added to the rig's list. On a bone, it starts at the bone and follows it with a VRC Parent Constraint (offset = the pose when it was made). The bone can be outside the rig, in an armature next to it under the same Animator.
- **Add Emitter At Body Part**: right or left index fingertip, both fingertips, right or left palm, or mouth (needs a humanoid Animator). The emitter follows the bone in the same way.
- **+ Add parent bone** (on the `GunnerEmitter` inspector, for `linkBones`): an emitter made by these commands has an empty `linkBones`; with the list empty, the button adds the bone its VRC Parent Constraint follows, and each press after that adds the parent of the first one.

**Tools > Gunner > Check Window** lists problems in every UdonSharp component of the open scenes. Click a row to select the object; **Fix** repairs what it can. The same checks also show in each inspector.

### Language

The inspectors and the Check Window have a **Language** popup at the bottom: Follow the editor, Japanese, or English. The choice is saved in EditorPrefs.

## Extending from other packages

Both are in `Mecaota.Gunner.EditorTools`. Call them from an `[InitializeOnLoad]` static constructor.

- `GunnerChecks.Register(IGunnerCheckProvider)`: add checks for your components. They show in the inspectors and the Check Window.
- `GunnerL10n.AddTable("Packages/<your.package>/Editor/strings.tsv")`: add your own text table (tab-separated columns `key`, `ja`, `en`; `<key>.tip` is the tooltip). Later tables override earlier keys.

## Changes

See [CHANGELOG.md](CHANGELOG.md) for every version.

## Changes in 0.15.0

Breaking: each emitter is now a `GunnerEmitter` component, and the rig's per-emitter arrays are gone. Emitters sit under `rig/muzzles` and follow bones with a VRC Parent Constraint. Old values are lost on update; note them first and see the migration steps in CHANGELOG.md.

```
Spider                (has the Animator)
├─ Armature            (the bone is here)
└─ GunnerRig
   └─ muzzles
      └─ mouth      GunnerEmitter + VRC Parent Constraint (source: the bone)
         └─ Pivot   the pivot: the aim turns this
```

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

- `GunnerAutoAim`: aim at and shoot players whose feet are inside a box (see "Auto aim").
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
