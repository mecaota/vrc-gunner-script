using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>GunnerRig のインスペクタ(射出口ごとのカード、項目のまとめ、セットアップのボタン)とシーンのハンドル: 射出口の姿勢、射程、狙いの扇。</summary>
    [CustomEditor(typeof(GunnerRig)), CanEditMultipleObjects]
    public class GunnerRigEditor : GunnerInspectorBase
    {
        const string HandlesPref = "Mecaota.Gunner.ShowHandles";
        const float CardHead = 52f;
        static readonly Color FanFill = new Color(1f, 0.6f, 0.1f, 0.12f);
        static readonly Color FanLine = new Color(1f, 0.6f, 0.1f, 0.8f);
        static readonly Color RangeLine = new Color(0.3f, 0.8f, 1f, 0.9f);
        static float Line => EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        ReorderableList cards;
        int addParentCount = 1;

        void OnEnable()
        {
            // 並べ替えは付けない: 番号は GunnerPre の番号や、ほかの部品の番号の配列と対応している。
            cards = new ReorderableList(serializedObject, serializedObject.FindProperty("emitters"), false, false, true, true)
            {
                elementHeightCallback = i => CardRows(i) * Line,
                drawElementCallback = (rect, i, active, focused) => DrawCard(rect, i),
                onCanAddCallback = list => list.count < GunnerRig.MaxEmitters,
                onAddCallback = _ =>
                {
                    var rig = (GunnerRig)target;
                    GunnerSetup.AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                    GUIUtility.ExitGUI();
                },
                onRemoveCallback = list =>
                {
                    RemoveEmitter(serializedObject, list.index);
                    list.index = Mathf.Min(list.index, list.count - 1);
                },
            };
        }

        protected override void DrawBody()
        {
            var rig = (GunnerRig)target;

            if (Foldout("GunnerRig.section.emitters"))
            {
                // rig ごとに追加の骨の並びが違うので、カードは1つを選んだときだけ。
                if (targets.Length == 1)
                {
                    cards.DoLayoutList();
                    EditorGUILayout.LabelField(T("GunnerRig.emitters.removeNote"), EditorStyles.miniLabel);
                    DrawOrphanBones(rig);
                }
                else
                    Fields("emitters", "shotParticles", "linkBones", "linkExtraBones", "linkExtraEmitter", "aimMaxAngles");
                using (new EditorGUI.DisabledScope(targets.Length != 1))
                {
                    if (GUILayout.Button(T("button.place_hand")))
                    {
                        Selection.activeObject = GunnerSetup.PlaceHand(rig).gameObject;
                        GUIUtility.ExitGUI();
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

            if (Foldout("GunnerRig.section.shot"))
                Fields("fireMode", "shotSpeed", "maxRange", "castRadius", "aimMaxAngle", "fireAnimator");

            if (Foldout("GunnerRig.section.sound"))
                Fields("fireClip", "impactClip", "fireVolume", "impactVolume", "pitchJitter", "fireVoices", "impactVoices",
                    "impactParticles", "impactBurst", "fireMinInterval", "impactMinInterval",
                    "soundOnPlayers", "soundOnWorld", "soundOnMoving", "soundCullDistance");

            if (Foldout("GunnerRig.section.hit", false))
                Fields("receivers", "movers", "hitMask", "hitPlayers", "excludeOwner", "playerRadius", "maxHits");

            if (Foldout("GunnerRig.section.stream", false))
                Fields("stream", "streamRate", "streamJoin", "streamHitMode", "streamHitInterval", "streamCollide");

            if (Foldout("GunnerRig.section.bone", false))
                Fields("boneFollow", "boneStrength", "boneYawMax", "bonePitchUpMax", "bonePitchDownMax",
                    "boneSmooth", "boneMinSpeed", "boneMaxSpeed");
        }

        void Fields(params string[] names)
        {
            foreach (var name in names) Field(name);
        }

        // ---------------- 射出口のカード ----------------

        int ExtraOwner(int k)
        {
            var owners = serializedObject.FindProperty("linkExtraEmitter");
            return k < owners.arraySize ? owners.GetArrayElementAtIndex(k).intValue : -1;
        }

        // 1行目、粒子・可動域・連動ボーンの3行、所属する追加の骨、親の骨を足す行(連動があるとき)。
        int CardRows(int i)
        {
            if (!cards.serializedProperty.GetArrayElementAtIndex(i).isExpanded) return 1;
            int rows = 4 + (LeadBone((GunnerRig)target, i) != null ? 1 : 0);
            int bones = serializedObject.FindProperty("linkExtraBones").arraySize;
            for (int k = 0; k < bones; k++) if (ExtraOwner(k) == i) rows++;
            return rows;
        }

        void DrawCard(Rect rect, int i)
        {
            var rig = (GunnerRig)target;
            var emitter = cards.serializedProperty.GetArrayElementAtIndex(i);
            var row = new Rect(rect.x, rect.y + 1f, rect.width, EditorGUIUtility.singleLineHeight);
            emitter.isExpanded = EditorGUI.Foldout(new Rect(row.x + 12f, row.y, CardHead - 12f, row.height), emitter.isExpanded, CardTitle(rig, i), true);
            EditorGUI.PropertyField(new Rect(row.x + CardHead, row.y, row.width - CardHead, row.height), emitter, GUIContent.none);
            if (!emitter.isExpanded) return;

            row = new Rect(row.x + CardHead, row.y, row.width - CardHead, row.height);
            row.y += Line;
            ObjectAt(row, "shotParticles", i, typeof(ParticleSystem));
            row.y += Line;
            var angles = serializedObject.FindProperty("aimMaxAngles");
            float angle = i < angles.arraySize ? angles.GetArrayElementAtIndex(i).floatValue : 0f;
            EditorGUI.BeginChangeCheck();
            angle = EditorGUI.FloatField(row, new GUIContent(T("GunnerRig.aimMaxAngle"), T("GunnerRig.aimMaxAngles.row")), angle);
            if (EditorGUI.EndChangeCheck()) Grow(angles, i).floatValue = Mathf.Clamp(angle, 0f, 180f);
            row.y += Line;
            ObjectAt(row, "linkBones", i, typeof(Transform));

            var label = Label("GunnerRig.linkExtraBones");
            int bones = serializedObject.FindProperty("linkExtraBones").arraySize;
            for (int k = 0; k < bones; k++)
            {
                if (ExtraOwner(k) != i) continue;
                row.y += Line;
                ExtraRow(row, rig, k, label);
                label = new GUIContent(" ");
            }
            if (LeadBone(rig, i) == null) return;
            row.y += Line;
            var field = EditorGUI.PrefixLabel(row, new GUIContent(" "));
            addParentCount = Mathf.Clamp(EditorGUI.IntField(new Rect(field.x, field.y, 40f, field.height), addParentCount), 1, 16);
            if (GUI.Button(new Rect(field.x + 44f, field.y, field.width - 44f, field.height), T("button.add_parent_bones").Replace("{0}", addParentCount.ToString())))
            {
                int n = addParentCount;
                GunnerEditorUtil.Edit(rig, so => AddParentBones(rig, so, i, n));
                GUIUtility.ExitGUI();
            }
        }

        GUIContent CardTitle(GunnerRig rig, int i)
        {
            var emitter = rig.emitters != null && i < rig.emitters.Length ? rig.emitters[i] : null;
            var particles = rig.shotParticles != null && i < rig.shotParticles.Length ? rig.shotParticles[i] : null;
            foreach (var c in checks)
            {
                if (c.target == null || (c.target != emitter && c.target != particles)) continue;
                string icon = c.level == GunnerCheckLevel.Error ? "console.erroricon.sml" : "console.warnicon.sml";
                return new GUIContent(i.ToString(), EditorGUIUtility.IconContent(icon).image, c.Text);
            }
            return new GUIContent(i.ToString());
        }

        // 配列が射出口より短いときは空の欄を出し、値を入れたときにだけ伸ばす(見るだけでプレハブのオーバーライドを作らない)。
        void ObjectAt(Rect row, string array, int i, System.Type type)
        {
            var arr = serializedObject.FindProperty(array);
            var label = Label("GunnerRig." + array);
            if (i < arr.arraySize)
            {
                EditorGUI.PropertyField(row, arr.GetArrayElementAtIndex(i), label);
                return;
            }
            var value = EditorGUI.ObjectField(row, label, null, type, true);
            if (value != null) Grow(arr, i).objectReferenceValue = value;
        }

        // arraySize を増やすと末尾の値が写るので、間に増えた要素は空に戻す。
        static SerializedProperty Grow(SerializedProperty arr, int i)
        {
            int from = arr.arraySize;
            if (from <= i) arr.arraySize = i + 1;
            for (int n = from; n < i; n++)
            {
                var e = arr.GetArrayElementAtIndex(n);
                if (e.propertyType == SerializedPropertyType.ObjectReference) e.objectReferenceValue = null;
                else e.floatValue = 0f;
            }
            return arr.GetArrayElementAtIndex(i);
        }

        /// <summary>射出口 i を、同じ番号の欄とその追加の骨ごと外す。</summary>
        static void RemoveEmitter(SerializedObject so, int i)
        {
            foreach (var name in new[] { "emitters", "shotParticles", "linkBones", "aimMaxAngles" })
            {
                var arr = so.FindProperty(name);
                if (i < arr.arraySize) GunnerEditorUtil.RemoveAt(arr, i);
            }
            var owners = so.FindProperty("linkExtraEmitter");
            for (int k = owners.arraySize - 1; k >= 0; k--)
            {
                var owner = owners.GetArrayElementAtIndex(k);
                if (owner.intValue == i) RemoveExtraAt(so, k);
                else if (owner.intValue > i) owner.intValue--;
            }
        }

        // 射出口 i の狙える角度(rig の AimLimit と同じ決まり)。
        internal static float EmitterLimit(GunnerRig rig, int i)
        {
            var angles = rig.aimMaxAngles;
            if (angles != null && i < angles.Length && angles[i] > 0f) return angles[i];
            return rig.aimMaxAngle;
        }

        // ---------------- 追加の連動ボーン ----------------

        // どの射出口にも属さない骨(点検が赤を出す)。消せるように並べる。
        void DrawOrphanBones(GunnerRig rig)
        {
            int emitters = cards.count;
            int bones = serializedObject.FindProperty("linkExtraBones").arraySize;
            bool labelShown = false;
            for (int k = 0; k < bones; k++)
            {
                int owner = ExtraOwner(k);
                if (owner >= 0 && owner < emitters) continue;
                if (!labelShown) EditorGUILayout.LabelField(T("GunnerRig.extra.orphan"));
                labelShown = true;
                ExtraRow(EditorGUILayout.GetControlRect(), rig, k, GUIContent.none);
            }
        }

        void ExtraRow(Rect row, GunnerRig rig, int k, GUIContent label)
        {
            var bones = serializedObject.FindProperty("linkExtraBones");
            EditorGUI.PropertyField(new Rect(row.x, row.y, row.width - 28f, row.height), bones.GetArrayElementAtIndex(k), label);
            if (GUI.Button(new Rect(row.xMax - 24f, row.y, 24f, row.height), "-"))
            {
                GunnerEditorUtil.Edit(rig, so => RemoveExtraAt(so, k));
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>射出口 i の先頭の骨(実行時と同じ決め方)。連動しない射出口は null。</summary>
        internal static Transform LeadBone(GunnerRig rig, int i)
        {
            if (rig == null || rig.emitters == null || i < 0 || i >= rig.emitters.Length) return null;
            var em = rig.emitters[i];
            if (em == null) return null;
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
            if (bones != null && k < bones.arraySize) GunnerEditorUtil.RemoveAt(bones, k);
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
