# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package uses [Semantic Versioning](https://semver.org/). While the version is 0.x, minor versions may break the API.

このファイルの形式は Keep a Changelog に従います。版は Semantic Versioning です。0.x の間は、マイナー版でも API が変わることがあります。

## [Unreleased]

### Breaking changes / 互換性のない変更

- With `fireAnimator` set, an emitter without `preFireClip` now shoots at once instead of waiting for `_AnimFire`. If you built the pre-fire layers by hand, set the clips on the emitters and press **Sync Animator**. Once generated, delete your hand-made layers that use the same parameters, or they run twice.
- `fireAnimator` を入れていても、`preFireClip` が空の射出口は `_AnimFire` を待たずにすぐ撃つようになりました。撃つ前のレイヤーを手で作っていた場合は、射出口にクリップを入れて「Animator にそろえる」を押してください。ボタンで生成したら、同じパラメーターを使う手作りのレイヤーは消してください(二重に動くため)。

### Added / 追加

- `GunnerEmitter.preFireClip` / `postFireClip`, and **Sync Animator** on the rig's inspector. It rebuilds the layers named `Gunner_*` and the parameters `GunnerPre*` in the `fireAnimator`'s Animator Controller from these clips (one layer per pre-fire clip). Each layer gets an Avatar Mask of the objects its clips animate, stored inside the controller. Other layers and parameters are left alone. The `Gunner_` layers go back where the first one was, so keep them together to keep their place among your layers. One Undo reverts a press for one rig.
- `GunnerEmitter.preFireClip` / `postFireClip` と、rig のインスペクターの「Animator にそろえる」。`fireAnimator` の Animator Controller に、これらのクリップから `Gunner_*` のレイヤーと `GunnerPre*` のパラメーターを作り直します(撃つ前のクリップ1つにつきレイヤー1枚)。各レイヤーには、クリップが動かす物だけの Avatar Mask を付け、Controller の中に保存します。ほかのレイヤーとパラメーターには触りません。`Gunner_` のレイヤーは、最初の1枚があった所にまとめて戻るので、一続きに置いておけば、ほかのレイヤーとの上下は変わりません。1回の Undo で、rig 1つぶん押す前に戻ります。
- Checks: the Animator does not match the emitters' clips (press Sync Animator again), and a pre-fire clip without the `_AnimFire` event.
- 点検: Animator が射出口のクリップと合っていないとき(押し直しが要る)、撃つ前のクリップに `_AnimFire` の Event が無いとき。

## [0.15.0] - 2026-11-20

Breaking: the rig's per-emitter arrays are replaced by one `GunnerEmitter` component per emitter. Values in the old arrays are lost on update.

互換性のない変更です。rig の射出口ごとの配列を、射出口1つにつき部品 `GunnerEmitter` 1つに置き換えました。古い配列の値は、更新すると失われます。

### Breaking changes / 互換性のない変更

- `GunnerRig` loses `emitters`, `shotParticles`, `linkBones`, `linkExtraBones`, `linkExtraEmitter` and every other per-emitter array. Each emitter is a `GunnerEmitter` with the same fields in the singular (`pivot` replaces the emitter Transform). List them in the rig's `muzzles`; the order gives the emitter numbers.
- `GunnerRig` から、`emitters`、`shotParticles`、`linkBones`、`linkExtraBones`、`linkExtraEmitter` と、そのほかの射出口ごとの配列を削除しました。射出口は1つずつ `GunnerEmitter` になり、欄の名前は単数形です(射出口の Transform は `pivot` に入れます)。rig の `muzzles` に並べ、その順が射出口の番号になります。
- Linked bones are one list per emitter, `GunnerEmitter.linkBones`, from root to tip. An empty list no longer falls back to the emitter's parent, and the emitter need not be under the tip bone. To keep a link that used the emitter's parent as the tip, put that bone in `linkBones`.
- 連動ボーンは、射出口ごとの1つの一覧 `GunnerEmitter.linkBones`(根元 → 先端)にまとめました。空のとき射出口の親を使うことはなくなり、射出口が先端のボーンの下にある必要もありません。射出口の親を先端として連動していた場合は、そのボーンを `linkBones` に入れてください。
- `GunnerSetup.AddEmitter` returns the new `GunnerEmitter`; its second parameter is the bone to follow (`null` = none).
- `GunnerSetup.AddEmitter` は作った `GunnerEmitter` を返し、2つ目の引数は追従するボーン(`null` なら追従しません)です。
- `GunnerSetup.RigFor`: with no `GunnerRig` on the object or its parents, it uses the nearest Animator (the object itself included) and returns the first rig under it. With several rigs under one Animator, the first one is used.
- `GunnerSetup.RigFor` は、自身か親に `GunnerRig` が無いとき、最も近い Animator(自身を含む)の下で最初に見つかった rig を返します。同じ Animator の下に rig が複数あるときは、最初の1つです。

