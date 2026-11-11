using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>One-shot setup commands (hierarchy right-click: GameObject/Gunner/...). Menu names stay English.</summary>
    public static class GunnerSetup
    {
        const string Root = "GameObject/Gunner/";
        const int Priority = 10;
        const int PickupLayer = 13;

        // ---------------- Menus ----------------

        [MenuItem(Root + "Add Gunner Rig", false, Priority)]
        static void MenuAddRig(MenuCommand command)
        {
            var go = Target(command);
            var rig = go != null ? go.GetComponentInParent<GunnerRig>(true) : null;
            if (rig == null)
            {
                rig = AttachRig(go);
                AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                // Ready for stream mode too: switching the fire mode in the inspector is then enough.
                EnsureStream(rig);
            }
            Selection.activeObject = rig.gameObject;
        }

        [MenuItem(Root + "Add Emitter", false, Priority + 1)]
        static void MenuAddEmitter(MenuCommand command)
        {
            var go = Target(command);
            var rig = RigFor(go);
            if (rig == null) return;
            var parent = go != null ? go.transform : rig.transform;
            Selection.activeObject = AddEmitter(rig, parent, parent.position, parent.rotation).gameObject;
        }

        [MenuItem(Root + "Add Emitter", true)]
        static bool CanAddEmitter() => RigFor(Selection.activeGameObject) != null;

        [MenuItem(Root + "Add Emitter At Body Part/Right Index Fingertip", false, Priority + 2)]
        static void MenuRightIndex(MenuCommand c) => AddAtBodyPart(Target(c), BodyPart.RightIndexTip);

        [MenuItem(Root + "Add Emitter At Body Part/Left Index Fingertip", false, Priority + 2)]
        static void MenuLeftIndex(MenuCommand c) => AddAtBodyPart(Target(c), BodyPart.LeftIndexTip);

        [MenuItem(Root + "Add Emitter At Body Part/Both Index Fingertips", false, Priority + 2)]
        static void MenuBothIndex(MenuCommand c)
        {
            var go = Target(c);
            AddAtBodyPart(go, BodyPart.RightIndexTip);
            AddAtBodyPart(go, BodyPart.LeftIndexTip);
        }

        [MenuItem(Root + "Add Emitter At Body Part/Right Palm", false, Priority + 2)]
        static void MenuRightPalm(MenuCommand c) => AddAtBodyPart(Target(c), BodyPart.RightPalm);

        [MenuItem(Root + "Add Emitter At Body Part/Left Palm", false, Priority + 2)]
        static void MenuLeftPalm(MenuCommand c) => AddAtBodyPart(Target(c), BodyPart.LeftPalm);

        [MenuItem(Root + "Add Emitter At Body Part/Mouth", false, Priority + 2)]
        static void MenuMouth(MenuCommand c) => AddAtBodyPart(Target(c), BodyPart.Mouth);

        [MenuItem(Root + "Add Emitter At Body Part/Right Index Fingertip", true)]
        [MenuItem(Root + "Add Emitter At Body Part/Left Index Fingertip", true)]
        [MenuItem(Root + "Add Emitter At Body Part/Both Index Fingertips", true)]
        [MenuItem(Root + "Add Emitter At Body Part/Right Palm", true)]
        [MenuItem(Root + "Add Emitter At Body Part/Left Palm", true)]
        [MenuItem(Root + "Add Emitter At Body Part/Mouth", true)]
        static bool CanAddAtBodyPart() => HumanoidFor(Selection.activeGameObject) != null;

        [MenuItem(Root + "Place Hand Controller", false, Priority + 20)]
        static void MenuPlaceHand(MenuCommand command)
        {
            var rig = RigFor(Target(command));
            if (rig == null) return;
            Selection.activeObject = PlaceHand(rig).gameObject;
        }

        [MenuItem(Root + "Place Hand Controller", true)]
        static bool CanPlaceHand() => RigFor(Selection.activeGameObject) != null;

        [MenuItem(Root + "Add Hit Splash", false, Priority + 21)]
        static void MenuAddSplash(MenuCommand command)
        {
            var rig = RigFor(Target(command));
            if (rig == null) return;
            Selection.activeObject = AddSplash(rig).gameObject;
        }

        [MenuItem(Root + "Add Hit Splash", true)]
        static bool CanAddSplash() => RigFor(Selection.activeGameObject) != null;

        [MenuItem("Tools/Gunner/Check Window", false, 0)]
        static void MenuCheckWindow() => GunnerCheckWindow.Open();

        static GameObject Target(MenuCommand command)
        {
            var go = command != null ? command.context as GameObject : null;
            return go != null ? go : Selection.activeGameObject;
        }

        public static GunnerRig RigFor(GameObject go) => go != null ? go.GetComponentInParent<GunnerRig>(true) : null;

        static Animator HumanoidFor(GameObject go)
        {
            if (go == null) return null;
            var a = go.GetComponentInParent<Animator>(true);
            if (a == null || !a.isHuman) a = go.GetComponentInChildren<Animator>(true);
            return a != null && a.isHuman && a.avatar != null ? a : null;
        }

        // ---------------- Operations ----------------

        /// <summary>
        /// Adds a GunnerRig to the object (or a new one when nothing is given). The rig is Manual-synced, which cannot share a
        /// GameObject with VRC Object Sync, so such an object gets a child for the rig instead.
        /// </summary>
        public static GunnerRig AttachRig(GameObject go)
        {
            if (go == null)
            {
                go = new GameObject("GunnerRig");
                Undo.RegisterCreatedObjectUndo(go, "Gunner: rig");
                var view = SceneView.lastActiveSceneView;
                if (view != null) go.transform.position = view.pivot;
            }
            else if (go.GetComponent<VRCObjectSync>() != null)
            {
                go = GunnerEditorUtil.CreateChild(go.transform, GunnerEditorUtil.UniqueChildName(go.transform, "GunnerRig"));
            }
            var rig = GunnerEditorUtil.GetOrAddUdon<GunnerRig>(go);
            GunnerEditorUtil.CommitProxy(rig);
            return rig;
        }

        /// <summary>A new emitter under parent (+Z = rest direction) with its own shot particles, appended to the rig.</summary>
        public static Transform AddEmitter(GunnerRig rig, Transform parent, Vector3 position, Quaternion rotation, string name = "Emitter")
        {
            var go = GunnerEditorUtil.CreateChild(parent, GunnerEditorUtil.UniqueChildName(parent, name));
            go.transform.SetPositionAndRotation(position, rotation);
            var ps = GunnerEditorUtil.CreateShotParticles(go.transform);
            GunnerEditorUtil.Edit(rig, so =>
            {
                var emitters = so.FindProperty("emitters");
                int index = emitters.arraySize;
                GunnerEditorUtil.Append(emitters, go.transform);
                GunnerEditorUtil.SetAt(so.FindProperty("shotParticles"), index, ps);
            });
            return go.transform;
        }

        public static GunnerStream EnsureStream(GunnerRig rig)
        {
            if (rig.stream != null) return rig.stream;
            var parts = Parts(rig);
            var stream = GunnerEditorUtil.GetOrAddUdon<GunnerStream>(parts.gameObject);
            GunnerEditorUtil.CommitProxy(stream);
            GunnerEditorUtil.Edit(rig, so => so.FindProperty("stream").objectReferenceValue = stream);
            return stream;
        }

        /// <summary>Generated helpers live under the rig's "Parts" child.</summary>
        public static Transform Parts(GunnerRig rig)
        {
            var t = rig.transform.Find("Parts");
            return t != null ? t : GunnerEditorUtil.CreateChild(rig.transform, "Parts").transform;
        }

        /// <summary>The hand-held ball: a small sphere with VRC Pickup (AutoHold, so the desktop trigger arrives) and the four parts.</summary>
        public static GunnerHandController PlaceHand(GunnerRig rig)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(go, "Gunner: hand");
            go.name = "GunnerHandController";
            go.layer = PickupLayer;
            go.transform.localScale = Vector3.one * 0.1f;
            go.transform.position = rig.transform.position + rig.transform.forward * 0.5f + Vector3.up * 1f;

            var body = Undo.AddComponent<Rigidbody>(go);
            body.isKinematic = true;
            body.useGravity = false;
            var pickup = Undo.AddComponent<VRCPickup>(go);
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            Undo.AddComponent<VRCObjectSync>(go);

            var input = GunnerEditorUtil.AddUdon<GunnerPickupInput>(go);
            var aim = GunnerEditorUtil.AddUdon<GunnerStickAim>(go);
            var hold = GunnerEditorUtil.AddUdon<GunnerMoveHold>(go);
            var hand = GunnerEditorUtil.AddUdon<GunnerHandController>(go);
            GunnerEditorUtil.CommitProxy(aim);
            GunnerEditorUtil.CommitProxy(hold);
            GunnerEditorUtil.Edit(input, so =>
            {
                so.FindProperty("controller").objectReferenceValue = hand;
                so.FindProperty("pickup").objectReferenceValue = pickup;
            });
            GunnerEditorUtil.Edit(hand, so =>
            {
                so.FindProperty("rig").objectReferenceValue = rig;
                so.FindProperty("input").objectReferenceValue = input;
                so.FindProperty("aim").objectReferenceValue = aim;
                so.FindProperty("hold").objectReferenceValue = hold;
            });
            // The ball is held next to the shooter: a shot must not stop on it.
            if (GunnerEditorUtil.InMask(rig.hitMask, PickupLayer)) ExcludeLayer(rig, PickupLayer);
            return hand;
        }

        public static GunnerHitSplash AddSplash(GunnerRig rig)
        {
            var parts = Parts(rig);
            var go = GunnerEditorUtil.CreateChild(parts, GunnerEditorUtil.UniqueChildName(parts, "HitSplash"));
            var psGo = GunnerEditorUtil.CreateChild(go.transform, "Splash");
            var ps = Undo.AddComponent<ParticleSystem>(psGo);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.loop = false;
            // Debug-friendly defaults: big, bright, long-lived, so the burst shows even in a far screenshot.
            main.startLifetime = 1.5f;
            main.startSpeed = 3f;
            main.startSize = 0.2f;
            main.gravityModifier = 0.5f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.01f;
            var renderer = psGo.GetComponent<ParticleSystemRenderer>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.mecaota.gunner/Material/GunnerSampleSplashDebug.mat");
            if (mat == null) mat = GunnerEditorUtil.DefaultParticleMaterial();
            if (renderer != null && mat != null) renderer.sharedMaterial = mat;

            var splash = GunnerEditorUtil.AddUdon<GunnerHitSplash>(go);
            GunnerEditorUtil.Edit(splash, so =>
            {
                so.FindProperty("targetRig").objectReferenceValue = rig;
                so.FindProperty("splash").objectReferenceValue = ps;
                so.FindProperty("burst").intValue = 30;
            });
            return splash;
        }

        public static void ExcludeLayer(GunnerRig rig, int layer)
        {
            GunnerEditorUtil.Edit(rig, so =>
            {
                var mask = so.FindProperty("hitMask");
                mask.intValue = mask.intValue & ~(1 << layer);
            });
        }

        // ---------------- Body parts (editor-time defaults only; runtime code never names bones) ----------------

        enum BodyPart { RightIndexTip, LeftIndexTip, RightPalm, LeftPalm, Mouth }

        static void AddAtBodyPart(GameObject go, BodyPart part)
        {
            var animator = HumanoidFor(go);
            if (animator == null) return;
            if (!Locate(animator, part, out Transform bone, out Vector3 pos, out Vector3 forward))
            {
                Debug.LogWarning("[Gunner] Body part not found on this humanoid: " + part);
                return;
            }
            var rig = RigFor(bone.gameObject);
            if (rig == null) rig = RigFor(animator.gameObject);
            if (rig == null) rig = AttachRig(animator.gameObject);
            var up = animator.transform.up;
            if (Mathf.Abs(Vector3.Dot(up, forward)) > 0.99f) up = animator.transform.forward;
            var t = AddEmitter(rig, bone, pos, Quaternion.LookRotation(forward, up), "Emitter_" + part);
            Selection.activeObject = t.gameObject;
        }

        static bool Locate(Animator a, BodyPart part, out Transform bone, out Vector3 pos, out Vector3 forward)
        {
            bone = null; pos = Vector3.zero; forward = a.transform.forward;
            float scale = Mathf.Max(a.humanScale, 0.01f);
            switch (part)
            {
                case BodyPart.RightIndexTip:
                case BodyPart.LeftIndexTip:
                {
                    bool right = part == BodyPart.RightIndexTip;
                    var hand = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                    var prox = a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                    var mid = a.GetBoneTransform(right ? HumanBodyBones.RightIndexIntermediate : HumanBodyBones.LeftIndexIntermediate);
                    var dist = a.GetBoneTransform(right ? HumanBodyBones.RightIndexDistal : HumanBodyBones.LeftIndexDistal);
                    // Walk back to the deepest pair of bones that exists; the tip lies one segment past the last joint.
                    Transform last = dist != null ? dist : mid != null ? mid : prox;
                    Transform before = last == dist ? (mid != null ? mid : prox) : last == mid ? prox : hand;
                    if (last == null || before == null)
                    {
                        if (hand == null) return false;
                        var lower = a.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                        bone = hand;
                        forward = lower != null ? (hand.position - lower.position).normalized : a.transform.forward;
                        pos = hand.position + forward * 0.08f * scale;
                        return true;
                    }
                    Vector3 seg = last.position - before.position;
                    if (seg.sqrMagnitude < 1e-8f) return false;
                    bone = last;
                    forward = seg.normalized;
                    pos = last.position + seg * 0.8f; // a fingertip segment is a bit shorter than the one before it
                    return true;
                }
                case BodyPart.RightPalm:
                case BodyPart.LeftPalm:
                {
                    bool right = part == BodyPart.RightPalm;
                    var hand = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                    var middle = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                    if (hand == null) return false;
                    bone = hand;
                    Vector3 along = middle != null ? (middle.position - hand.position) : hand.forward * 0.08f * scale;
                    pos = hand.position + along * 0.5f;
                    // The palm faces down in a T pose: shoot down from it; adjust in the scene if the avatar differs.
                    forward = -a.transform.up;
                    return true;
                }
                case BodyPart.Mouth:
                {
                    var jaw = a.GetBoneTransform(HumanBodyBones.Jaw);
                    var head = a.GetBoneTransform(HumanBodyBones.Head);
                    if (head == null) return false;
                    bone = head;
                    forward = a.transform.forward;
                    Vector3 basePos = jaw != null ? jaw.position : head.position;
                    pos = basePos + forward * 0.08f * scale;
                    return true;
                }
            }
            return false;
        }
    }
}
