using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using E = Mecaota.Gunner.EditorTools.GunnerCheckLevel;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>Checks for the gunner components themselves.</summary>
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
                case GunnerStream stream: Stream(stream, into); break;
            }
        }

        // ---------------- Rig ----------------

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
                    () => GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveNulls(so, "emitters", "shotParticles", "mounts", "linkBones"))));

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

            // The projectile hears _OnOwnerHit as the projectile, but _OnHit only as a listed receiver.
            if (rig.projectile != null && System.Array.IndexOf(listed, rig.projectile) < 0)
                into.Add(new GunnerCheck(E.Warning, "check.rig.projectile_unlisted", rig, () => AddReceiver(rig, rig.projectile)));

            foreach (var r in rig.GetComponentsInChildren<GunnerHitReceiver>(true))
            {
                if (System.Array.IndexOf(listed, r) >= 0 || r == rig.projectile) continue;
                if (r is GunnerHitSplash s && s.targetRig == rig) continue; // registers itself on Start
                var receiver = r;
                into.Add(new GunnerCheck(E.Warning, "check.rig.receiver_unlisted", r, () => AddReceiver(rig, receiver), r.name));
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

        // ---------------- Hand ----------------

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

        // ---------------- Receivers and stream ----------------

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

        static void Stream(GunnerStream stream, List<GunnerCheck> into)
        {
            foreach (var rig in GunnerEditorUtil.FindInScenes<GunnerRig>())
                if (rig.stream == stream) return;
            into.Add(new GunnerCheck(E.Warning, "check.stream.unused", stream));
        }
    }
}