### Added / 追加

- `GunnerEmitter` inspector and Scene view handles. The rig's inspector lists the emitters; **+ Add emitter** makes one (it follows no bone), and **Remove** only takes it off the list.
- `GunnerEmitter` のインスペクターとシーンビューのハンドル。rig のインスペクターは射出口を一覧にし、「+ 射出口を足す」で作り(ボーンには追従しません)、「外す」は一覧から外すだけです。
- Emitters are made at `rig/muzzles/<name>` with a child `Pivot` (the `pivot`, the part the aim turns). **Add Emitter** on a bone and **Add Emitter At Body Part** add a VRC Parent Constraint that follows the bone, with the pose at that time as the offset. **Add Emitter** also accepts a bone in an armature that sits next to the rig under the same Animator.
- 射出口は `rig/muzzles/<名前>` と子 `Pivot`(`pivot`。狙いで回す部分)として作ります。ボーンの上で使う「Add Emitter」と「Add Emitter At Body Part」は、そのボーンに追従する VRC Parent Constraint を付け、そのときの姿勢を Offset にします。「Add Emitter」は、同じ Animator の下で rig と並ぶ Armature のボーンも使えます。
- Do not use a Transform with a constraint as the `pivot`; the constraint overwrites the aim.
- Constraint の付いた Transform は `pivot` にしないでください。狙いを Constraint が上書きします。
- With `linkBones` empty, **+ Add parent bone** on the `GunnerEmitter` inspector adds the bone its VRC Parent Constraint follows.
- `linkBones` が空のとき、`GunnerEmitter` のインスペクターの「+ 親のボーンを足す」は、VRC Parent Constraint の追従先のボーンを入れます。
- In Play mode in the editor, a change on a `GunnerEmitter` takes effect at once for single shots, sounds, `castRadius` and the like. The values the stream (`GunnerStream`) reads, the fire mode, `streamCollide`, `linkBones` and the rest pose apply from the next Play. `pivot` and `shotParticle` change for single shots at once, but the stream and the rest pose use the old ones until the next Play. To be sure, Play again.
- エディターの Play 中にインスペクターで値を変えると、単発の発射、音、`castRadius` などはすぐ変わります。連射(`GunnerStream`)が読む値、撃ち方、`streamCollide`、`linkBones`、休みの姿勢は、次の Play から効きます。`pivot` と `shotParticle` は単発だけ先に変わり、連射と休みの姿勢は次の Play からです。確実に確かめるときは、Play し直してください。
- Checks: no `pivot`; a `GunnerEmitter` in no rig's list; the `pivot` is not under the tip bone and there is no VRC Parent Constraint; a VRC Parent Constraint with an empty source.
- 点検の追加: `pivot` が空、どの rig の一覧にもない `GunnerEmitter`、`pivot` が先端のボーンの下になく VRC Parent Constraint もない、Source が空の VRC Parent Constraint。

### Removed / 削除

- The checks for per-emitter array lengths.
- 射出口ごとの配列の長さの点検を削除しました。

### Migration / 入れ直しの手順

