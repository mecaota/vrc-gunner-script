# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package uses [Semantic Versioning](https://semver.org/). While the version is 0.x, minor versions may break the API.

このファイルの形式は Keep a Changelog に従います。版は Semantic Versioning です。0.x の間は、マイナー版でも API が変わることがあります。

## [0.9.0] - unreleased / 未公開

The hit splash receiver is merged into the rig. It shares the impact sound's filters, so a rig can play only the sound, show only the particles, or both.

着弾のしぶきの受け手を rig に統合しました。着弾音と同じ絞り込みを使うので、音だけ、粒子だけ、その両方を rig だけで設定できます。

### Added / 追加

- `GunnerRig.impactParticles` and `impactBurst`: particles burst at the hit point (World simulation space, +Z = splash direction). The particle system decides the look and lifetime. Empty = none. They use the impact sound's filters: `soundOnPlayers` / `soundOnWorld` / `soundOnMoving`, `hitReplay` and `hitResolved`. `impactMinInterval` drops only the sound, not the particles.
- The Check Window reports `impactParticles` that are not in World simulation space.
- `GunnerRig.impactParticles` と `impactBurst`。当たった点で粒子を弾けさせます(World simulation space、+Z が飛び散る向き)。粒の見た目と寿命は粒子の側で決めます。空なら出しません。絞り込みは着弾音と同じです: `soundOnPlayers` / `soundOnWorld` / `soundOnMoving`、`hitReplay`、`hitResolved`。`impactMinInterval` が間引くのは音だけで、粒子は間引きません。
- 点検ウィンドウが、World simulation space でない `impactParticles` を知らせます。

### Changed / 変更

