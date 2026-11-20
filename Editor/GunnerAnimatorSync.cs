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

        public static void Sync(GunnerRig rig)
        {
            var controller = ControllerOf(rig);
            if (controller == null)
            {
                Debug.LogError(GunnerL10n.Tr("GunnerRig.syncAnimator.noController"), rig);
                return;
            }
            var layers = controller.layers;
            for (int i = layers.Length - 1; i >= 0; i--)
                if (layers[i].name.StartsWith(LayerPrefix)) controller.RemoveLayer(i);
            foreach (var p in controller.parameters)
                if (p.name.StartsWith(ParamPrefix)) controller.RemoveParameter(p);

            var muzzles = rig.muzzles ?? new GunnerEmitter[0];
            var animated = Enumerable.Range(0, muzzles.Length).Where(i => muzzles[i] != null && muzzles[i].preFireClip != null).ToList();
            foreach (int i in animated) controller.AddParameter(ParamPrefix + i, AnimatorControllerParameterType.Bool);
            // 同じ部位を動かす射出口でレイヤーを分けると、上のレイヤーの Rest が下の動きを打ち消すので、撃つ前のクリップごとに1枚にする。
            foreach (var group in animated.GroupBy(i => muzzles[i].preFireClip))
                AddLayer(controller, group.Key, muzzles[group.First()].postFireClip, group.ToArray());

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        static void AddLayer(AnimatorController controller, AnimationClip pre, AnimationClip post, int[] emitters)
        {
            string name = controller.MakeUniqueLayerName(LayerPrefix + pre.name);
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = machine, defaultWeight = 1f });

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