1. Before updating, note each emitter's values on its card in the rig's inspector, including the linked bones.
2. After updating, for each emitter make `muzzles/<name>` under the rig at the emitter's world pose and add a `GunnerEmitter`. If the emitter was under a bone, add a VRC Parent Constraint there, set its source to that bone and press **Activate**. Move the old emitter GameObject under it keeping its world pose, set it as `pivot`, and enter the values again.
3. If the emitter was a descendant of its tip bone, list `linkBones` from root to tip, ending with that old tip bone (if the tip field was empty and the emitter's parent was a bone under the rig, not the rig itself, end with that parent). Otherwise it was not linked: leave `linkBones` empty.
4. Add the `GunnerEmitter`s to the rig's `muzzles` in the old order so the emitter numbers stay the same.
5. Open **Tools > Gunner > Check Window** and fix what it lists.

1. 更新の前に、rig のインスペクターで、射出口ごとのカードの値(連動ボーンを含む)を控えます。
2. 更新した後、射出口ごとに rig の下へ `muzzles/<名前>` を射出口と同じワールドの姿勢で作り、`GunnerEmitter` を付けます。射出口がボーンの下にあった場合は、そこに VRC Parent Constraint も付け、Source にそのボーンを入れて「Activate」を押します。古い射出口の GameObject を、ワールドの姿勢を保ったままその下へ移して `pivot` に入れ、控えた値を入れ直します。
3. 射出口が先端のボーンの子孫だった場合は、`linkBones` に根元から先端の順に並べ、最後に更新前の先端のボーンを入れます(先端の欄が空で、射出口の親が rig の下のボーン(rig 自身は除く)だった場合は、その親を最後に入れます)。子孫でなかった射出口は連動していなかったので、`linkBones` は空のままにします。
4. rig の `muzzles` に、前と同じ順で `GunnerEmitter` を入れます(射出口の番号を変えないため)。
5. **Tools > Gunner > Check Window** を開き、出た項目を直します。

## [0.14.0] - 2026-11-20

Breaking: `GunnerAutoAim.switchWait` is removed. The value in it is lost on update.

互換性のない変更です。`GunnerAutoAim` の `switchWait` を削除しました。この欄の値は、更新すると失われます。

### Breaking changes / 互換性のない変更

- `switchWait` is removed. `GunnerAutoAim` now aims only just before it shoots, so it no longer holds an emitter between shots.
- `switchWait` を削除しました。`GunnerAutoAim` は撃つ直前にだけ狙うようになり、撃つ合間に射出口を保つことがなくなりました。

### Added / 追加

- `GunnerAutoAim.emitterMask`: the emitters it may aim with (bit i = emitter i, -1 = all). With no emitter in the set, it does not shoot.
- `GunnerAutoAim.emitterMask`: 狙わせる射出口の組です(ビット i = 射出口 i、-1 = 全部)。組に入る射出口がなければ撃ちません。

### Changed / 変更

- `GunnerAutoAim` starts aiming one or two ticks (`interval` each) before each shot (one every `fireInterval`), so the bones have time to turn, then shoots. Between shots the emitters return to rest. The aim is kept while the pre-fire animation plays.
- `GunnerAutoAim` は、`fireInterval` ごとに撃つ、その `interval` 秒の1〜2回前から狙い始め、ボーンが向く時間を取ってから撃ちます。撃つ合間は、射出口が休みの向きへ戻ります。撃つ前アニメの間は、狙いを保ちます。

## [0.13.0] - 2026-11-20

Breaking: `GunnerRig.fireMode` is replaced by the per-emitter `fireModes`. The value in `fireMode` is lost on update.

互換性のない変更です。`GunnerRig` の `fireMode` を、射出口ごとの `fireModes` に置き換えました。`fireMode` の値は、更新すると失われます。

### Breaking changes / 互換性のない変更

- `fireMode` is removed. Set the fire mode (Single or Stream) on each emitter card (`fireModes`, empty = Single). If a rig had `fireMode` set to Stream, set Stream on every card. A rig left on Single needs no change.
- `TriggerDown()` starts the stream on the Stream emitters and fires the Single emitters once, and `TriggerUp()` stops the stream. The hand controller does the same. With `emitter` set to one emitter, it uses that emitter's fire mode, and while held the aim set is that emitter only (`useEmitters` is ignored).
- `Fire()` and `_FireWith(-1, payload)` fire only the Single emitters in the aim set. `_FireWith(emitter, payload)` with an emitter number still fires that emitter, whatever its fire mode. `_FireVolley()` is unchanged.
- A stream comes out only of the Stream emitters in the aim set. With no Stream emitter in the aim set, `StartStream()` does nothing: the stream does not start, nothing is synced and `_OnFire` is not called.
- The hand-held ball (`GunnerHandController`) now aims at the point straight ahead of the ball (as far as the emitter's range, at least 1 m), using `_SetAimTarget` like `GunnerAutoAim`. Stick aiming is removed: `GunnerStickAim`, `GunnerHandController.aim`, `GunnerPickupInput.desktopStick` / `invertVertical`, and `GunnerRig._SetAimAngles`. The package's ball prefab and sample scene are updated. Remove the GunnerStickAim component left on balls you placed in your own scenes (it shows as a missing script).
- The aim is now synced when the aim point moves more than 0.87% of its distance from the rig (about 0.5 degrees seen from the rig; before: more than 0.05 m). This keeps the hand-held ball's small hand movements from being sent all the time.
- On links whose aim limit is over 90 degrees, the bones now follow the hand-held ball with the axis-and-angle form instead of yaw and pitch, the same as for `GunnerAutoAim`.
- **Place Hand Controller** is removed from the `GunnerRig` inspector and the `GameObject/Gunner` menu (`GunnerSetup.PlaceHand` too). The ball components and the sample prefab stay, so the ball can still be used for debugging.
- `fireMode` を削除しました。撃ち方(単発・連射)は、射出口のカードの `fireModes` で決めます(空なら単発)。`fireMode` を連射にしていた rig は、すべてのカードで連射にしてください。単発のままの rig は、変更する必要はありません。
- `TriggerDown()` を呼ぶと、連射の射出口で連射を始め、単発の射出口で1回撃ちます。`TriggerUp()` は連射を止めます。手に持つ球(`GunnerHandController`)も同じように撃ちます。`emitter` に射出口の番号を入れた球は、その射出口の撃ち方で撃ちます。持っている間は、狙う射出口の組をその射出口だけにし、`useEmitters` は使いません。
- `Fire()` と `_FireWith(-1, payload)` は、狙う射出口の組のうち、単発の射出口だけから撃ちます。`_FireWith(emitter, payload)` に射出口の番号を渡したときは、これまでどおり、撃ち方に関係なくその射出口で撃ちます。`_FireVolley()` は変わりません。
- 連射は、狙う射出口の組のうち、連射の射出口だけから出ます。狙う射出口の組に連射の射出口がない場合は、`StartStream()` を呼んでも連射は始まりません。同期も `_OnFire` の呼び出しも行われません。
- `GunnerRig` のインスペクタと `GameObject/Gunner` メニューから **操作用の球を置く**(Place Hand Controller)を削除しました。`GunnerSetup.PlaceHand` も削除しました。操作用の球はデバッグ用として引き続き使えるよう、球のコンポーネントとサンプルのプレハブは残しています。
- 手に持つ球(`GunnerHandController`)は、正面方向に射出口の射程(1 m 以上)だけ離れた点を狙うようにしました。`GunnerAutoAim` と同じく `_SetAimTarget` を使うようにしました。スティックで狙う機能は削除しました。これに伴い、`GunnerStickAim`、`GunnerHandController.aim`、`GunnerPickupInput.desktopStick` / `invertVertical`、`GunnerRig._SetAimAngles` を削除しました。パッケージの球のプレハブとサンプルのシーンは修正済みです。自分のシーンに置いた球に残った `GunnerStickAim` のコンポーネントは、見つからないスクリプト(Missing Script)として表示されるため、手動で削除してください。
- 狙いを送る条件を、「狙う点が 0.05 m を超えて動いたとき」から「狙う点が rig からの距離の 0.87%(rig から見て約 0.5 度に相当)を超えて動いたとき」に変えました。球を持つ手の細かな揺れによって、送信が頻繁に発生するのを防ぐためです。
- 狙える角度が 90 度を超える連動では、手に持つ球で狙うときのボーンの追従の計算を、左右・上下ではなく、軸と角度で行うようにしました(`GunnerAutoAim` と同じです)。

## [0.12.0] - 2026-11-20

Breaking: four `GunnerRig` fields are removed (see "Breaking changes"). Values in them are lost on update.

互換性のない変更です。`GunnerRig` の欄を4つ削除しました(詳細は下の「互換性のない変更」を参照)。これらの欄の値は、更新すると失われます。

### Breaking changes / 互換性のない変更

- `hitPlayers` and `excludeOwner` are removed. Shots always hit players, including the rig's owner, and `GunnerAutoAim` can target the owner. A rig that had `hitPlayers` off or `excludeOwner` on now behaves differently. `excludeOwner` was on by default, including in the package prefab, so rigs placed from the prefab are affected: shots fired from near the shooter (a held object, the shooter's own bones) can now hit the shooter, and there is no way to turn this off.
- `aimMaxAngle` is removed. Set the aim limit on each emitter card (`aimMaxAngles`). An empty or 0 value means 60 degrees. If a rig had `aimMaxAngle` other than 60, enter that value on each card whose aim limit was empty or 0. The hand-held ball's stick aim is now limited by each emitter's aim limit too (before, by `aimMaxAngle`), so a card set above 60 lets the stick turn further.
- `streamCollide` is removed. Set stream collision on each emitter card (`streamCollides`, empty = On). If a rig had `streamCollide` off, set it to Off on every card.
- `hitPlayers` と `excludeOwner` を削除しました。弾は、rig の持ち主も含めて、常に人に当たります。`GunnerAutoAim` も持ち主を狙います。`hitPlayers` を切っていた rig や、`excludeOwner` を入れていた rig は、動きが変わります。`excludeOwner` は既定で入っていて、パッケージのプレハブでも入っていたので、プレハブから置いた rig も動きが変わります。撃つ人の近く(手に持つ物や自分のボーンなど)から出る弾は、撃った本人にも当たるようになり、これを切る方法はありません。
- `aimMaxAngle` を削除しました。可動域は、射出口のカードの `aimMaxAngles` で決めます。空または 0 なら 60 度です。`aimMaxAngle` を 60 以外にしていた rig は、可動域が空または 0 だったカードに、その値を入れ直してください。手に持つ球のスティックで狙える範囲も、射出口ごとの可動域で決まるようになりました(以前は `aimMaxAngle` でした)。カードの可動域を 60 より大きく設定すると、スティックでも最大でその角度まで回せるようになります。
- `streamCollide` を削除しました。連射の当たり判定は、射出口のカードの `streamCollides` で決めます(空なら「記録する」)。`streamCollide` を切っていた rig は、すべてのカードで「記録しない」にしてください。

### Added / 追加

- Each emitter card takes a fire clip and an impact clip (`fireClips`, `impactClips`). When the rig plays a sound, it puts the clip on the source first; empty = the clip already on the source (with a source shared by several emitters, the clip put on it last).
- 射出口のカードに、発射音と着弾音のクリップの欄(`fireClips`、`impactClips`)を足しました。rig が鳴らすときに、そのクリップを音源に入れます。空なら、音源に入っているクリップを鳴らします(音源を複数の射出口で共有していると、最後に入れたクリップが鳴ります)。

### Changed / 変更

- `GunnerRig` inspector: **+ Add parent bone** is above the bone list, which is drawn in one frame with labelled rows. **Remove** and its note are at the bottom of each card. Some field names now say what their values mean (for example, "0 = no limit" on **Maximum speed**), and **Place hand controller** has a tooltip. The **Stream** section is gone: `stream` is under **Shooting**, and stream collision is on the cards. Text in the inspector says "ボーン" instead of "骨" in Japanese.
- In the Scene view, the aim limit handle now appears on every emitter and edits that emitter's `aimMaxAngles` value (before, only emitter 0).
- `GunnerRig` のインスペクタ: **+ 親のボーンを足す** をボーンの一覧の上に移し、一覧を1つの枠で囲んで、各行に名前を付けました。**外す** とその注意書きは、各カードの下に移しました。いくつかの欄の名前に値の意味を書き足し(例: **最高の速さ** に「0 = 上限なし」)、**操作用の球を置く** に説明を付けました。「連射」の節をなくし、`stream` は「射撃」の節へ、連射の当たり判定は各カードへ移しました。インスペクタの文言の「骨」を「ボーン」にそろえました。
- シーンビューの可動域のハンドルを、すべての射出口に出し、各射出口の `aimMaxAngles` の値を変えられるようにしました(以前は射出口0だけでした)。

### Fixed / 修正

- The fire sound at the start of a stream now plays from the first emitter in the aim set (`_SetAimEmitters`) that has a fire sound source, on every device. If no emitter in the aim set has one, no sound plays (before, the first emitter with a fire sound source played it, whatever the set).
- 連射を始めたときの発射音を、狙う射出口の組(`_SetAimEmitters`)のうち、発射音の音源がある先頭の射出口で鳴らすようにしました。どの端末でも同じ射出口で鳴ります。組の中に発射音の音源がある射出口がなければ、音は鳴りません(以前は、組と関係なく、発射音の音源がある先頭の射出口で鳴っていました)。

## [0.11.0] - 2026-11-20

Breaking: most `GunnerRig` fields move to the emitter cards, and the sounds move to AudioSources. Values in the removed fields are lost on update.

互換性のない変更です。`GunnerRig` の欄の多くを射出口のカードへ移し、音は AudioSource で決める形にしました。削除した欄の値は、更新すると失われます。

To update from 0.10.0: before updating, note each rig's values for the fields under "Removed". After updating, set them again as listed there. A card field left empty uses the old default, so only values that differ from the default need to be entered. A value on one card does not apply to the other emitters, except the bone link values of emitters that share a lead bone (the lowest-numbered card's values apply to all of them).

