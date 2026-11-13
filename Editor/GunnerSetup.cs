using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>1回きりのセットアップのコマンド(ヒエラルキーの右クリック: GameObject/Gunner/...)。メニュー名は英語のまま。</summary>
    public static class GunnerSetup
    {
        const string Root = "GameObject/Gunner/";
        const int Priority = 10;
        const int PickupLayer = 13;

        // ---------------- メニュー ----------------

        [MenuItem(Root + "Add Gunner Rig", false, Priority)]
        static void MenuAddRig(MenuCommand command)
        {
            var go = Target(command);
            var rig = go != null ? go.GetComponentInParent<GunnerRig>(true) : null;
            if (rig == null)
            {
                rig = AttachRig(go);
                AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                // 連射にも備えておく: インスペクタで撃ち方を切り替えるだけで済む。
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

        [MenuItem(Root + "Add Gunner Sounds", false, Priority + 22)]
        static void MenuAddSounds(MenuCommand command)
        {
            var rig = RigFor(Target(command));
            if (rig == null) return;
            Selection.activeObject = AddSounds(rig).gameObject;
        }

        [MenuItem(Root + "Add Gunner Sounds", true)]
        static bool CanAddSounds() => RigFor(Selection.activeGameObject) != null;

        [MenuItem("Tools/Gunner/Add Gunner Sounds", false, 1)]
        static void MenuToolsAddSounds() => MenuAddSounds(null);

        [MenuItem("Tools/Gunner/Add Gunner Sounds", true)]
        static bool CanToolsAddSounds() => CanAddSounds();

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

        // ---------------- 操作 ----------------

        /// <summary>
        /// オブジェクトに GunnerRig を足す(何も渡さなければ新しく作る)。rig は Manual 同期で、VRC Object Sync と同じ
        /// GameObject には置けないので、そういうオブジェクトには rig 用の子を作る。
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

        /// <summary>parent の下に新しい射出口(+Z = 休み方向)を、専用の弾の粒子と一緒に作り、rig に追加する。</summary>
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

        /// <summary>作った補助の部品は rig の "Parts" の子に置く。</summary>
        public static Transform Parts(GunnerRig rig)
        {
            var t = rig.transform.Find("Parts");
            return t != null ? t : GunnerEditorUtil.CreateChild(rig.transform, "Parts").transform;
        }

        /// <summary>手に持つ玉: VRC Pickup(AutoHold。デスクトップのトリガーが届くように)付きの小さな球と、4つの部品。</summary>
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
            // 玉は撃つ人のすぐそばに持たれる: 弾が玉で止まってはいけない。
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
            // 確認しやすい既定値: 大きく明るく長持ちさせ、遠くからのスクリーンショットでも弾けた様子が写るようにする。
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

        /// <summary>
        /// 発射音と着弾音の受け手を rig の Parts の下に作り、音源の子(発射3・着弾4)を配線する。クリップは空のまま。
        /// その rig を既に受け持つ GunnerSounds があれば、それを返す(二重に鳴らさないため)。
        /// </summary>
        public static GunnerSounds AddSounds(GunnerRig rig)
        {
            var existing = GunnerAudio.SoundsFor(rig);
            if (existing != null) return existing;
            var parts = Parts(rig);
            var go = GunnerEditorUtil.CreateChild(parts, GunnerEditorUtil.UniqueChildName(parts, "Sounds"));
            var sounds = GunnerEditorUtil.AddUdon<GunnerSounds>(go);
            GunnerEditorUtil.Edit(sounds, so =>
            {
                var rigs = so.FindProperty("rigs");
                rigs.arraySize = 0;
                GunnerEditorUtil.Append(rigs, rig);
            });
            GunnerAudio.SetUpVoices(sounds, GunnerAudio.DefaultReach);
            return sounds;
        }

        public static void ExcludeLayer(GunnerRig rig, int layer)
        {
            GunnerEditorUtil.Edit(rig, so =>
            {
                var mask = so.FindProperty("hitMask");
                mask.intValue = mask.intValue & ~(1 << layer);
            });
        }

        // ---------------- 体の部位(エディタでの既定値だけ。実行時のコードは骨の名前を使わない) ----------------

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
                    // ある骨の組のうち最も深いものまで戻る。先端は最後の関節から1区間先にある。
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
                    pos = last.position + seg * 0.8f; // 指先の区間は1つ手前より少し短い
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
                    // T ポーズでは手のひらが下を向く: そこから下へ撃つ。アバターが違えばシーンで調整する。
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
