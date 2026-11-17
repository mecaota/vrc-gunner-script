# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package uses [Semantic Versioning](https://semver.org/). While the version is 0.x, minor versions may break the API.

このファイルの形式は Keep a Changelog に従います。版は Semantic Versioning です。0.x の間は、マイナー版でも API が変わることがあります。

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