0.10.0 からの更新の手順: 更新の前に、下の「削除」に挙げた欄の値を rig ごとに控えてください。更新した後に、各項目に書いた移し先へ入れ直してください。カードの欄は、空なら以前の既定値で動くので、入れ直すのは既定値と違う値だけで済みます。1枚のカードに入れた値は、ほかの射出口には反映されません。ただし、同じ先頭の骨を使う射出口のボーン連動の値は例外として、番号の最も若いカードの値がすべての射出口に反映されます。

- Fire sound sources play where they are; place each one near its emitter. Impact sound sources are moved to the hit point; place them outside the rig (for example under `GunnerRig/Parts`), not under a linked bone.
- Shots now hit the layers in each emitter particle system's **Collision > Collides With**. Unity's default includes Ignore Raycast, while the old `hitMask` default left it out. To keep the old behaviour, untick Ignore Raycast there (and any layer you had removed from `hitMask`). An emitter without a particle system no longer hits objects (whether it hits players still follows `hitPlayers`); give it a particle system if it should hit objects.
- When emitters share a lead bone, enter the bone link values on the lowest-numbered card; the other cards do not show them. When that card changes (you remove it, or change its lead bone), the new lowest-numbered card's values are used.
- 発射音の音源は、置いた場所で鳴ります。各射出口の近くに置いてください。着弾音の音源は、rig が当たった点へ動かして鳴らします。そのため、連動ボーンの子ではなく、rig の外(例: `GunnerRig/Parts` の下)に置いてください。
- 弾が当たるレイヤーは、射出口のパーティクルの **Collision > Collides With** で決まります。Unity の既定では Ignore Raycast が含まれますが、以前の `hitMask` の既定では含まれていませんでした。今までと同じにするには、Collides With で Ignore Raycast を外してください(`hitMask` で外していたレイヤーも外してください)。パーティクルを入れていない射出口の弾は、物に当たらなくなりました(人に当たるかどうかは、今までどおり `hitPlayers` に従います)。弾を物に当てたい射出口には、パーティクルを入れてください。
- 同じ先頭の骨を使う射出口が複数あるときは、ボーン連動の値を、番号の最も若いカードに入れてください。ほかのカードには、その欄が表示されません。そのカードが変わると(カードを外す、先頭の骨を付け替えるなど)、新たに番号が最も若くなったカードの値が使われます。

