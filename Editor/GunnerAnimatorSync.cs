using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// 名前が Gunner_ か GunnerPre で始まる物は毎回消して作り直すので、手で直しても次に押すと消える。
    /// </summary>
    public static class GunnerAnimatorSync
    {
        public const string LayerPrefix = "Gunner_";
        public const string ParamPrefix = "GunnerPre";

        /// <summary>fireAnimator の Controller。空か Override Controller なら null。</summary>
        public static AnimatorController ControllerOf(GunnerRig rig) =>
            rig.fireAnimator != null ? rig.fireAnimator.runtimeAnimatorController as AnimatorController : null;

        // 同じ部位を動かす射出口でレイヤーを分けると、上のレイヤーの Rest が下の動きを打ち消すので、撃つ前のクリップごとに1枚にする。
        public static List<int[]> Groups(GunnerEmitter[] muzzles) =>
            Enumerable.Range(0, muzzles.Length).Where(i => muzzles[i] != null && muzzles[i].preFireClip != null)
                .GroupBy(i => muzzles[i].preFireClip).Select(g => g.ToArray()).ToList();

        public static void Sync(GunnerRig rig)
        {
            var controller = ControllerOf(rig);
            if (controller == null)
            {
                Debug.LogError(GunnerL10n.Tr("GunnerRig.syncAnimator.noController"), rig);
                return;
            }
            string label = GunnerL10n.Tr("GunnerRig.syncAnimator");
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(label);
            int undoGroup = Undo.GetCurrentGroup();

            var layers = controller.layers;
            int at = System.Array.FindIndex(layers, l => l.name.StartsWith(LayerPrefix));
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (!layers[i].name.StartsWith(LayerPrefix)) continue;
                var mask = layers[i].avatarMask;
                controller.RemoveLayer(i);
                if (mask != null && AssetDatabase.GetAssetPath(mask) == AssetDatabase.GetAssetPath(controller))
                    Undo.DestroyObjectImmediate(mask);
            }
            foreach (var p in controller.parameters)
                if (p.name.StartsWith(ParamPrefix)) controller.RemoveParameter(p);

            var muzzles = rig.muzzles ?? new GunnerEmitter[0];
            var groups = Groups(muzzles);
            foreach (int i in groups.SelectMany(g => g)) controller.AddParameter(ParamPrefix + i, AnimatorControllerParameterType.Bool);
            foreach (var group in groups)
                AddLayer(controller, muzzles[group[0]].preFireClip, muzzles[group[0]].postFireClip, group, label);

            // 押し直しても、手で足したレイヤーとの上下を変えない。
            if (at >= 0)
            {
                var all = controller.layers.ToList();
                var added = all.GetRange(all.Count - groups.Count, groups.Count);
                all.RemoveRange(all.Count - groups.Count, groups.Count);
                all.InsertRange(at, added);
                Undo.RecordObject(controller, label);
                controller.layers = all.ToArray();
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        static void AddLayer(AnimatorController controller, AnimationClip pre, AnimationClip post, int[] emitters, string label)
        {
            string name = controller.MakeUniqueLayerName(LayerPrefix + pre.name);
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            Undo.RegisterCreatedObjectUndo(machine, label);
            // Rest は Write Defaults で既定の姿勢を書くので、マスクが無いと、ほかのレイヤーが動かすボーンまで止める。
            var mask = MaskOf(name, pre, post);
            AssetDatabase.AddObjectToAsset(mask, controller);
            Undo.RegisterCreatedObjectUndo(mask, label);
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = machine, avatarMask = mask, defaultWeight = 1f });

            var rest = machine.AddState("Rest");
            var preState = machine.AddState(pre.name);
            preState.motion = pre;
            var after = rest;
            if (post != null)
            {
                after = machine.AddState(post.name);
                after.motion = post;
                Timing(after.AddTransition(rest), true, 0.1f);
            }
            foreach (int i in emitters)
                Timing(rest.AddTransition(preState), false, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, ParamPrefix + i);
            var release = Timing(preState.AddTransition(after), false, 0.05f);
            foreach (int i in emitters) release.AddCondition(AnimatorConditionMode.IfNot, 0f, ParamPrefix + i);
        }

        // パスが0件のマスクは何も絞らないので、カーブの無いクリップには、無いパスを1つ入れて何も通さないようにする。
        static AvatarMask MaskOf(string name, params AnimationClip[] clips)
        {
            var paths = clips.Where(c => c != null)
                .SelectMany(c => AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c)))
                .Select(b => b.path).Distinct().DefaultIfEmpty(LayerPrefix + "none").ToArray();
            var mask = new AvatarMask { name = name, hideFlags = HideFlags.HideInHierarchy, transformCount = paths.Length };
            for (int i = 0; i < paths.Length; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, true);
            }
            return mask;
        }

        static AnimatorStateTransition Timing(AnimatorStateTransition t, bool atEnd, float duration)
        {
            t.hasExitTime = atEnd;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = duration;
            return t;
        }
    }
}
