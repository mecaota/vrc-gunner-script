# Gunner

A generic projectile shooting library for VRChat worlds (UdonSharp).

- Emitters, aiming, single shots and streams
- Synced hit records (world, moving objects, players) delivered to hit receivers
- Receivers derive from `GunnerHitReceiver` and override only what they need
- Streams can keep their line unbroken on the first hit (`streamClearOnHit`, on by default = clear as before)

Status: early (0.5.0). APIs will change.

Changes in 0.5.0:

- `streamWidthScales`: per-emitter multiplier on the stream line width (empty = 1x, same as before).
- `streamClearOnHit`: with "Stop at first hit", choose whether the first hit clears that emitter's flying particles and trails (default `true`, same as before; `false` keeps the running line unbroken).

Requires VRChat Worlds SDK 3.8.1 or later.

License: MIT (see `LICENSE`).
