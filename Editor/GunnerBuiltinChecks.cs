using System.Collections.Generic;
using System.Linq;
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
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "emitters", "shotParticles", "linkBones", "aimMaxAngles",
                        "fireClips", "fireVolumes", "impactClips", "impactVolumes", "impactParticles", "impactBursts"))));

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

            // 同じパーティクルを複数の射出口で使うことがあるので、1つにつき1回だけ出す。
            foreach (var impact in (rig.impactParticles ?? new ParticleSystem[0]).Distinct())
                if (impact != null && impact.main.simulationSpace != ParticleSystemSimulationSpace.World)
                    into.Add(new GunnerCheck(E.Error, "check.particle.not_world", impact, () => GunnerEditorUtil.MakeWorldSpace(impact), impact.name));

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

            foreach (var r in rig.GetComponentsInChildren<GunnerHitReceiver>(true))
            {
                if (System.Array.IndexOf(listed, r) >= 0) continue;
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

        // ---------------- 連射 ----------------

        static void Stream(GunnerStream stream, List<GunnerCheck> into)
        {
            foreach (var rig in GunnerEditorUtil.FindInScenes<GunnerRig>())
                if (rig.stream == stream) return;
            into.Add(new GunnerCheck(E.Warning, "check.stream.unused", stream));
        }
    }
}