### Breaking changes / 互換性のない変更

- Each emitter card takes one fire sound source and one impact sound source (`fireSources`, `impactSources`). The clip, volume, pitch and range are set on those AudioSources. A source plays one sound at a time; the next sound restarts it. Impact particles now show whatever is hit.
- Shot speed, range, shot radius, the stream rate, look, hit mode and hit interval, the sound min intervals and the bone link values are set per emitter on its card (`shotSpeeds`, `maxRanges`, `castRadii`, `streamRates`, `streamJoins`, `streamHitModes`, `streamHitIntervals`, `fireMinIntervals`, `impactMinIntervals`, `boneStrengths`, `boneYawMaxes`, `bonePitchUpMaxes`, `bonePitchDownMaxes`, `boneSmooths`, `boneMinSpeeds`, `boneMaxSpeeds`). Empty = the old default, except the min intervals, which default to 0.
- Shots hit the layers in each emitter particle system's **Collision > Collides With**, even when the Collision module is off. An emitter without a particle system hits no layers (it can still hit players).
- The public Editor API `GunnerValueSchemas.Register` is removed with the JSON feature.
- 射出口のカードに、発射音と着弾音の音源(`fireSources`、`impactSources`)を1つずつ割り当てる形にしました。クリップ・音量・音の高さ・聞こえる距離は、その AudioSource で決めます。1つの音源は一度に1つの音を鳴らし、次の音が来ると最初から鳴らし直します。着弾パーティクルは、当たった相手によらず出ます。
- 弾の速さ・射程・弾の半径、連射の量・見た目・当たり方・当たりの間隔、音の最短間隔、ボーン連動の値を、射出口のカードで決める形にしました(`shotSpeeds`、`maxRanges`、`castRadii`、`streamRates`、`streamJoins`、`streamHitModes`、`streamHitIntervals`、`fireMinIntervals`、`impactMinIntervals`、`boneStrengths`、`boneYawMaxes`、`bonePitchUpMaxes`、`bonePitchDownMaxes`、`boneSmooths`、`boneMinSpeeds`、`boneMaxSpeeds`)。空の欄は以前の既定値で動きます。ただし、最短間隔の既定値は 0 です。
- 弾が当たるレイヤーは、射出口のパーティクルの **Collision > Collides With** で決まります。Collision モジュールを無効にしていても、この値を使います。パーティクルを入れていない射出口の弾は、どのレイヤーにも当たりません(ただし、人には当たります)。
- JSON の機能とともに、Editor の公開 API `GunnerValueSchemas.Register` を削除しました。