- `impactClip` may be empty while `impactParticles` is set (particles only), and the other way round (sound only).
- Stream hits always burst the particles (the splash's `onStream` switch is gone). To show fewer, lower `impactBurst`.
- The inspector section **Sounds** is now **Sounds and particles**. `Prefab/GunnerRig.prefab` and `Scene/Gunner.unity` use the rig's new fields instead of a `GunnerHitSplash`.
- `impactClip` が空で `impactParticles` だけ(粒子だけ)、その逆(音だけ)にできます。
- 連射の着弾でも、いつも粒子を出します(しぶきの `onStream` はなくなりました)。多すぎる時は `impactBurst` を減らしてください。
- インスペクタの節 **効果音** は **効果音と粒子** になりました。`Prefab/GunnerRig.prefab` と `Scene/Gunner.unity` は、`GunnerHitSplash` の代わりに rig の新しい欄を使います。

### Removed / 削除

- `GunnerHitSplash` (the component, its Udon program asset, its inspector, its checks, and the **Add Hit Splash** menu and button). Move its particle system to the rig's `impactParticles` and its `burst` to `impactBurst`. Its `sound` and `surfaceOffset` have no replacement: use the rig's impact sound; the offset is fixed at 0.02 m. `GunnerHitReceiver` stays.
- `GunnerHitSplash`(コンポーネント、Udon のプログラムアセット、インスペクタ、点検、メニューとボタンの **Add Hit Splash**)。粒子は rig の `impactParticles` へ、`burst` は `impactBurst` へ移してください。`sound` と `surfaceOffset` の代わりはありません。rig の着弾音を使ってください。面からの浮きは 0.02 m に固定です。`GunnerHitReceiver` は残ります。

## [0.8.0] - unreleased / 未公開

Removals and clean-up. Members nobody called, and fields that a Unity or VRChat feature already covers, are removed.

削除と整理です。呼ぶ者のいないメンバーと、Unity・VRChat の機能で足りる欄を消しました。

### Removed / 削除

- `GunnerSounds` (the component, its Udon program asset, its inspector and the **Add Gunner Sounds** menu and button), and the public Editor class `GunnerAudio` that lives in `GunnerSoundsEditor.cs` (`CreateVoice`, `ConfigureVoice`, `Rolloff`, `SetUpVoices`, `RigsOf`, `SoundsFor`). Use the rig's built-in sounds (`fireClip`, `impactClip` and the other sound fields), which stay.
- `streamWidth`, `streamWidthScales`, `streamTaper`. The rig no longer overwrites the stream particles' Trails > Width over Trail; set the width there on each particle system. A single shot's trail and a stream's line then have the same width; they cannot have separate widths. Rigs that used `streamWidth` get a different line width until the particles are set.
- `streamClearOnHit`. "Stop at first hit" always clears the emitter's flying particles on the first hit (the old default).
- `projectile`, `GunnerHitReceiver._OnOwnerHit` / `_OnOwnerCommit`, `_RetireHit`.
- `mounts`, `_SetMount`. Make the emitter a child of the Transform, or use a VRC Parent Constraint.
- `chainYawLimit`, `chainPitchUpLimit`, `chainPitchDownLimit`. The chain limit is always the per-bone limit times the number of bones (the old behaviour with the fields at 0).
- `_SetAimDirection` and the direction aim mode (`AimDirection`). Use `_SetAimTarget` or `_SetAimAngles`. The synced values of the other aim modes are unchanged.
- `_FireFrom` (use `_FireWith(emitter, 0)`), `_Unregister`, `_ForceStopStream`, `_IsCollisionOn`, `_LiveHitCount`, `_SlotUsed` / `_SlotKind` / `_SlotIndex` / `_SlotPayload` / `_SlotCount`, `_CastKind`, `_CastIndex`.
- `_SolveFor`, `_SolveWithin`, `_SolveFrom`, `_SolveNearest` and `solveDir`, `solveDistance`, `solveCasts`.
- `flightFlown`. `flightLength` and `flightSlot` are now private (`flightOn` stays public).
- `GunnerStream._Step` (the rig calls `_StepMasked`).
- `GunnerSounds`(コンポーネント、Udon のプログラムアセット、インスペクタ、メニューとボタンの **Add Gunner Sounds**)と、`GunnerSoundsEditor.cs` 内の Editor の公開クラス `GunnerAudio`(`CreateVoice`、`ConfigureVoice`、`Rolloff`、`SetUpVoices`、`RigsOf`、`SoundsFor`)。rig に内蔵の効果音(`fireClip`、`impactClip` ほか)は残ります。そちらを使ってください。
- `streamWidth`、`streamWidthScales`、`streamTaper`。rig は連射の粒子の Trails > Width over Trail を上書きしなくなりました。線の太さは各粒子の Trails > Width over Trail で決めます。単発の軌跡も連射の線も同じ幅になり、別々の幅は持てません。`streamWidth` を使っていた rig は、粒子側を直すまで線の太さが変わります。
- `streamClearOnHit`。「1回で止める」は、最初の当たりでいつもその射出口の飛んでいる粒子を消します(これまでの既定)。
- `projectile`、`GunnerHitReceiver._OnOwnerHit` / `_OnOwnerCommit`、`_RetireHit`。
- `mounts`、`_SetMount`。射出口をその Transform の子にするか、VRC Parent Constraint を使ってください。
- `chainYawLimit`、`chainPitchUpLimit`、`chainPitchDownLimit`。鎖の上限は、いつも「骨1本あたりの上限 × 骨の数」です(欄が 0 の時のこれまでの動き)。
- `_SetAimDirection` と向きでの狙い(`AimDirection`)。`_SetAimTarget` か `_SetAimAngles` を使ってください。ほかの狙いの同期する値は変わりません。
- `_FireFrom`(`_FireWith(emitter, 0)` を使う)、`_Unregister`、`_ForceStopStream`、`_IsCollisionOn`、`_LiveHitCount`、`_SlotUsed` / `_SlotKind` / `_SlotIndex` / `_SlotPayload` / `_SlotCount`、`_CastKind`、`_CastIndex`。
- `_SolveFor`、`_SolveWithin`、`_SolveFrom`、`_SolveNearest` と `solveDir`、`solveDistance`、`solveCasts`。
- `flightFlown`。`flightLength` と `flightSlot` は private になりました(`flightOn` は公開のまま)。
- `GunnerStream._Step`(rig は `_StepMasked` を呼びます)。

## [0.7.0] - unreleased / 未公開

### Added / 追加

- `GunnerRig.fireAnimator`: an optional Animator that plays a pre-fire animation. Single shots from `_FireWith(emitter, payload)` then wait: the rig sets the bool `GunnerPre<e>` (e = emitter index) and shoots when an Animation Event calls `_AnimFire` (SendCustomEvent). It gives up after 3 seconds, and when the owner changes, without shooting and clears the bool. Only one shot waits at a time; volleys and streams do not wait. New public methods: `_AnimFire`, `_IsPreFiring`.
- `GunnerAutoAim.firePayloads`: the integer passed to `_FireWith` for each emitter (empty or short = 0). It does nothing while `_IsPreFiring()` is true.
- `GunnerRig.fireAnimator`: 撃つ前のアニメを流す Animator(任意)です。入れると `_FireWith(emitter, payload)` の単発は、すぐには撃たず、bool `GunnerPre<e>`(e = 射出口の番号)を真にして待ち、Animation Event が `_AnimFire`(SendCustomEvent)を呼んだ時に撃ちます。3 秒待っても合図が来ない時と、持ち主が替わった時は、撃たずにやめて bool を戻します。待つのは 1 度に 1 つで、一斉射と連射は待ちません。公開メソッドが増えました: `_AnimFire`、`_IsPreFiring`。
- `GunnerAutoAim.firePayloads`: 射出口ごとに `_FireWith` へ渡す整数です(空・短い所は 0)。`_IsPreFiring()` が真の間は何もしません。

### Changed / 変更

- Nothing existing changes. With `fireAnimator` empty, behaviour is the same as 0.6.0.
- 既存の動きは変わりません。`fireAnimator` が空なら 0.6.0 と同じ動きです。

## [0.6.0] - unreleased / 未公開

### Added / 追加

- `GunnerAutoAim`: a new component that makes a rig aim at and shoot players inside a trigger `BoxCollider`. Put it on the same GameObject as the collider (Is Trigger on), not on the rig's GameObject. Every device keeps the list of players in the zone; only the rig's owner picks the target (the player nearest to the zone centre), picks the emitter nearest to the aim point, aims and fires (`_SetAimEmitters` → `_SetAimTarget` → `_FireWith`). Fields: `rig`, `zone`, `interval` (0.2), `fireInterval` (3), `aimHeight` (0.7), `switchWait` (0.5), `recheckInterval` (1). It has no sync, no network events and no public methods.
- Built-in sounds on `GunnerRig`: `fireClip`, `impactClip`, `fireVolume`, `impactVolume`, `pitchJitter`, `fireVoices`, `impactVoices`, `fireMinInterval`, `impactMinInterval`, `soundOnPlayers`, `soundOnWorld`, `soundOnMoving`, `soundCullDistance`. They play the fire sound at the emitter and the impact sound at the hit point, like `GunnerSounds`. The repeated fire sound during a stream is not included.
- `GunnerAutoAim`: トリガーの `BoxCollider` の中にいる人を、rig に狙わせて撃たせる新しいコンポーネントです。コライダーと同じ GameObject(Is Trigger 入)に付け、rig の GameObject には付けません。範囲内の一覧はどの端末も持ち、狙う相手(範囲の中心に一番近い人)と射出口(狙う点に一番近いもの)を選んで向けて撃つのは rig の持ち主だけです(`_SetAimEmitters` → `_SetAimTarget` → `_FireWith`)。欄: `rig`、`zone`、`interval`(0.2)、`fireInterval`(3)、`aimHeight`(0.7)、`switchWait`(0.5)、`recheckInterval`(1)。同期、ネットワークイベント、公開メソッドは持ちません。
- `GunnerRig` に効果音を内蔵しました: `fireClip`、`impactClip`、`fireVolume`、`impactVolume`、`pitchJitter`、`fireVoices`、`impactVoices`、`fireMinInterval`、`impactMinInterval`、`soundOnPlayers`、`soundOnWorld`、`soundOnMoving`、`soundCullDistance`。`GunnerSounds` と同じく、発射音は射出口で、着弾音は当たった点で鳴ります。連射中の発射音の繰り返しは入っていません。

### Changed / 変更

- Nothing existing changes. With the clips empty, a rig makes no sound, as before. `GunnerSounds` is unchanged; if both the rig and a `GunnerSounds` have clips, the sound plays twice.
- 既存の動きは変わりません。クリップが空なら、これまでどおり rig は音を鳴らしません。`GunnerSounds` は変えていません。rig と `GunnerSounds` の両方にクリップを入れると、音が 2 重に鳴ります。

## [0.5.0] - 2026-11-17

First public release. Versions before 0.5.0 were not published.

最初の公開版です。0.5.0 より前の版は公開していません。

### Added / 追加

- `streamWidthScales`: per-emitter multiplier on the stream line width (`streamWidth`). Empty or 0 means 1x, the same as before.
- `streamClearOnHit`: with "Stop at first hit", choose whether the first hit clears that emitter's flying particles and trails. The default is `true`, the same as before; `false` keeps the running line unbroken. It is read at every hit.
- `streamWidthScales`: 連射の線の幅(`streamWidth`)に、射出口ごとに掛ける倍率です。空か 0 なら 1 倍で、これまでと同じです。
- `streamClearOnHit`: 「最初の着弾で止まる」連射で、最初に当たったときにその射出口の飛んでいる粒と軌跡を消すかを選べます。既定は `true` でこれまでと同じです。`false` にすると、続いている線が途切れません。着弾のたびに読み直します。

### Changed / 変更

- The minimum VRChat Worlds SDK is now 3.10.0 (`com.vrchat.worlds >=3.10.0`).
- 必要な VRChat Worlds SDK を 3.10.0 以上にしました(`com.vrchat.worlds >=3.10.0`)。

## [0.4.0] - unreleased / 非公開

### Added / 追加

- Extra linked bones: `linkExtraBones` / `linkExtraEmitter` add parent bones to a link. The aim angle is split evenly, so every bone turns by the same amount. `chainYawLimit` / `chainPitchUpLimit` / `chainPitchDownLimit` cap the whole chain.
- Per-emitter aim limit: `aimMaxAngles`.
- Emitter sets: `_SetAimEmitters(mask)` chooses which emitters aim, stream and volley. `GunnerHandController.useEmitters` sets it while the ball is held.
- `_FireVolley(payload, mask, offsets)`: a volley with an emitter bit mask and per-emitter angle offsets.
- `firePayload` in `_OnFire`.
- `_SolveWithin`, `_SolveFrom`, `_SolveNearest` and `solveCasts`.
- Smooth follow: `boneMaxSpeed` limits how fast the linked bones turn.
- JSON export and import of number fields (the `GunnerRig` inspector and the component's right-click menu).
- 追加の骨の連動: `linkExtraBones` / `linkExtraEmitter` で、連動に親の骨を足せます。狙いの角度を等分するので、どの骨も同じだけ回ります。`chainYawLimit` / `chainPitchUpLimit` / `chainPitchDownLimit` で、つながり全体の角度に上限を付けられます。
- 射出口ごとの角度の上限: `aimMaxAngles`。
- 狙いの射出口の組: `_SetAimEmitters(mask)` で、狙い・連射・一斉射撃に使う射出口を選べます。`GunnerHandController.useEmitters` は、球を持っている間この組を使います。
- `_FireVolley(payload, mask, offsets)`: 使う射出口(ビットの組)と、射出口ごとの角度のずれを指定する一斉射撃です。
- `_OnFire` で `firePayload` を読めます。
- `_SolveWithin`、`_SolveFrom`、`_SolveNearest` と `solveCasts`。
- なめらかな追従: `boneMaxSpeed` で、連動する骨の回る速さに上限を付けられます。
- 数値の欄を JSON に書き出し、読み込めます(`GunnerRig` のインスペクタと、コンポーネントの右クリックのメニュー)。

## [0.3.0] - unreleased / 非公開

### Added / 追加

- `GunnerRig._CastKind()` and `GunnerRig._CastIndex()`: read what the last line-of-fire check hit (kind and index), so a shooter can skip a shot that would hit the wrong target.
- `GunnerRig._CastKind()` と `GunnerRig._CastIndex()`: 直前の射線の判定が何に当たったか(種類と番号)を読み取れます。撃つ前に、狙っていない相手に当たる射撃を取りやめるのに使えます。

### Changed / 変更

- Code comments are now in Japanese. Identifiers and the API are unchanged.
- コードのコメントを日本語にしました。識別子と API は変わりません。

## [0.2.0] - unreleased / 非公開

### Added / 追加

- `GunnerHitReceiver._OnFire`: called when a shot or stream is fired.
- `GunnerSounds`: plays fire and impact sounds from the `_OnFire` and hit notifications (the package ships no audio clips; assign your own).
- `GunnerHitReceiver._OnFire`: 単発と連射を撃ったときに呼ばれます。
- `GunnerSounds`: `_OnFire` と着弾の知らせで、発射音と着弾音を鳴らします(音声ファイルは同梱しません。各自で割り当ててください)。

## [0.1.0] - unreleased / 非公開

### Added / 追加

- `GunnerRig`: emitters, aiming, single shots, synced hit records, and receiver registration.
- `GunnerStream`: streams that stop at the first hit, or keep hitting.
- `GunnerHitReceiver`, `GunnerHitSplash`, and the hand controller (`GunnerHandController` and its parts).
- Editor: setup menu (`GameObject/Gunner/`), inspectors, check window, Japanese and English UI.
- `GunnerRig`: 射出口、狙い、単発、着弾の記録の同期、受け手の登録。
- `GunnerStream`: 最初の着弾で止まる連射と、当たり続ける連射。
- `GunnerHitReceiver`、`GunnerHitSplash`、持って撃つ球(`GunnerHandController` と部品)。
- エディタ: 設置メニュー(`GameObject/Gunner/`)、インスペクタ、点検の窓、日本語と英語の UI。
