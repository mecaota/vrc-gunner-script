using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>GunnerRig のインスペクタ(射出口の一覧と共通の設定)と、全部の射出口のシーンのハンドル。</summary>
    [CustomEditor(typeof(GunnerRig)), CanEditMultipleObjects]
    public class GunnerRigEditor : GunnerInspectorBase
    {
        internal const string HandlesPref = "Mecaota.Gunner.ShowHandles";
        const float RemoveWidth = 48f;

        protected override void DrawBody()
        {
            var rig = (GunnerRig)target;

            bool open = Foldout("GunnerRig.section.emitters");
            var muzzles = serializedObject.FindProperty("muzzles");
            if (targets.Length == 1) CountAt(GUILayoutUtility.GetLastRect(), muzzles.arraySize);
            if (open)
            {
                if (targets.Length == 1)
                {
                    for (int i = 0; i < muzzles.arraySize; i++) DrawRow(rig, muzzles.GetArrayElementAtIndex(i), i);
                    using (new EditorGUI.DisabledScope(muzzles.arraySize >= GunnerRig.MaxEmitters))
                    {
                        if (GUILayout.Button(T("GunnerRig.emitters.add")))
                        {
                            GunnerSetup.AddEmitter(rig, null, rig.transform.position, rig.transform.rotation);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUILayout.LabelField(T("GunnerRig.emitters.removeNote"), EditorStyles.wordWrappedMiniLabel);
                }
                else
                    EditorGUILayout.HelpBox(T("GunnerRig.emitters.multi"), MessageType.Info);
                EditorGUILayout.Space();
                bool show = EditorPrefs.GetBool(HandlesPref, true);
                bool next = EditorGUILayout.Toggle(Label("GunnerRig.showHandles"), show);
                if (next != show)
                {
                    EditorPrefs.SetBool(HandlesPref, next);
                    SceneView.RepaintAll();
                }
            }

            if (Foldout("GunnerRig.section.shot"))
            {
                Fields("stream", "fireAnimator");
                if (GUILayout.Button(T("GunnerRig.syncAnimator")))
                    foreach (GunnerRig r in targets) GunnerAnimatorSync.Sync(r);
            }

            if (Foldout("GunnerRig.section.hit", false))
                Fields("receivers", "movers", "playerRadius", "maxHits");

            if (Foldout("GunnerRig.section.bone", false))
                Field("boneFollow");
        }

        void Fields(params string[] names)
        {
            foreach (var name in names) Field(name);
        }

        static void CountAt(Rect row, int n)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.IntField(new Rect(row.xMax - 40f, row.y, 40f, EditorGUIUtility.singleLineHeight), n);
        }

        // 「外す」は一覧から外すだけで、GameObject は消さない。
        void DrawRow(GunnerRig rig, SerializedProperty element, int i)
        {
            var row = EditorGUILayout.GetControlRect();
            var label = RowLabel(element.objectReferenceValue as GunnerEmitter, i);
            EditorGUI.PropertyField(new Rect(row.x, row.y, row.width - RemoveWidth - 4f, row.height), element, label);
            if (GUI.Button(new Rect(row.xMax - RemoveWidth, row.y, RemoveWidth, row.height), T("GunnerRig.emitters.remove")))
            {
                GunnerEditorUtil.Edit(rig, so => GunnerEditorUtil.RemoveAt(so.FindProperty("muzzles"), i));
                GUIUtility.ExitGUI();
            }
        }

        static GUIContent RowLabel(GunnerEmitter muzzle, int i)
        {
            foreach (var c in GunnerChecks.Run(muzzle))
            {
                string icon = c.level == GunnerCheckLevel.Error ? "console.erroricon.sml" : "console.warnicon.sml";
                return new GUIContent(i.ToString(), EditorGUIUtility.IconContent(icon).image, c.Text);
            }
            return new GUIContent(i.ToString());
        }

        void OnSceneGUI()
        {
            var rig = (GunnerRig)target;
            if (rig == null || rig.muzzles == null) return;
            for (int i = 0; i < rig.muzzles.Length; i++)
                GunnerEmitterEditor.DrawHandles(rig.muzzles[i], i, rig.transform);
        }
    }
}
