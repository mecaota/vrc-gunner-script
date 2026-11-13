using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using E = Mecaota.Gunner.EditorTools.GunnerCheckLevel;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>gunner 自身の部品の点検。</summary>
    [InitializeOnLoad]
    public sealed class GunnerBuiltinChecks : IGunnerCheckProvider
    {
        const int ManyHits = GunnerRig.SyncSlots;

        static GunnerBuiltinChecks() => GunnerChecks.Register(new GunnerBuiltinChecks());

        public void Collect(Component c, List<GunnerCheck> into)
        {
            switch (c)
            {
                case GunnerRig rig: Rig(rig, into); break;
                case GunnerHandController hand: Hand(hand, into); break;
                case GunnerPickupInput input: PickupInput(input, into); break;
                case GunnerHitSplash splash: Splash(splash, into); break;
                case GunnerSounds sounds: Sounds(sounds, into); break;
                case GunnerStream stream: Stream(stream, into); break;
            }
        }

        // ---------------- rig ----------------

        static void Rig(GunnerRig rig, List<GunnerCheck> into)
        {
            var emitters = rig.emitters ?? new Transform[0];
            if (emitters.Length == 0)
                into.Add(new GunnerCheck(E.Error, "check.rig.no_emitter", rig,
                    () => GunnerSetup.AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation)));
            if (emitters.Length > GunnerRig.MaxEmitters)
                into.Add(new GunnerCheck(E.Error, "check.rig.too_many_emitters", rig, null, GunnerRig.MaxEmitters.ToString()));

            bool nullEmitter = false;
            for (int i = 0; i < emitters.Length; i++) if (emitters[i] == null) nullEmitter = true;
            if (nullEmitter)
                into.Add(new GunnerCheck(E.Error, "check.rig.null_emitter", rig,
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "emitters", "shotParticles", "mounts", "linkBones", "aimMaxAngles"))));

            var particles = rig.shotParticles ?? new ParticleSystem[0];
            for (int i = 0; i < emitters.Length && i < GunnerRig.MaxEmitters; i++)
            {
                var em = emitters[i];
                if (em == null) continue;
                var ps = i < particles.Length ? particles[i] : null;
                string label = i + " (" + em.name + ")";
                if (ps == null)
                {
                    int index = i;
                    into.Add(new GunnerCheck(E.Warning, "check.rig.no_particle", em, () =>
                    {
                        var created = GunnerEditorUtil.CreateShotParticles(em);
                        GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.SetAt(so.FindProperty("shotParticles"), index, created));
                    }, label));
                    continue;
                }
                var main = ps.main;
                if (main.simulationSpace != ParticleSystemSimulationSpace.World)
                    into.Add(new GunnerCheck(E.Error, "check.particle.not_world", ps, () => GunnerEditorUtil.MakeWorldSpace(ps), label));
                if (main.playOnAwake || main.loop)
                    into.Add(new GunnerCheck(E.Warning, "check.particle.autoplay", ps, () => GunnerEditorUtil.StopAutoPlay(ps), label));
                if (rig.fireMode == GunnerRig.FireStream && rig.streamJoin == GunnerStream.JoinLine && !ps.trails.enabled)
                    into.Add(new GunnerCheck(E.Warning, "check.rig.trails_off", ps, () => GunnerEditorUtil.EnableTrails(ps), label));
            }

            if (rig.fireMode == GunnerRig.FireStream && rig.stream == null)
                into.Add(new GunnerCheck(E.Error, "check.rig.no_stream", rig, () => GunnerSetup.EnsureStream(rig)));

            if (rig.maxHits < 1)
                into.Add(new GunnerCheck(E.Error, "check.rig.hits_zero", rig,
                    () => GunnerEditorUtil.Edit(rig, so => so.FindProperty("maxHits").intValue = 16)));
            else if (rig.maxHits > ManyHits)
                into.Add(new GunnerCheck(E.Warning, "check.rig.many_hits", rig, null, ManyHits.ToString()));

            if (rig.GetComponent<VRCObjectSync>() != null)
                into.Add(new GunnerCheck(E.Error, "check.rig.object_sync", rig));

            Receivers(rig, into);
            ExtraBones(rig, into);
            AimAngles(rig, into);

            if (rig.movers != null)
                foreach (var m in rig.movers)
                    if (m == null)
                    {
                        into.Add(new GunnerCheck(E.Warning, "check.rig.null_mover", rig,
                            () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "movers"))));
                        break;
                    }
        }

        static void Receivers(GunnerRig rig, List<GunnerCheck> into)
        {
            var listed = rig.receivers ?? new GunnerHitReceiver[0];
            bool hasNull = false;
            foreach (var r in listed) if (r == null) hasNull = true;
            if (hasNull)
                into.Add(new GunnerCheck(E.Warning, "check.rig.null_receiver", rig,
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "receivers"))));

            // projectile に指定した受け手は、_OnOwnerHit は一覧に無くても受けるが、_OnHit は一覧に入っていないと受けない。
            if (rig.projectile != null && System.Array.IndexOf(listed, rig.projectile) < 0)
                into.Add(new GunnerCheck(E.Warning, "check.rig.projectile_unlisted", rig, () => AddReceiver(rig, rig.projectile)));

            foreach (var r in rig.GetComponentsInChildren<GunnerHitReceiver>(true))
            {
                if (System.Array.IndexOf(listed, r) >= 0 || r == rig.projectile) continue;
                if (r is GunnerHitSplash s && s.targetRig == rig) continue; // Start で自分を登録する
                if (r is GunnerSounds snd && snd.rigs != null && System.Array.IndexOf(snd.rigs, rig) >= 0) continue; // 開始時に自分で登録する
                var receiver = r;
                into.Add(new GunnerCheck(E.Warning, "check.rig.receiver_unlisted", r, () => AddReceiver(rig, receiver), r.name));
            }
        }

        // 追加の連動ボーン。実行時は、赤の骨を無視して残りで鎖を作る。
        const int LongChain = 6;

        static void ExtraBones(GunnerRig rig, List<GunnerCheck> into)
        {
            var bones = rig.linkExtraBones;
            if (bones == null || bones.Length == 0) return;
            var owners = rig.linkExtraEmitter ?? new int[0];
            int emitterCount = rig.emitters != null ? rig.emitters.Length : 0;
            var leads = new HashSet<Transform>();
            for (int e = 0; e < emitterCount; e++)
            {
                var lead = GunnerRigEditor.LeadBone(rig, e);
                if (lead != null) leads.Add(lead);
            }
            var chainOf = new Dictionary<Transform, Transform>(); // 追加の骨 → その鎖の先頭の骨
            var counts = new Dictionary<Transform, int>();
            bool hasNull = false;
            for (int k = 0; k < bones.Length; k++)
            {
                var bone = bones[k];
                if (bone == null)
                {
                    hasNull = true;
                    continue;
                }
                int index = k;
                System.Action remove = () => GunnerEditorUtil.Edit(rig, so => GunnerRigEditor.RemoveExtraAt(so, index));
                string label = k + " (" + bone.name + ")";
                int emitter = k < owners.Length ? owners[k] : -1;
                var lead = GunnerRigEditor.LeadBone(rig, emitter);
                if (lead == null)
                {
                    into.Add(new GunnerCheck(E.Error, "check.rig.extra_no_link", rig, remove, label));
                    continue;
                }
                if (bone == lead || !lead.IsChildOf(bone))
                {
                    into.Add(new GunnerCheck(E.Error, "check.rig.extra_not_ancestor", rig, remove, label));
                    continue;
                }
                if (leads.Contains(bone))
                {
                    into.Add(new GunnerCheck(E.Error, "check.rig.extra_shared", rig, remove, label));
                    continue;
                }
                if (chainOf.TryGetValue(bone, out var other))
                {
                    into.Add(new GunnerCheck(E.Error, other == lead ? "check.rig.extra_duplicate" : "check.rig.extra_shared", rig, remove, label));
                    continue;
                }
                chainOf[bone] = lead;
                counts[lead] = (counts.TryGetValue(lead, out int c) ? c : 0) + 1;
            }
            if (hasNull)
                into.Add(new GunnerCheck(E.Warning, "check.rig.extra_null", rig,
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "linkExtraBones", "linkExtraEmitter"))));
            foreach (var pair in counts)
            {
                int total = pair.Value + 1; // 先頭の骨を含む
                if (total > LongChain)
                    into.Add(new GunnerCheck(E.Warning, "check.rig.chain_long", rig, null, pair.Key.name + " (" + total + ")"));
            }
        }

        // 射出口ごとの狙える角度: 射出口より長い配列と、180 度を超える値を黄で知らせる。
        static void AimAngles(GunnerRig rig, List<GunnerCheck> into)
        {
            // 連動する射出口の狙える角度は、およそ 170 度まで(180 度の真後ろでは回す軸が決まらず、骨が跳ぶ)。
            // 180 度を超える値は下の点検が先に知らせるので、ここは 170 度を超え 180 度以下だけ。
            const float LinkMax = 170f;
            if (rig.aimMaxAngle > LinkMax && rig.aimMaxAngle <= 180f)
                into.Add(new GunnerCheck(E.Warning, "check.rig.angle_link", rig,
                    () => GunnerEditorUtil.Edit(rig, so => so.FindProperty("aimMaxAngle").floatValue = LinkMax),
                    "- (" + rig.aimMaxAngle.ToString("0.#") + ")"));
            var angles = rig.aimMaxAngles;
            if (angles == null || angles.Length == 0) return;
            for (int i = 0; i < angles.Length; i++)
            {
                if (angles[i] <= LinkMax || angles[i] > 180f) continue;
                into.Add(new GunnerCheck(E.Warning, "check.rig.angle_link", rig, () => GunnerEditorUtil.Edit(rig, so =>
                {
                    var arr = so.FindProperty("aimMaxAngles");
                    for (int k = 0; k < arr.arraySize; k++)
                    {
                        var el = arr.GetArrayElementAtIndex(k);
                        if (el.floatValue > LinkMax && el.floatValue <= 180f) el.floatValue = LinkMax;
                    }
                }), i + " (" + angles[i].ToString("0.#") + ")"));
                break;
            }
            int count = rig.emitters != null ? rig.emitters.Length : 0;
            if (angles.Length > count)
                into.Add(new GunnerCheck(E.Warning, "check.rig.angles_long", rig,
                    () => GunnerEditorUtil.Edit(rig, so => so.FindProperty("aimMaxAngles").arraySize = count),
                    angles.Length + " > " + count));
            for (int i = 0; i < angles.Length; i++)
            {
                if (angles[i] <= 180f) continue;
                into.Add(new GunnerCheck(E.Warning, "check.rig.angle_over", rig, () => GunnerEditorUtil.Edit(rig, so =>
                {
                    var arr = so.FindProperty("aimMaxAngles");
                    for (int k = 0; k < arr.arraySize; k++)
                    {
                        var el = arr.GetArrayElementAtIndex(k);
                        if (el.floatValue > 180f) el.floatValue = 180f;
                    }
                }), i + " (" + angles[i].ToString("0.#") + ")"));
                break;
            }
        }

        static void AddReceiver(GunnerRig rig, GunnerHitReceiver r)
        {
            GunnerEditorUtil.Edit(rig, so =>
            {
                var list = so.FindProperty("receivers");
                if (!GunnerEditorUtil.Contains(list, r)) GunnerEditorUtil.Append(list, r);
            });
        }

        // ---------------- 手持ち ----------------

        static void Hand(GunnerHandController hand, List<GunnerCheck> into)
        {
            if (hand.rig == null)
            {
                var rigs = GunnerEditorUtil.FindInScenes<GunnerRig>();
                System.Action fix = null;
                if (rigs.Count == 1) fix = () => GunnerEditorUtil.Edit(hand, so => so.FindProperty("rig").objectReferenceValue = rigs[0]);
                into.Add(new GunnerCheck(E.Error, "check.hand.no_rig", hand, fix));
            }
            if (hand.input == null)
                into.Add(new GunnerCheck(E.Error, "check.hand.no_input", hand, () =>
                {
                    var input = GunnerEditorUtil.GetOrAddUdon<GunnerPickupInput>(hand.gameObject);
                    GunnerEditorUtil.Edit(input, so => so.FindProperty("controller").objectReferenceValue = hand);
                    GunnerEditorUtil.Edit(hand, so => so.FindProperty("input").objectReferenceValue = input);
                }));
            else if (hand.input.controller != hand)
                into.Add(new GunnerCheck(E.Error, "check.hand.input_unwired", hand,
                    () => GunnerEditorUtil.Edit(hand.input, so => so.FindProperty("controller").objectReferenceValue = hand)));
            if (hand.aim == null)
                into.Add(new GunnerCheck(E.Warning, "check.hand.no_aim", hand, () =>
                {
                    var aim = GunnerEditorUtil.GetOrAddUdon<GunnerStickAim>(hand.gameObject);
                    GunnerEditorUtil.Edit(hand, so => so.FindProperty("aim").objectReferenceValue = aim);
                }));
            if (hand.hold == null)
                into.Add(new GunnerCheck(E.Warning, "check.hand.no_hold", hand, () =>
                {
                    var hold = GunnerEditorUtil.GetOrAddUdon<GunnerMoveHold>(hand.gameObject);
                    GunnerEditorUtil.Edit(hand, so => so.FindProperty("hold").objectReferenceValue = hold);
                }));

            var rig = hand.rig;
            if (rig != null)
            {
                if (hand.collideMode == GunnerHandController.CollideAfterRelease && rig.fireMode != GunnerRig.FireStream)
                    into.Add(new GunnerCheck(E.Warning, "check.hand.window_single", hand,
                        () => GunnerEditorUtil.Edit(hand, so => so.FindProperty("collideMode").intValue = GunnerHandController.CollideAlways)));
                // 射出口が2つ以上の連射の rig で組を決めていない球は、全部の射出口から連射する。
                if (hand.useEmitters == 0 && rig.fireMode == GunnerRig.FireStream && rig.emitters != null && rig.emitters.Length >= 2)
                    into.Add(new GunnerCheck(E.Warning, "check.hand.all_emitters", hand, null, rig.emitters.Length.ToString()));
                var ball = hand.input != null ? hand.input.GetComponent<Collider>() : null;
                if (ball != null && GunnerEditorUtil.InMask(rig.hitMask, ball.gameObject.layer))
                {
                    int layer = ball.gameObject.layer;
                    into.Add(new GunnerCheck(E.Warning, "check.hand.mask_hits_ball", hand,
                        () => GunnerSetup.ExcludeLayer(rig, layer), LayerMask.LayerToName(layer)));
                }
            }
        }

        static void PickupInput(GunnerPickupInput input, List<GunnerCheck> into)
        {
            if (input.controller == null)
            {
                var hand = input.GetComponent<GunnerHandController>();
                System.Action fix = null;
                if (hand != null) fix = () => GunnerEditorUtil.Edit(input, so => so.FindProperty("controller").objectReferenceValue = hand);
                into.Add(new GunnerCheck(E.Error, "check.input.no_controller", input, fix));
            }
            var pickup = input.GetComponent<VRC_Pickup>();
            if (pickup == null)
            {
                into.Add(new GunnerCheck(E.Error, "check.input.no_pickup", input));
                return;
            }
            if (input.pickup != null && input.pickup != pickup)
                into.Add(new GunnerCheck(E.Error, "check.input.pickup_elsewhere", input,
                    () => GunnerEditorUtil.Edit(input, so => so.FindProperty("pickup").objectReferenceValue = pickup)));
            if (pickup.AutoHold != VRC_Pickup.AutoHoldMode.Yes)
                into.Add(new GunnerCheck(E.Error, "check.input.autohold", pickup, () =>
                {
                    Undo.RecordObject(pickup, "Gunner: AutoHold");
                    pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
                    EditorUtility.SetDirty(pickup);
                }));
            if (input.GetComponent<VRCObjectSync>() == null)
                into.Add(new GunnerCheck(E.Warning, "check.input.no_object_sync", input,
                    () => Undo.AddComponent<VRCObjectSync>(input.gameObject)));
        }

        // ---------------- 受け手と連射 ----------------

        static void Splash(GunnerHitSplash splash, List<GunnerCheck> into)
        {
            if (splash.splash == null) into.Add(new GunnerCheck(E.Error, "check.splash.no_particle", splash));
            else if (splash.splash.main.simulationSpace != ParticleSystemSimulationSpace.World)
            {
                var ps = splash.splash;
                into.Add(new GunnerCheck(E.Error, "check.particle.not_world", ps, () => GunnerEditorUtil.MakeWorldSpace(ps), ps.name));
            }
            if (splash.targetRig == null && !ListedAnywhere(splash))
            {
                var rig = GunnerSetup.RigFor(splash.gameObject);
                System.Action fix = null;
                if (rig != null) fix = () => GunnerEditorUtil.Edit(splash, so => so.FindProperty("targetRig").objectReferenceValue = rig);
                into.Add(new GunnerCheck(E.Warning, "check.receiver.no_rig", splash, fix));
            }
        }

        static bool ListedAnywhere(GunnerHitReceiver r)
        {
            foreach (var rig in GunnerEditorUtil.FindInScenes<GunnerRig>())
                if (rig.receivers != null && System.Array.IndexOf(rig.receivers, r) >= 0) return true;
            return false;
        }

        // クリップ未設定は点検に出さない(無音のまま使う構成もある)。インスペクタで知らせるだけにする。
        static void Sounds(GunnerSounds sounds, List<GunnerCheck> into)
        {
            var rigs = GunnerAudio.RigsOf(sounds);
            if (rigs.Count == 0)
            {
                var parent = GunnerSetup.RigFor(sounds.gameObject);
                System.Action fix = null;
                if (parent != null) fix = () => GunnerEditorUtil.Edit(sounds, so => GunnerEditorUtil.Append(so.FindProperty("rigs"), parent));
                into.Add(new GunnerCheck(E.Warning, "check.receiver.no_rig", sounds, fix));
            }
            if (sounds.rigs != null && System.Array.IndexOf(sounds.rigs, null) >= 0)
                into.Add(new GunnerCheck(E.Warning, "check.sounds.null_rig", sounds,
                    () => GunnerEditorUtil.Edit(sounds, so => GunnerEditorUtil.RemoveNulls(so, "rigs"))));

            Voices(sounds, sounds.fireVoices, sounds.fireClip, "fireVoices", into);
            Voices(sounds, sounds.impactVoices, sounds.impactClip, "impactVoices", into);

            // 同じ rig を2つで受け持つと、発射音も着弾音も二重に鳴る。
            foreach (var other in GunnerEditorUtil.FindInScenes<GunnerSounds>())
            {
                if (other == sounds) continue;
                foreach (var r in GunnerAudio.RigsOf(other))
                {
                    if (!rigs.Contains(r)) continue;
                    into.Add(new GunnerCheck(E.Warning, "check.sounds.double", sounds, null, r.name));
                    break;
                }
            }

            // しぶきの見本にも音の欄があり、両方にあると着弾で二重に鳴る。
            if (sounds.impactClip == null) return;
            foreach (var splash in GunnerEditorUtil.FindInScenes<GunnerHitSplash>())
            {
                if (splash.sound == null) continue;
                bool shared = false;
                foreach (var r in GunnerAudio.RigsOf(splash)) if (rigs.Contains(r)) shared = true;
                if (!shared) continue;
                var s = splash;
                into.Add(new GunnerCheck(E.Warning, "check.sounds.splash_sound", splash,
                    () => GunnerEditorUtil.Edit(s, so => so.FindProperty("sound").objectReferenceValue = null), splash.name));
            }
        }

        static void Voices(GunnerSounds sounds, AudioSource[] voices, AudioClip clip, string field, List<GunnerCheck> into)
        {
            string label = GunnerL10n.Tr("GunnerSounds." + field);
            float reach = GunnerAudio.ReachOf(sounds);
            if (voices == null || voices.Length == 0)
            {
                if (clip != null)
                    into.Add(new GunnerCheck(E.Warning, "check.sounds.no_voices", sounds, () => GunnerAudio.SetUpVoices(sounds, reach), label));
                return;
            }
            if (System.Array.IndexOf(voices, null) >= 0)
                into.Add(new GunnerCheck(E.Warning, "check.sounds.null_voice", sounds,
                    () => GunnerEditorUtil.Edit(sounds, so => GunnerEditorUtil.RemoveNulls(so, field)), label));
            foreach (var v in voices)
            {
                if (v == null) continue;
                var voice = v;
                var spatial = v.GetComponent<VRCSpatialAudioSource>();
                if (spatial == null)
                    into.Add(new GunnerCheck(E.Warning, "check.sounds.no_spatial", v, () => GunnerAudio.ConfigureVoice(voice, reach), v.name));
                // 実行時は maxDistance を届く距離として遠い音を間引くので、Far と食い違うと間引きが効かない。
                else if (Mathf.Abs(v.maxDistance - spatial.Far) > 0.01f)
                {
                    float far = spatial.Far > 0f ? spatial.Far : reach;
                    into.Add(new GunnerCheck(E.Warning, "check.sounds.reach_mismatch", v, () => GunnerAudio.ConfigureVoice(voice, far), v.name));
                }
            }
        }

        static void Stream(GunnerStream stream, List<GunnerCheck> into)
        {
            foreach (var rig in GunnerEditorUtil.FindInScenes<GunnerRig>())
                if (rig.stream == stream) return;
            into.Add(new GunnerCheck(E.Warning, "check.stream.unused", stream));
        }
    }
}
