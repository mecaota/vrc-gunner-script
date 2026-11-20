using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
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
                case GunnerEmitter muzzle: Emitter(muzzle, into); break;
                case GunnerHandController hand: Hand(hand, into); break;
                case GunnerPickupInput input: PickupInput(input, into); break;
                case GunnerStream stream: Stream(stream, into); break;
            }
        }

        // ---------------- rig ----------------

        static void Rig(GunnerRig rig, List<GunnerCheck> into)
        {
            var muzzles = rig.muzzles ?? new GunnerEmitter[0];
            if (muzzles.Length == 0)
                into.Add(new GunnerCheck(E.Error, "check.rig.no_emitter", rig,
                    () => GunnerSetup.AddEmitter(rig, null, rig.transform.position, rig.transform.rotation)));
            if (muzzles.Length > GunnerRig.MaxEmitters)
                into.Add(new GunnerCheck(E.Error, "check.rig.too_many_emitters", rig, null, GunnerRig.MaxEmitters.ToString()));
            if (muzzles.Contains(null))
                into.Add(new GunnerCheck(E.Error, "check.rig.null_emitter", rig,
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "muzzles"))));

            // 着弾音の音源は当たった点へ動かすので、rig の下にあると鳴っている間に rig と一緒に動き、rig や射出口に付けるとそれごと動かしてしまう。
            foreach (var source in muzzles.Where(m => m != null).Select(m => m.impactSource).Distinct())
                if (source != null && source.transform.IsChildOf(rig.transform))
                    into.Add(new GunnerCheck(E.Warning, "check.rig.impact_in_rig", source, null, source.name));

            if (StreamCount(rig, 0) > 0 && rig.stream == null)
                into.Add(new GunnerCheck(E.Error, "check.rig.no_stream", rig, () => GunnerSetup.EnsureStream(rig)));

            if (rig.maxHits < 1)
                into.Add(new GunnerCheck(E.Error, "check.rig.hits_zero", rig,
                    () => GunnerEditorUtil.Edit(rig, so => so.FindProperty("maxHits").intValue = 16)));
            else if (rig.maxHits > ManyHits)
                into.Add(new GunnerCheck(E.Warning, "check.rig.many_hits", rig, null, ManyHits.ToString()));

            if (rig.GetComponent<VRCObjectSync>() != null)
                into.Add(new GunnerCheck(E.Error, "check.rig.object_sync", rig));

            Receivers(rig, into);
            SharedBones(rig, into);
            PreFireParams(rig, muzzles, into);

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

        // パラメーターが無いと撃つ前のアニメが流れず、合図を待ったまま撃てない。
        static void PreFireParams(GunnerRig rig, GunnerEmitter[] muzzles, List<GunnerCheck> into)
        {
            var controller = GunnerAnimatorSync.ControllerOf(rig);
            if (controller == null) return;
            var names = new HashSet<string>(controller.parameters.Select(p => p.name));
            for (int i = 0; i < muzzles.Length; i++)
                if (muzzles[i] != null && muzzles[i].preFireClip != null && !names.Contains(GunnerAnimatorSync.ParamPrefix + i))
                {
                    into.Add(new GunnerCheck(E.Warning, "check.rig.animator_unsynced", rig, () => GunnerAnimatorSync.Sync(rig)));
                    return;
                }
        }

        const int LongChain = 6;

        // 鎖は同じ先端のボーンを持つ射出口で共有する。別の鎖にも入っているボーンは、回転が二重に書かれる。
        static void SharedBones(GunnerRig rig, List<GunnerCheck> into)
        {
            var muzzles = (rig.muzzles ?? new GunnerEmitter[0]).Where(m => GunnerEmitterEditor.Tip(m) != null).ToList();
            var tips = new HashSet<Transform>(muzzles.Select(GunnerEmitterEditor.Tip));
            var chainOf = new Dictionary<Transform, Transform>(); // 先端より上のボーン → その鎖の先端のボーン
            var counts = new Dictionary<Transform, int>();
            foreach (var m in muzzles)
            {
                var tip = GunnerEmitterEditor.Tip(m);
                for (int k = 0; k < m.linkBones.Length - 1; k++)
                {
                    var bone = m.linkBones[k];
                    if (bone == null || bone == tip || !tip.IsChildOf(bone)) continue;
                    if (tips.Contains(bone) || (chainOf.TryGetValue(bone, out var other) && other != tip))
                    {
                        into.Add(new GunnerCheck(E.Error, "check.rig.extra_shared", m, null, m.name + ": " + bone.name));
                        continue;
                    }
                    if (chainOf.ContainsKey(bone)) continue;
                    chainOf[bone] = tip;
                    counts[tip] = (counts.TryGetValue(tip, out int c) ? c : 0) + 1;
                }
            }
            foreach (var pair in counts)
            {
                int total = pair.Value + 1; // 先端のボーンを含む
                if (total > LongChain)
                    into.Add(new GunnerCheck(E.Warning, "check.rig.chain_long", rig, null, pair.Key.name + " (" + total + ")"));
            }
        }

        // ---------------- 射出口 ----------------

        static void Emitter(GunnerEmitter m, List<GunnerCheck> into)
        {
            if (GunnerEmitterEditor.RigOf(m, out _) == null)
            {
                var parent = GunnerSetup.RigFor(m.gameObject);
                System.Action fix = null;
                if (parent != null) fix = () => GunnerEditorUtil.Edit(parent, so => GunnerEditorUtil.Append(so.FindProperty("muzzles"), m));
                into.Add(new GunnerCheck(E.Warning, "check.emitter.unlisted", m, fix));
            }
            // Constraint の付いた Transform を pivot にすると、狙いが上書きされる。
            if (m.pivot == null)
                into.Add(new GunnerCheck(E.Error, "check.emitter.no_pivot", m, m.GetComponent<VRCParentConstraint>() != null ? null :
                    (System.Action)(() => GunnerEditorUtil.Edit(m, so => so.FindProperty("pivot").objectReferenceValue = m.transform))));

            var ps = m.shotParticle;
            if (ps == null)
            {
                Transform at = m.pivot != null ? m.pivot : m.transform;
                into.Add(new GunnerCheck(E.Warning, "check.rig.no_particle", m, () =>
                {
                    var created = GunnerEditorUtil.CreateShotParticles(at);
                    GunnerEditorUtil.Edit(m, so => so.FindProperty("shotParticle").objectReferenceValue = created);
                }, m.name));
            }
            else
            {
                var main = ps.main;
                if (main.simulationSpace != ParticleSystemSimulationSpace.World)
                    into.Add(new GunnerCheck(E.Error, "check.particle.not_world", ps, () => GunnerEditorUtil.MakeWorldSpace(ps), ps.name));
                if (main.playOnAwake || main.loop)
                    into.Add(new GunnerCheck(E.Warning, "check.particle.autoplay", ps, () => GunnerEditorUtil.StopAutoPlay(ps), ps.name));
                if (m.fireMode == GunnerRig.FireStream && m.streamJoin == GunnerStream.JoinLine && !ps.trails.enabled)
                    into.Add(new GunnerCheck(E.Warning, "check.rig.trails_off", ps, () => GunnerEditorUtil.EnableTrails(ps), m.name));
            }

            var impact = m.impactParticle;
            if (impact != null && impact.main.simulationSpace != ParticleSystemSimulationSpace.World)
                into.Add(new GunnerCheck(E.Error, "check.particle.not_world", impact, () => GunnerEditorUtil.MakeWorldSpace(impact), impact.name));

            // 連動する射出口の狙える角度は、およそ 170 度まで(180 度の真後ろでは回す軸が決まらず、ボーンが跳ぶ)。
            const float LinkMax = 170f;
            float angle = m.aimMaxAngle;
            if (angle > 180f)
                into.Add(new GunnerCheck(E.Warning, "check.rig.angle_over", m,
                    () => GunnerEditorUtil.Edit(m, so => so.FindProperty("aimMaxAngle").floatValue = 180f), angle.ToString("0.#")));
            else if (angle > LinkMax)
                into.Add(new GunnerCheck(E.Warning, "check.rig.angle_link", m,
                    () => GunnerEditorUtil.Edit(m, so => so.FindProperty("aimMaxAngle").floatValue = LinkMax), angle.ToString("0.#")));

            var clip = m.preFireClip;
            if (clip != null && !AnimationUtility.GetAnimationEvents(clip).Any(e => e.functionName == "SendCustomEvent" && e.stringParameter == nameof(GunnerRig._AnimFire)))
                into.Add(new GunnerCheck(E.Warning, "check.emitter.no_anim_fire", m, null, clip.name));

            LinkBones(m, into);
            Constraint(m, into);
        }

        // 先端のボーンの下にない射出口は、Parent Constraint が無いとボーンに付いて行かない。
        static void Constraint(GunnerEmitter m, List<GunnerCheck> into)
        {
            var c = m.GetComponent<VRCParentConstraint>();
            if (c == null)
            {
                var tip = GunnerEmitterEditor.Tip(m);
                if (tip != null && m.pivot != null && !m.pivot.IsChildOf(tip))
                    into.Add(new GunnerCheck(E.Warning, "check.emitter.no_constraint", m,
                        m.pivot != m.transform ? () => GunnerSetup.Follow(m, tip) : (System.Action)null, tip.name));
                return;
            }
            bool empty = c.Sources.Count == 0;
            for (int i = 0; i < c.Sources.Count; i++) if (c.Sources[i].SourceTransform == null) empty = true;
            if (empty) into.Add(new GunnerCheck(E.Warning, "check.emitter.constraint_no_source", c));
        }

        // 実行時は、赤のボーンを無視して残りで鎖を作る。
        static void LinkBones(GunnerEmitter m, List<GunnerCheck> into)
        {
            var bones = m.linkBones ?? new Transform[0];
            if (bones.Contains(null))
                into.Add(new GunnerCheck(E.Warning, "check.rig.extra_null", m,
                    () => GunnerEditorUtil.Edit(m, so => GunnerEditorUtil.RemoveNulls(so, "linkBones"))));
            var tip = GunnerEmitterEditor.Tip(m);
            if (tip == null) return;
            var seen = new HashSet<Transform>();
            for (int k = 0; k < bones.Length - 1; k++)
            {
                var bone = bones[k];
                if (bone == null) continue;
                int index = k;
                System.Action remove = () => GunnerEditorUtil.Edit(m, so => GunnerEditorUtil.RemoveAt(so.FindProperty("linkBones"), index));
                string label = k + " (" + bone.name + ")";
                if (!seen.Add(bone) || bone == tip)
                    into.Add(new GunnerCheck(E.Error, "check.rig.extra_duplicate", m, remove, label));
                else if (!tip.IsChildOf(bone))
                    into.Add(new GunnerCheck(E.Error, "check.rig.extra_not_ancestor", m, remove, label));
            }
        }

        static bool IsStream(GunnerRig rig, int i) => rig.muzzles != null && i < rig.muzzles.Length && rig.muzzles[i] != null && rig.muzzles[i].fireMode == GunnerRig.FireStream;

        // mask: 数える射出口の組(0 = 全部)。
        static int StreamCount(GunnerRig rig, int mask)
        {
            int count = rig.muzzles != null ? Mathf.Min(rig.muzzles.Length, GunnerRig.MaxEmitters) : 0;
            int n = 0;
            for (int i = 0; i < count; i++) if (IsStream(rig, i) && (mask == 0 || (mask & (1 << i)) != 0)) n++;
            return n;
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
            if (hand.hold == null)
                into.Add(new GunnerCheck(E.Warning, "check.hand.no_hold", hand, () =>
                {
                    var hold = GunnerEditorUtil.GetOrAddUdon<GunnerMoveHold>(hand.gameObject);
                    GunnerEditorUtil.Edit(hand, so => so.FindProperty("hold").objectReferenceValue = hold);
                }));

            var rig = hand.rig;
            if (rig != null)
            {
                int set = hand.emitter >= 0 ? 1 << hand.emitter : hand.useEmitters;
                int streams = StreamCount(rig, set);
                if (hand.collideMode == GunnerHandController.CollideAfterRelease && streams == 0)
                    into.Add(new GunnerCheck(E.Warning, "check.hand.window_single", hand,
                        () => GunnerEditorUtil.Edit(hand, so => so.FindProperty("collideMode").intValue = GunnerHandController.CollideAlways)));
                if (set == 0 && streams >= 2)
                    into.Add(new GunnerCheck(E.Warning, "check.hand.all_emitters", hand, null, streams.ToString()));
                var ball = hand.input != null ? hand.input.GetComponent<Collider>() : null;
                if (ball != null && GunnerEditorUtil.HitsLayer(rig, ball.gameObject.layer))
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