### Changed / 変更

- `GunnerRig` inspector: each emitter card and each linked bone has its own frame. Add emitters with **+ Add emitter** below the cards and remove one with **Remove** on its card. Add bones with **+ Add parent bone** below the bone list and remove one with **-** on its row. Units are shown in the field names. The bone link values are under **Link settings** on the card.
- Shot particle systems made by the editor tools leave out layers 2 (Ignore Raycast) and 13 (the hand ball) in **Collides With**. The sample scene uses the new per-emitter fields, and its Shot particles also leave out the same layers. The prefab's Shot particles leave out only layer 2 (Ignore Raycast).
- `GunnerRig` のインスペクタ: 射出口のカードと連動ボーンを、1つずつ枠で囲みました。射出口はカードの下の **+ 射出口を足す** で足し、各カードの **外す** で外します。骨は、一覧の下の **+ 親の骨を足す** で足し、各行の **-** で外します。欄の名前に単位を付けました。ボーン連動の値は、カードの **連動の設定** にあります。
- エディタの道具で作る Shot パーティクルは、**Collides With** からレイヤー 2(Ignore Raycast)と 13(手の球)を外した状態になります。サンプルシーンでは、設定を射出口ごとの新しい欄に移し、Shot パーティクルからも同じレイヤーを外しました。プレハブの Shot パーティクルでは、レイヤー 2(Ignore Raycast)だけを外してあります。

### Removed / 削除

