using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>GunnerRig のインスペクタ(項目のまとめ、セットアップのボタン)とシーンのハンドル: 射出口の姿勢、射程、狙いの扇。</summary>
    [CustomEditor(typeof(GunnerRig)), CanEditMultipleObjects]
    public class GunnerRigEditor : GunnerInspectorBase
    {
        const string HandlesPref = "Mecaota.Gunner.ShowHandles";
        static readonly Color FanFill = new Color(1f, 0.6f, 0.1f, 0.12f);
        static readonly Color FanLine = new Color(1f, 0.6f, 0.1f, 0.8f);
        static readonly Color RangeLine = new Color(0.3f, 0.8f, 1f, 0.9f);

        protected override void DrawBody()
        {
            var rig = (GunnerRig)target;

            if (Foldout("GunnerRig.section.emitters"))
            {
                Field("emitters");
                Field("shotParticles");
                Field("mounts");
                Field("linkBones");
                DrawExtraBones(rig);
                Field("movers");
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(targets.Length != 1))
                    {
                        if (GUILayout.Button(T("button.add_emitter")))
                        {
                            GunnerSetup.AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                            GUIUtility.ExitGUI();
                        }
                        if (GUILayout.Button(T("button.place_hand")))
                        {
                            Selection.activeObject = GunnerSetup.PlaceHand(rig).gameObject;
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                bool show = EditorPrefs.GetBool(HandlesPref, true);
                bool next = EditorGUILayout.Toggle(Label("GunnerRig.showHandles"), show);
                if (next != show)
                {
                    EditorPrefs.SetBool(HandlesPref, next);
                    SceneView.RepaintAll();
                }
            }

            if (Foldout("GunnerRig.section.receivers"))
            {
                Field("projectile");
                Field("receivers");
                Field("stream");
                using (new EditorGUI.DisabledScope(targets.Length != 1))
                {
                    if (GUILayout.Button(T("button.add_splash")))
                    {
                        GunnerSetup.AddSplash(rig);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(T("button.add_sounds")))
                    {
                        Selection.activeObject = GunnerSetup.AddSounds(rig).gameObject;
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (Foldout("GunnerRig.section.shot"))
            {
                Field("fireMode");
                Field("shotSpeed");
                Field("maxRange");
                Field("castRadius");
                Field("hitMask");
                Field("hitPlayers");
                Field("excludeOwner");
                Field("playerRadius");
                Field("aimMaxAngle");
                DrawAimAngles(rig);
                Field("maxHits");
            }

            if (Foldout("GunnerRig.section.stream"))
            {
                Field("streamRate");
                Field("streamWidth");
                Field("streamTaper");
                Field("streamJoin");
                Field("streamHitMode");
                Field("streamHitInterval");
                Field("streamCollide");
            }

            if (Foldout("GunnerRig.section.bone"))
            {
                Field("boneFollow");
                Field("boneStrength");
                Field("boneYawMax");
                Field("bonePitchUpMax");
                Field("bonePitchDownMax");
                Field("chainYawLimit");
                Field("chainPitchUpLimit");
                Field("chainPitchDownLimit");
                Field("boneSmooth");
                Field("boneMinSpeed");
                Field("boneMaxSpeed");
            }
        }

        // ---------------- 射出口ごとの狙える角度 ----------------

        // 射出口ごとに角度の欄を1つ出す(0 = 可動域 aimMaxAngle を使う)。中身は aimMaxAngles(同じ番号が射出口)。
        void DrawAimAngles(GunnerRig rig)
        {
            if (targets.Length != 1)
            {
                Field("aimMaxAngles");
                return;
            }
            var emitters = rig.emitters ?? new Transform[0];
            var angles = rig.aimMaxAngles ?? new float[0];
            if (emitters.Length < 2 && angles.Length == 0)
            {
                // 射出口が1つなら可動域だけで足りる。欄は畳んだまま出す。
                Field("aimMaxAngles");
                return;
            }
            EditorGUILayout.LabelField(Label("GunnerRig.aimMaxAngles"));
            EditorGUI.indentLevel++;
            for (int e = 0; e < emitters.Length && e < GunnerRig.MaxEmitters; e++)
            {
                string name = emitters[e] != null ? emitters[e].name : "-";
                float now = e < angles.Length ? angles[e] : 0f;
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.FloatField(new GUIContent(e + " (" + name + ")", T("GunnerRig.aimMaxAngles.row")), now);
                if (EditorGUI.EndChangeCheck())
                {
                    int index = e;
                    int size = emitters.Length;
                    float value = Mathf.Clamp(next, 0f, 180f);
                    GunnerEditorUtil.Edit(rig, so =>
                    {
                        var arr = so.FindProperty("aimMaxAngles");
                        if (arr.arraySize < size) arr.arraySize = size;
                        arr.GetArrayElementAtIndex(index).floatValue = value;
                    });
                }
            }
            EditorGUI.indentLevel--;
        }

        // 射出口 i の狙える角度(rig の AimLimit と同じ決まり)。
        internal static float EmitterLimit(GunnerRig rig, int i)
        {
            var angles = rig.aimMaxAngles;
            if (angles != null && i < angles.Length && angles[i] > 0f) return angles[i];
            return rig.aimMaxAngle;
        }

        // ---------------- 追加の連動ボーン ----------------

        int addParentCount = 1;

        // 射出口ごとに「追加の骨」の一覧を出す。中身は linkExtraBones と linkExtraEmitter(同じ番号が組)。
        void DrawExtraBones(GunnerRig rig)
        {
            if (targets.Length != 1)
            {
                Field("linkExtraBones");
                Field("linkExtraEmitter");
                return;
            }
            var emitters = rig.emitters ?? new Transform[0];
            var bones = rig.linkExtraBones ?? new Transform[0];
            var owners = rig.linkExtraEmitter ?? new int[0];
            EditorGUILayout.LabelField(Label("GunnerRig.linkExtraBones"));
            EditorGUI.indentLevel++;
            var shown = new bool[bones.Length];
            for (int e = 0; e < emitters.Length; e++)
            {
                Transform lead = LeadBone(rig, e);
                if (lead == null) continue;
                EditorGUILayout.LabelField(T("GunnerRig.extra.emitter").Replace("{0}", e.ToString()).Replace("{1}", lead.name));
                EditorGUI.indentLevel++;
                for (int k = 0; k < bones.Length; k++)
                {
                    if (k >= owners.Length || owners[k] != e) continue;
                    shown[k] = true;
                    ExtraRow(rig, k);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    addParentCount = Mathf.Clamp(EditorGUILayout.IntField(addParentCount, GUILayout.Width(80)), 1, 16);
                    if (GUILayout.Button(T("button.add_parent_bones").Replace("{0}", addParentCount.ToString())))
                    {
                        int emitter = e;
                        int n = addParentCount;
                        GunnerEditorUtil.Edit(rig, so => AddParentBones(rig, so, emitter, n));
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUI.indentLevel--;
            }
            // 連動の無い射出口を指している骨(点検が赤を出す)。消せるように並べる。
            bool orphanLabel = false;
            for (int k = 0; k < bones.Length; k++)
            {
                if (shown[k]) continue;
                if (!orphanLabel)
                {
                    EditorGUILayout.LabelField(T("GunnerRig.extra.orphan"));
                    orphanLabel = true;
                }
                EditorGUI.indentLevel++;
                ExtraRow(rig, k);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        void ExtraRow(GunnerRig rig, int k)
        {
            var bones = serializedObject.FindProperty("linkExtraBones");
            if (bones == null || k >= bones.arraySize) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(bones.GetArrayElementAtIndex(k), GUIContent.none);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    int index = k;
                    GunnerEditorUtil.Edit(rig, so => RemoveExtraAt(so, index));
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>射出口 i の先頭の骨(実行時と同じ決め方)。付け替えた射出口・連動しない射出口は null。</summary>
        internal static Transform LeadBone(GunnerRig rig, int i)
        {
            if (rig == null || rig.emitters == null || i < 0 || i >= rig.emitters.Length) return null;
            var em = rig.emitters[i];
            if (em == null) return null;
            if (rig.mounts != null && i < rig.mounts.Length && rig.mounts[i] != null) return null;
            Transform bone = rig.linkBones != null && i < rig.linkBones.Length ? rig.linkBones[i] : null;
            Transform parent = em.parent;
            Transform self = rig.transform;
            if (bone == null && parent != null && parent != self && parent.IsChildOf(self)) bone = parent;
            if (bone == null || !em.IsChildOf(bone)) return null;
            return bone;
        }

        /// <summary>射出口の鎖のいちばん上(先頭の骨か、登録済みの追加の骨のうち最も上)から、親を n 個足す。</summary>
        internal static void AddParentBones(GunnerRig rig, SerializedObject so, int emitter, int n)
        {
            Transform lead = LeadBone(rig, emitter);
            if (lead == null) return;
            var bones = so.FindProperty("linkExtraBones");
            var owners = so.FindProperty("linkExtraEmitter");
            // 同じ先頭の骨を持つ射出口は1つの鎖になるので、そのどれかに登録済みの骨をすべて数える(重複を作らない)。
            var mine = new HashSet<Transform>();
            for (int k = 0; k < bones.arraySize && k < owners.arraySize; k++)
                if (LeadBone(rig, owners.GetArrayElementAtIndex(k).intValue) == lead)
                    mine.Add(bones.GetArrayElementAtIndex(k).objectReferenceValue as Transform);
            Transform top = lead;
            for (var p = lead.parent; p != null; p = p.parent) if (mine.Contains(p)) top = p;
            for (int c = 0; c < n; c++)
            {
                Transform next = top.parent;
                if (next == null) break;
                int i = bones.arraySize;
                bones.arraySize = i + 1;
                bones.GetArrayElementAtIndex(i).objectReferenceValue = next;
                owners.arraySize = i + 1;
                owners.GetArrayElementAtIndex(i).intValue = emitter;
                top = next;
            }
        }

        /// <summary>追加の骨を1つ除く(組の射出口番号も一緒に)。</summary>
        internal static void RemoveExtraAt(SerializedObject so, int k)
        {
            var bones = so.FindProperty("linkExtraBones");
            var owners = so.FindProperty("linkExtraEmitter");
            if (bones != null && k < bones.arraySize)
            {
                bones.GetArrayElementAtIndex(k).objectReferenceValue = null;
                bones.DeleteArrayElementAtIndex(k);
            }
            if (owners != null && k < owners.arraySize) owners.DeleteArrayElementAtIndex(k);
        }

        // ---------------- シーンビュー ----------------

        void OnSceneGUI()
        {
            var rig = (GunnerRig)target;
            if (rig == null || rig.emitters == null) return;
            bool edit = EditorPrefs.GetBool(HandlesPref, true) && !Application.isPlaying;
            for (int i = 0; i < rig.emitters.Length; i++)
            {
                var em = rig.emitters[i];
                if (em == null) continue;
                DrawEmitter(rig, em, i, edit);
            }
        }

        void DrawEmitter(GunnerRig rig, Transform em, int index, bool edit)
        {
            Vector3 pos = em.position;
            Vector3 fwd = em.forward;
            Vector3 up = em.up;
            Vector3 right = em.right;
            float size = HandleUtility.GetHandleSize(pos);
            float range = Mathf.Max(0f, rig.maxRange);
            float angle = Mathf.Clamp(EmitterLimit(rig, index), 0f, 180f);
            float fan = Mathf.Min(range, size * 1.5f);

            // 狙いは休み方向からここまで届く: 左右に扇を1つ、上下に1つ。
            Handles.color = FanFill;
            Handles.DrawSolidArc(pos, up, Quaternion.AngleAxis(-angle, up) * fwd, angle * 2f, fan);
            Handles.DrawSolidArc(pos, right, Quaternion.AngleAxis(-angle, right) * fwd, angle * 2f, fan);
            Handles.color = FanLine;
            Handles.DrawWireArc(pos, up, Quaternion.AngleAxis(-angle, up) * fwd, angle * 2f, fan);
            Handles.DrawWireArc(pos, right, Quaternion.AngleAxis(-angle, right) * fwd, angle * 2f, fan);

            Handles.color = RangeLine;
            Handles.DrawDottedLine(pos, pos + fwd * range, 4f);
            Handles.Label(pos + up * size * 0.3f, index + " " + em.name);

            if (!edit) return;

            // 姿勢: シーンのツール(移動 / 回転)に従うので、既定の Transform ハンドルとぶつからない。
            // rig 自身の Transform にある射出口には Unity のハンドルが既にある。2つ目を出すとぶつかる。
            bool own = em == rig.transform;
            if (!own && Tools.current == Tool.Move)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 p = Handles.PositionHandle(pos, Tools.pivotRotation == PivotRotation.Local ? em.rotation : Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(em, "Gunner: move emitter");
                    em.position = p;
                }
            }
            else if (!own && Tools.current == Tool.Rotate)
            {
                EditorGUI.BeginChangeCheck();
                Quaternion r = Handles.RotationHandle(em.rotation, pos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(em, "Gunner: turn emitter");
                    em.rotation = r;
                }
            }

            // 射程と狙いの限度は rig のもの: 最初の射出口でだけ編集する(射出口ごとの角度はインスペクタで)。
            if (index != 0) return;
            Handles.color = RangeLine;
            EditorGUI.BeginChangeCheck();
            Vector3 tip = Handles.Slider(pos + fwd * range, fwd, size * 0.12f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float nextRange = Mathf.Max(0.1f, Vector3.Dot(tip - pos, fwd));
                GunnerEditorUtil.Edit(rig, so => so.FindProperty("maxRange").floatValue = nextRange);
            }

            Handles.color = FanLine;
            Vector3 edge = pos + Quaternion.AngleAxis(angle, up) * fwd * fan;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(edge, size * 0.08f, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 flat = Vector3.ProjectOnPlane(moved - pos, up);
                if (flat.sqrMagnitude > 1e-8f)
                {
                    float nextAngle = Mathf.Clamp(Vector3.Angle(fwd, flat), 1f, 180f);
                    // 射出口0に自分の角度があればそれを、無ければ可動域を動かす(見えている扇と同じ値)。
                    bool own0 = rig.aimMaxAngles != null && rig.aimMaxAngles.Length > 0 && rig.aimMaxAngles[0] > 0f;
                    GunnerEditorUtil.Edit(rig, so =>
                    {
                        if (own0) so.FindProperty("aimMaxAngles").GetArrayElementAtIndex(0).floatValue = Mathf.Round(nextAngle);
                        else so.FindProperty("aimMaxAngle").floatValue = Mathf.Round(nextAngle);
                    });
                }
            }
        }
    }
}
