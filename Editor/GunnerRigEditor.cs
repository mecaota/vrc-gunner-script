using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>GunnerRig のインスペクタ(射出口ごとのカード、項目のまとめ、セットアップのボタン)とシーンのハンドル: 射出口の姿勢、射程、狙いの扇。</summary>
    [CustomEditor(typeof(GunnerRig)), CanEditMultipleObjects]
    public class GunnerRigEditor : GunnerInspectorBase
    {
        const string HandlesPref = "Mecaota.Gunner.ShowHandles";
        const float CardHead = 52f;
        const float RemoveWidth = 48f;
        static readonly Color FanFill = new Color(1f, 0.6f, 0.1f, 0.12f);
        static readonly Color FanLine = new Color(1f, 0.6f, 0.1f, 0.8f);
        static readonly Color RangeLine = new Color(0.3f, 0.8f, 1f, 0.9f);

        protected override void DrawBody()
        {
            var rig = (GunnerRig)target;

            bool open = Foldout("GunnerRig.section.emitters");
            var emitters = serializedObject.FindProperty("emitters");
            if (targets.Length == 1) CountAt(GUILayoutUtility.GetLastRect(), emitters.arraySize);
            if (open)
            {
                // rig ごとに追加の骨の並びが違うので、カードは1つを選んだときだけ。
                if (targets.Length == 1)
                {
                    for (int i = 0; i < emitters.arraySize; i++) DrawCard(rig, emitters, i);
                    using (new EditorGUI.DisabledScope(emitters.arraySize >= GunnerRig.MaxEmitters))
                    {
                        if (GUILayout.Button(T("GunnerRig.emitters.add")))
                        {
                            GunnerSetup.AddEmitter(rig, rig.transform, rig.transform.position, rig.transform.rotation);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.LabelField(T("GunnerRig.emitters.removeNote"), EditorStyles.wordWrappedMiniLabel);
                    DrawOrphanBones(rig);
                }
                else
                    EditorGUILayout.HelpBox(T("GunnerRig.emitters.multi"), MessageType.Info);
                EditorGUILayout.Space();
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
                Fields("fireMode", "aimMaxAngle", "fireAnimator");

            if (Foldout("GunnerRig.section.hit", false))
                Fields("receivers", "movers", "hitPlayers", "excludeOwner", "playerRadius", "maxHits");

            if (Foldout("GunnerRig.section.stream", false))
                Fields("stream", "streamCollide");

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

        // 根元から先端の順: 追加の骨を浅い順に、最後に先端の骨(-1)。
        List<int> BoneRows(int i)
        {
            var rows = new List<int>();
            var bones = serializedObject.FindProperty("linkExtraBones");
            for (int k = 0; k < bones.arraySize; k++) if (ExtraOwner(k) == i) rows.Add(k);
            rows.Sort((a, b) => Depth(bones, a).CompareTo(Depth(bones, b)));
            rows.Add(-1);
            return rows;
        }

        static int Depth(SerializedProperty bones, int k)
        {
            int d = 0;
            for (var t = bones.GetArrayElementAtIndex(k).objectReferenceValue as Transform; t != null; t = t.parent) d++;
            return d;
        }

        static void CountAt(Rect row, int n)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.IntField(new Rect(row.xMax - 40f, row.y, 40f, EditorGUIUtility.singleLineHeight), n);
        }

        // 小見出しの下の欄は一段内側に置くので、小見出しだけ一段戻す。
        void Heading(string key)
        {
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(T(key), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
        }

        void DrawCard(GunnerRig rig, SerializedProperty emitters, int i)
        {
            var emitter = emitters.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var row = EditorGUILayout.GetControlRect();
                emitter.isExpanded = EditorGUI.Foldout(new Rect(row.x + 12f, row.y, CardHead - 12f, row.height), emitter.isExpanded, CardTitle(rig, i), true);
                EditorGUI.PropertyField(new Rect(row.x + CardHead, row.y, row.width - CardHead - RemoveWidth - 4f, row.height), emitter, GUIContent.none);
                if (GUI.Button(new Rect(row.xMax - RemoveWidth, row.y, RemoveWidth, row.height), T("GunnerRig.emitters.remove")))
                {
                    GunnerEditorUtil.Edit(rig, so => RemoveEmitter(so, i));
                    GUIUtility.ExitGUI();
                }
                if (emitter.isExpanded) DrawCardBody(rig, i);
            }
            EditorGUILayout.Space(2f);
        }

        void DrawCardBody(GunnerRig rig, int i)
        {
            ObjectAt("shotParticles", i, typeof(ParticleSystem));
            EditorGUI.indentLevel++;

            Heading("GunnerRig.card.fire");
            ObjectAt("fireSources", i, typeof(AudioSource));
            FloatAt("fireMinIntervals", i, "GunnerRig.fireMinIntervals", 0f, float.MaxValue);
            FloatAt("shotSpeeds", i, "GunnerRig.shotSpeeds", 25f, float.MaxValue);
            FloatAt("maxRanges", i, "GunnerRig.maxRanges", 30f, float.MaxValue);
            FloatAt("castRadii", i, "GunnerRig.castRadii", 0.05f, float.MaxValue);

            Heading("GunnerRig.card.impact");
            ObjectAt("impactSources", i, typeof(AudioSource));
            FloatAt("impactMinIntervals", i, "GunnerRig.impactMinIntervals", 0f, float.MaxValue);
            ObjectAt("impactParticles", i, typeof(ParticleSystem));
            var bursts = serializedObject.FindProperty("impactBursts");
            if (i < bursts.arraySize) EditorGUILayout.PropertyField(bursts.GetArrayElementAtIndex(i), Label("GunnerRig.impactBursts"));
            else
            {
                int burst = Mathf.Max(1, EditorGUILayout.IntField(Label("GunnerRig.impactBursts"), 1));
                if (burst != 1) Grow(bursts, i, 1).intValue = burst;
            }

            Heading("GunnerRig.card.stream");
            FloatAt("streamRates", i, "GunnerRig.streamRates", 12f, float.MaxValue);
            PopupAt("streamJoins", i, "GunnerRig.streamJoins", GunnerStream.JoinLine);
            PopupAt("streamHitModes", i, "GunnerRig.streamHitModes", GunnerStream.HitOnce);
            FloatAt("streamHitIntervals", i, "GunnerRig.streamHitIntervals", 0.25f, float.MaxValue);

            Heading("GunnerRig.card.aim");
            FloatAt("aimMaxAngles", i, "GunnerRig.aimMaxAngles.row", 0f, 180f);
            DrawBones(rig, i);
            EditorGUI.indentLevel--;
        }

        // 骨1本を1つの箱に入れ、欄はほかの欄と同じ位置に並べる。箱はラベルの文字が線に接しないよう少し左へ広げる。
        void DrawBones(GunnerRig rig, int i)
        {
            var rows = BoneRows(i);
            var head = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(head, Label("GunnerRig.linkExtraBones"));
            CountAt(head, rows.Count - (LeadBone(rig, i) == null ? 1 : 0));
            var bones = serializedObject.FindProperty("linkExtraBones");
            for (int r = 0; r < rows.Count; r++)
            {
                var box = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4f);
                var frame = EditorGUI.IndentedRect(box);
                frame.xMin -= 4f;
                GUI.Box(frame, GUIContent.none, EditorStyles.helpBox);
                var row = new Rect(box.x, box.y + 2f, box.width, EditorGUIUtility.singleLineHeight);
                if (rows[r] >= 0) ExtraRow(row, rig, rows[r], r == 0 ? Label("GunnerRig.linkRoot") : new GUIContent(" "));
                else
                {
                    ObjectAt("linkBones", i, typeof(Transform), FieldPart(row));
                    if (MinusAt(row))
                    {
                        GunnerEditorUtil.Edit(rig, so =>
                        {
                            var lead = so.FindProperty("linkBones");
                            if (i < lead.arraySize) lead.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        });
                        GUIUtility.ExitGUI();
                    }
                }
            }
            using (new EditorGUI.DisabledScope(LeadBone(rig, i) == null))
            {
                if (GUILayout.Button(T("GunnerRig.bones.add")))
                {
                    GunnerEditorUtil.Edit(rig, so => AddParentBones(rig, so, i, 1));
                    GUIUtility.ExitGUI();
                }
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
        void ObjectAt(string array, int i, System.Type type) => ObjectAt(array, i, type, EditorGUILayout.GetControlRect());

        void ObjectAt(string array, int i, System.Type type, Rect row)
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

        // 要素が無いときは empty を出す。empty は実行時が要素の無い射出口に使う値と同じにする。
        void FloatAt(string array, int i, string labelKey, float empty, float max)
        {
            var row = EditorGUILayout.GetControlRect();
            var arr = serializedObject.FindProperty(array);
            bool has = i < arr.arraySize;
            var label = Label(labelKey);
            if (has) label = EditorGUI.BeginProperty(row, label, arr.GetArrayElementAtIndex(i));
            EditorGUI.BeginChangeCheck();
            float value = EditorGUI.FloatField(row, label, has ? arr.GetArrayElementAtIndex(i).floatValue : empty);
            if (EditorGUI.EndChangeCheck()) Grow(arr, i, empty).floatValue = Mathf.Clamp(value, 0f, max);
            if (has) EditorGUI.EndProperty();
        }

        // 選択肢は "<labelKey>.opt"。
        void PopupAt(string array, int i, string labelKey, int empty)
        {
            var row = EditorGUILayout.GetControlRect();
            var arr = serializedObject.FindProperty(array);
            bool has = i < arr.arraySize;
            var label = Label(labelKey);
            if (has) label = EditorGUI.BeginProperty(row, label, arr.GetArrayElementAtIndex(i));
            var options = System.Array.ConvertAll(T(labelKey + ".opt").Split('|'), s => new GUIContent(s));
            EditorGUI.BeginChangeCheck();
            int value = EditorGUI.Popup(row, label, Mathf.Clamp(has ? arr.GetArrayElementAtIndex(i).intValue : empty, 0, options.Length - 1), options);
            if (EditorGUI.EndChangeCheck()) Grow(arr, i, empty).intValue = value;
            if (has) EditorGUI.EndProperty();
        }

        // arraySize を増やすと末尾の値が写るので、間に増えた要素は fill に戻す。
        static SerializedProperty Grow(SerializedProperty arr, int i, float fill = 0f)
        {
            int from = arr.arraySize;
            if (from <= i) arr.arraySize = i + 1;
            for (int n = from; n < i; n++)
            {
                var e = arr.GetArrayElementAtIndex(n);
                if (e.propertyType == SerializedPropertyType.ObjectReference) e.objectReferenceValue = null;
                else if (e.propertyType == SerializedPropertyType.Integer) e.intValue = (int)fill;
                else e.floatValue = fill;
            }
            return arr.GetArrayElementAtIndex(i);
        }

        /// <summary>emitters と同じ番号の配列。</summary>
        internal static readonly string[] PerEmitter = { "shotParticles", "linkBones", "aimMaxAngles",
            "fireSources", "fireMinIntervals", "impactSources", "impactMinIntervals", "impactParticles", "impactBursts",
            "shotSpeeds", "maxRanges", "castRadii", "streamRates", "streamJoins", "streamHitModes", "streamHitIntervals" };

        /// <summary>射出口 i を、同じ番号の欄とその追加の骨ごと外す。</summary>
        static void RemoveEmitter(SerializedObject so, int i)
        {
            foreach (var name in PerEmitter.Prepend("emitters"))
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
            int emitters = serializedObject.FindProperty("emitters").arraySize;
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

        static Rect FieldPart(Rect row) => new Rect(row.x, row.y, row.width - 28f, row.height);

        static bool MinusAt(Rect row) => GUI.Button(new Rect(row.xMax - 24f, row.y, 24f, row.height), "-");

        void ExtraRow(Rect row, GunnerRig rig, int k, GUIContent label)
        {
            var bones = serializedObject.FindProperty("linkExtraBones");
            EditorGUI.PropertyField(FieldPart(row), bones.GetArrayElementAtIndex(k), label);
            if (MinusAt(row))
            {
                GunnerEditorUtil.Edit(rig, so => RemoveExtraAt(so, k));
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>射出口 i の先端の骨(実行時と同じ決め方)。連動しない射出口は null。</summary>
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

        /// <summary>射出口の鎖のいちばん上(先端の骨か、登録済みの追加の骨のうち最も上)から、親を n 個足す。</summary>
        internal static void AddParentBones(GunnerRig rig, SerializedObject so, int emitter, int n)
        {
            Transform lead = LeadBone(rig, emitter);
            if (lead == null) return;
            var bones = so.FindProperty("linkExtraBones");
            var owners = so.FindProperty("linkExtraEmitter");
            // 同じ先端の骨を持つ射出口は1つの鎖になるので、そのどれかに登録済みの骨をすべて数える(重複を作らない)。
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
            float range = Mathf.Max(0f, rig.maxRanges != null && index < rig.maxRanges.Length ? rig.maxRanges[index] : 30f);
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

            Handles.color = RangeLine;
            EditorGUI.BeginChangeCheck();
            Vector3 tip = Handles.Slider(pos + fwd * range, fwd, size * 0.12f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float nextRange = Mathf.Max(0.1f, Vector3.Dot(tip - pos, fwd));
                GunnerEditorUtil.Edit(rig, so => Grow(so.FindProperty("maxRanges"), index, 30f).floatValue = nextRange);
            }

            // 既定の可動域は rig のもの: 最初の射出口でだけ編集する(射出口ごとの角度はインスペクタで)。
            if (index != 0) return;
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
