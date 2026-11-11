using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// 効果音の音源を作る・整える処理。Udon は実行時に VRC_SpatialAudioSource を足せないので、音源はエディタで作っておく。
    /// ほかのパッケージの音の部品も、ここで音源を作る。
    /// </summary>
    public static class GunnerAudio
    {
        public const float DefaultReach = 25f;
        public const int DefaultFireVoices = 3;
        public const int DefaultImpactVoices = 4;
        const float FullVolumeDistance = 1f;
        const float FadeFrom = 0.7f;   // 届く距離のこの割合から 0 へ寄せる。端でぷつっと消えないように
        const float Gain = 10f;        // VRC の既定値

        /// <summary>parent の子に、3D の音源を1つ作る。</summary>
        public static AudioSource CreateVoice(Transform parent, string name, float reach)
        {
            var go = GunnerEditorUtil.CreateChild(parent, GunnerEditorUtil.UniqueChildName(parent, name));
            var audio = Undo.AddComponent<AudioSource>(go);
            ConfigureVoice(audio, reach);
            return audio;
        }

        /// <summary>
        /// 音源を、届く距離 reach の 3D の音にする。減衰は AudioSource のカーブ(1 m まで全量、その先は 1/d)を VRC にも使わせる。
        /// 既にある VRC_SpatialAudioSource の Gain は変えない(作った人が決めた音量を残す)。
        /// </summary>
        public static void ConfigureVoice(AudioSource audio, float reach)
        {
            if (audio == null) return;
            reach = Mathf.Max(reach, FullVolumeDistance + 0.1f);
            Undo.RecordObject(audio, "Gunner: sound source");
            audio.playOnAwake = false;
            audio.loop = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0f;
            audio.rolloffMode = AudioRolloffMode.Custom;
            audio.minDistance = FullVolumeDistance;
            audio.maxDistance = reach;
            audio.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff(reach));
            var spatial = audio.GetComponent<VRCSpatialAudioSource>();
            if (spatial == null)
            {
                spatial = Undo.AddComponent<VRCSpatialAudioSource>(audio.gameObject);
                spatial.Gain = Gain;
            }
            else Undo.RecordObject(spatial, "Gunner: sound source");
            spatial.EnableSpatialization = true;
            spatial.UseAudioSourceVolumeCurve = true;
            spatial.Near = 0f;
            spatial.Far = reach;
            spatial.VolumetricRadius = 0f;
            EditorUtility.SetDirty(audio);
            EditorUtility.SetDirty(spatial);
            PrefabUtility.RecordPrefabInstancePropertyModifications(audio);
            PrefabUtility.RecordPrefabInstancePropertyModifications(spatial);
        }

        /// <summary>距離ごとの音量のカーブ。横軸は 0〜1 が 0〜reach。近くほど細かく打つ。</summary>
        public static AnimationCurve Rolloff(float reach)
        {
            var curve = new AnimationCurve();
            const int samples = 40;
            for (int i = 0; i <= samples; i++)
            {
                float u = (float)i / samples;
                float d = reach * u * u;
                curve.AddKey(new Keyframe(d / reach, Volume(d, reach)));
            }
            curve.AddKey(new Keyframe(FullVolumeDistance / reach, 1f));
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static float Volume(float d, float reach)
        {
            float v = d <= FullVolumeDistance ? 1f : FullVolumeDistance / d;
            return v * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeFrom * reach, reach, d)));
        }

        /// <summary>
        /// 今の音源の届く距離(最初に見つかった VRC_SpatialAudioSource の Far)。無ければ既定値。
        /// maxDistance は見ない。手で足した音源は Unity の既定の 500 m のままのことがあるため。
        /// </summary>
        public static float ReachOf(GunnerSounds sounds)
        {
            foreach (var list in new[] { sounds.fireVoices, sounds.impactVoices })
                if (list != null)
                    foreach (var v in list)
                    {
                        var spatial = v != null ? v.GetComponent<VRCSpatialAudioSource>() : null;
                        if (spatial != null && spatial.Far > 0f) return spatial.Far;
                    }
            return DefaultReach;
        }

        /// <summary>
        /// 空きの欄を詰め、既定の数(発射3・着弾4)に満たなければ足し、全部の音源を reach で整える。
        /// 既定より多い音源は残す(減らすのは利用者の判断)。
        /// </summary>
        public static void SetUpVoices(GunnerSounds sounds, float reach)
        {
            if (sounds == null) return;
            GunnerEditorUtil.Edit(sounds, so =>
            {
                GunnerEditorUtil.RemoveNulls(so, "fireVoices");
                GunnerEditorUtil.RemoveNulls(so, "impactVoices");
                Fill(so.FindProperty("fireVoices"), sounds.transform, "FireVoice", DefaultFireVoices, reach);
                Fill(so.FindProperty("impactVoices"), sounds.transform, "ImpactVoice", DefaultImpactVoices, reach);
            });
        }

        static void Fill(SerializedProperty list, Transform parent, string name, int count, float reach)
        {
            for (int i = 0; i < list.arraySize; i++)
                ConfigureVoice(list.GetArrayElementAtIndex(i).objectReferenceValue as AudioSource, reach);
            while (list.arraySize < count) GunnerEditorUtil.Append(list, CreateVoice(parent, name, reach));
        }

        /// <summary>受け手が着弾を聞く rig(自分で登録する先の欄と、受け手の一覧に入れている rig)。</summary>
        public static List<GunnerRig> RigsOf(GunnerHitReceiver receiver)
        {
            var found = new List<GunnerRig>();
            if (receiver is GunnerSounds s && s.rigs != null)
                foreach (var r in s.rigs) if (r != null && !found.Contains(r)) found.Add(r);
            if (receiver is GunnerHitSplash splash && splash.targetRig != null) found.Add(splash.targetRig);
            foreach (var r in GunnerEditorUtil.FindInScenes<GunnerRig>())
                if (r.receivers != null && System.Array.IndexOf(r.receivers, receiver) >= 0 && !found.Contains(r)) found.Add(r);
            return found;
        }

        /// <summary>この rig の音を受け持つ GunnerSounds(無ければ null)。</summary>
        public static GunnerSounds SoundsFor(GunnerRig rig)
        {
            foreach (var s in GunnerEditorUtil.FindInScenes<GunnerSounds>())
                if (RigsOf(s).Contains(rig)) return s;
            return null;
        }
    }

    /// <summary>GunnerSounds のインスペクタ。欄の下に、未設定のクリップの知らせと、音源を整えるボタンを出す。</summary>
    [CustomEditor(typeof(GunnerSounds)), CanEditMultipleObjects]
    public class GunnerSoundsEditor : GunnerInspectorBase
    {
        float reach = -1f;

        protected override void DrawBody()
        {
            base.DrawBody();
            var sounds = (GunnerSounds)target;
            // 無音のまま使う構成もあるので、点検の黄にはせず知らせるだけにする。
            if (sounds.fireClip == null || sounds.impactClip == null)
                EditorGUILayout.HelpBox(T("GunnerSounds.info.no_clip"), MessageType.Info);
            if (targets.Length != 1) return;
            if (reach < 0f) reach = GunnerAudio.ReachOf(sounds);
            using (new EditorGUILayout.HorizontalScope())
            {
                reach = Mathf.Max(1.1f, EditorGUILayout.FloatField(Label("GunnerSounds.reach"), reach));
                if (GUILayout.Button(T("button.setup_voices"), GUILayout.Width(120)))
                {
                    GunnerAudio.SetUpVoices(sounds, reach);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