- `shotSpeed`, `maxRange`, `castRadius`. Move them to `shotSpeeds`, `maxRanges`, `castRadii` on each card.
- `hitMask`. Move it to **Collision > Collides With** on each emitter's particle system.
- `streamRate`, `streamJoin`, `streamHitMode`, `streamHitInterval`. Move them to `streamRates`, `streamJoins`, `streamHitModes`, `streamHitIntervals` on each card.
- `fireClips`, `fireVolumes`. Move them to the AudioClip and Volume of the AudioSource in `fireSources`.
- `impactClips`, `impactVolumes`. Move them to the AudioClip and Volume of the AudioSource in `impactSources`.
- `fireVoices`, `impactVoices`. Move one source per emitter to `fireSources`, `impactSources`.
- `pitchJitter`. No replacement; the pitch is the AudioSource's Pitch and no longer varies.
- `soundOnPlayers`, `soundOnWorld`, `soundOnMoving`. No replacement; sounds and impact particles play on every hit.
- `soundCullDistance`. Move it to the Far of the `VRC_SpatialAudioSource` on each source.
- `fireMinInterval`, `impactMinInterval` (old defaults 0.05 / 0.08). Move them to `fireMinIntervals`, `impactMinIntervals` on each card.
- `boneStrength`, `boneYawMax`, `bonePitchUpMax`, `bonePitchDownMax`, `boneSmooth`, `boneMinSpeed`, `boneMaxSpeed`. Move them to `boneStrengths`, `boneYawMaxes`, `bonePitchUpMaxes`, `bonePitchDownMaxes`, `boneSmooths`, `boneMinSpeeds`, `boneMaxSpeeds` on each card.
- JSON export and import of number fields (the inspector's **JSON (values)** section, **Values JSON** in the right-click menu, and `GunnerValueSchemas.Register`).
- `shotSpeed`、`maxRange`、`castRadius`。各カードの `shotSpeeds`、`maxRanges`、`castRadii` へ移してください。
- `hitMask`。各射出口のパーティクルの **Collision > Collides With** へ移してください。
- `streamRate`、`streamJoin`、`streamHitMode`、`streamHitInterval`。各カードの `streamRates`、`streamJoins`、`streamHitModes`、`streamHitIntervals` へ移してください。
- `fireClips`、`fireVolumes`。`fireSources` に入れた AudioSource の AudioClip と Volume へ移してください。
- `impactClips`、`impactVolumes`。`impactSources` に入れた AudioSource の AudioClip と Volume へ移してください。
- `fireVoices`、`impactVoices`。射出口ごとに1つずつ、`fireSources`、`impactSources` へ移してください。
- `pitchJitter`。代わりはありません。音の高さは AudioSource の Pitch で決まり、毎回同じになります。
- `soundOnPlayers`、`soundOnWorld`、`soundOnMoving`。代わりはありません。どの着弾でも、音と着弾パーティクルが出ます。
- `soundCullDistance`。各音源の `VRC_SpatialAudioSource` の Far へ移してください。
- `fireMinInterval`、`impactMinInterval`(以前の既定値は 0.05 / 0.08)。各カードの `fireMinIntervals`、`impactMinIntervals` へ移してください。
- `boneStrength`、`boneYawMax`、`bonePitchUpMax`、`bonePitchDownMax`、`boneSmooth`、`boneMinSpeed`、`boneMaxSpeed`。各カードの `boneStrengths`、`boneYawMaxes`、`bonePitchUpMaxes`、`bonePitchDownMaxes`、`boneSmooths`、`boneMinSpeeds`、`boneMaxSpeeds` へ移してください。
- 数値の欄を JSON に書き出す機能と、JSON から読み込む機能(インスペクタの **JSON(数値)** の節、右クリックのメニューの **Values JSON**、`GunnerValueSchemas.Register`)を削除しました。

## [0.10.0] - 2026-11-19

Breaking: the fire and impact sounds, their volumes and the impact particles are now set per emitter. Values set in 0.9.0 for the six fields below are lost on update.

互換性のない変更です。発射音・着弾音・その音量・着弾のパーティクルを、射出口ごとに設定するようにしました。0.9.0 で下の6つの欄に入れた値は、更新すると消えます。

To update: before updating, note the six values on each rig. After updating, enter them again on each emitter's card. Leave a volume empty for 1. Up to 0.9.0, the impact particles were shared by all emitters. From this version onward, set them on every emitter's card; without them, hits from emitter 1 onward show no particles.

更新の手順: 更新の前に、各 rig の6つの欄の値を控えてください。更新した後に、射出口ごとのカードに入れ直してください。音量は、空のままなら 1 です。0.9.0 までは、着弾パーティクルを全部の射出口で共有していました。更新した後は、各射出口のカードにそれぞれ入れてください。入れていない場合、射出口 1 以降の着弾ではパーティクルが出ません。

### Changed / 変更

- `fireClips`, `impactClips`, `fireVolumes`, `impactVolumes`, `impactParticles` and `impactBursts` are arrays with the same index as `emitters`, shown on each emitter's card. A volume of 0 is silent; a missing volume is 1. A missing or 0 burst is 1. Removing an emitter removes its entries too. The inspector's **Sounds and particles** section is now **Sounds**.
- Value JSON (`GunnerValueBake`): the old keys `impactBurst`, `fireVolume` and `impactVolume` are skipped with a yellow "unknown key" and do not stop the import. The new arrays turn red when their length differs from the current length.
- The `GunnerRig` emitter card now shows its linked bones in a single list, parent to child, with the lead bone last. **+** adds the top bone's parent, **-** removes the selected bone. The "Add n parent bones" button has been removed.
- `fireClips`、`impactClips`、`fireVolumes`、`impactVolumes`、`impactParticles`、`impactBursts` は、`emitters` と同じ番号の配列になり、射出口ごとのカードに表示されます。音量は 0 なら無音、値が無ければ 1 です。パーティクルの数は、値が無いか 0 なら 1 です。射出口を外すと、その番号の値も外れます。インスペクタの **効果音とパーティクル** の節は **効果音** になりました。
- 数値の JSON(`GunnerValueBake`): 古いキーの `impactBurst`、`fireVolume`、`impactVolume` は、黄の「知らないキー」としてスキップされ、読み込みは中断されません。新しい配列の欄は、現在の長さと異なると赤になります。
- `GunnerRig` の射出口のカードで、連動ボーンを1つの一覧にまとめました。親から子の順に並び、最後の行が先頭の骨です。「+」でいちばん上の骨の親を足し、「-」で選んだ骨を外します。「親の骨を n 個足す」ボタンは削除しました。

### Removed / 削除

- `fireClip`. Move it to `fireClips`.
- `impactClip`. Move it to `impactClips`.
- `impactParticles` (one for the rig). Move it to `impactParticles` (one per emitter).
- `impactBurst`. Move it to `impactBursts`.
- `fireVolume`. Move it to `fireVolumes`.
- `impactVolume`. Move it to `impactVolumes`.
- `fireClip`。`fireClips` へ移してください。
- `impactClip`。`impactClips` へ移してください。
- `impactParticles`(rig に1つ)。`impactParticles`(射出口ごと)へ移してください。
- `impactBurst`。`impactBursts` へ移してください。
- `fireVolume`。`fireVolumes` へ移してください。
- `impactVolume`。`impactVolumes` へ移してください。

## [0.9.0] - 2026-11-18

### Added / 追加

- `GunnerRig.impactParticles` and `impactBurst`: particles burst at the hit point. They follow `soundOnPlayers` / `soundOnWorld` / `soundOnMoving`; `impactMinInterval` does not drop them.
- `GunnerRig.impactParticles` と `impactBurst`。当たった点で粒子を弾けさせます。`soundOnPlayers` / `soundOnWorld` / `soundOnMoving` に従い、`impactMinInterval` では間引きません。

### Removed / 削除

- `GunnerHitSplash` and the **Add Hit Splash** menu and button. Move its particle system to `impactParticles` and its `burst` to `impactBurst`.
- `GunnerHitSplash` と、メニューとボタンの **Add Hit Splash**。粒子は `impactParticles` へ、`burst` は `impactBurst` へ移してください。

## [0.8.0] - 2026-11-18

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

## [0.7.0] - released in 0.8.0 / 0.8.0 に含めて公開

### Added / 追加

- `GunnerRig.fireAnimator`: an optional Animator that plays a pre-fire animation. Single shots from `_FireWith(emitter, payload)` then wait: the rig sets the bool `GunnerPre<e>` (e = emitter index) and shoots when an Animation Event calls `_AnimFire` (SendCustomEvent). It gives up after 3 seconds, and when the owner changes, without shooting and clears the bool. Only one shot waits at a time; volleys and streams do not wait. New public methods: `_AnimFire`, `_IsPreFiring`.
- `GunnerAutoAim.firePayloads`: the integer passed to `_FireWith` for each emitter (empty or short = 0). It does nothing while `_IsPreFiring()` is true.
- `GunnerRig.fireAnimator`: 撃つ前のアニメを流す Animator(任意)です。入れると `_FireWith(emitter, payload)` の単発は、すぐには撃たず、bool `GunnerPre<e>`(e = 射出口の番号)を真にして待ち、Animation Event が `_AnimFire`(SendCustomEvent)を呼んだ時に撃ちます。3 秒待っても合図が来ない時と、持ち主が替わった時は、撃たずにやめて bool を戻します。待つのは 1 度に 1 つで、一斉射と連射は待ちません。公開メソッドが増えました: `_AnimFire`、`_IsPreFiring`。
- `GunnerAutoAim.firePayloads`: 射出口ごとに `_FireWith` へ渡す整数です(空・短い所は 0)。`_IsPreFiring()` が真の間は何もしません。

### Changed / 変更

- Nothing existing changes. With `fireAnimator` empty, behaviour is the same as 0.6.0.
- 既存の動きは変わりません。`fireAnimator` が空なら 0.6.0 と同じ動きです。

## [0.6.0] - released in 0.8.0 / 0.8.0 に含めて公開

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
