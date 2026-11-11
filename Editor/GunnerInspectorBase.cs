using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// Inspector layout shared by every gunner component: U# header, the check rows on top, the fields (all labels and tips
    /// from the string table, keys "&lt;Type&gt;.&lt;field&gt;"), and the language popup at the bottom.
    /// Int fields with a "&lt;key&gt;.opt" entry ("A|B|C") are drawn as a popup.
    /// </summary>
    public abstract class GunnerInspectorBase : Editor
    {
        readonly HashSet<string> missing = new HashSet<string>();
        string typeName;

        protected string TypeName => typeName ?? (typeName = target.GetType().Name);

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(targets)) return;
            missing.Clear();

            // Every selected object is checked, not just the first one.
            var checks = new List<GunnerCheck>();
            foreach (var t in targets) checks.AddRange(GunnerChecks.Run(t as Component));
            if (GunnerChecks.DrawRows(checks, targets.Length > 1)) GUIUtility.ExitGUI();

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawBody();
            if (EditorGUI.EndChangeCheck() || serializedObject.hasModifiedProperties)
            {
                serializedObject.ApplyModifiedProperties();
                foreach (var t in targets) GunnerEditorUtil.CommitProxy(t as UdonSharpBehaviour);
            }

            DrawMissingRow();
            EditorGUILayout.Space();
            GunnerL10n.DrawLanguagePopup();
        }

        /// <summary>Default body: every visible serialized field in declaration order.</summary>
        protected virtual void DrawBody()
        {
            var it = serializedObject.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.name == "m_Script") continue;
                Field(it.name);
            }
        }

        protected SerializedProperty Field(string name)
        {
            var p = serializedObject.FindProperty(name);
            if (p == null) return null;
            string key = KeyFor(name);
            var label = Label(key);
            string optKey = key + ".opt";
            if (p.propertyType == SerializedPropertyType.Integer && GunnerL10n.Tr(optKey) != optKey)
            {
                string[] options = GunnerL10n.Tr(optKey).Split('|');
                EditorGUI.showMixedValue = p.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int v = EditorGUILayout.Popup(label, Mathf.Clamp(p.intValue, 0, options.Length - 1), options);
                if (EditorGUI.EndChangeCheck()) p.intValue = v;
                EditorGUI.showMixedValue = false;
            }
            else EditorGUILayout.PropertyField(p, label, true);
            return p;
        }

        // The most derived type that has text for the field, so a subclass from another package reuses its base's labels.
        string KeyFor(string field)
        {
            for (var t = target.GetType(); t != null && t != typeof(UdonSharpBehaviour); t = t.BaseType)
            {
                string key = t.Name + "." + field;
                if (GunnerL10n.Has(key, GunnerL10n.Japanese) || GunnerL10n.Has(key, GunnerL10n.English)) return key;
            }
            return TypeName + "." + field;
        }

        protected GUIContent Label(string key)
        {
            if (!GunnerL10n.Has(key, GunnerL10n.Language)) missing.Add(key);
            return GunnerL10n.Content(key);
        }

        protected string T(string key)
        {
            if (!GunnerL10n.Has(key, GunnerL10n.Language)) missing.Add(key);
            return GunnerL10n.Tr(key);
        }

        protected bool Foldout(string key)
        {
            string state = "Mecaota.Gunner.Fold." + key;
            bool open = SessionState.GetBool(state, true);
            bool next = EditorGUILayout.Foldout(open, T(key), true, EditorStyles.foldoutHeader);
            if (next != open) SessionState.SetBool(state, next);
            return next;
        }

        void DrawMissingRow()
        {
            if (missing.Count == 0) return;
            var row = new GunnerCheck(GunnerCheckLevel.Warning, "check.l10n.missing", target, null, string.Join(", ", missing));
            GunnerChecks.DrawRows(new List<GunnerCheck> { row });
        }
    }

    [CustomEditor(typeof(GunnerHandController)), CanEditMultipleObjects]
    public class GunnerHandControllerEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerPickupInput)), CanEditMultipleObjects]
    public class GunnerPickupInputEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerStickAim)), CanEditMultipleObjects]
    public class GunnerStickAimEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerMoveHold)), CanEditMultipleObjects]
    public class GunnerMoveHoldEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerHitSplash)), CanEditMultipleObjects]
    public class GunnerHitSplashEditor : GunnerInspectorBase { }

    // Receivers from other packages get the same layout unless they bring their own editor.
    [CustomEditor(typeof(GunnerHitReceiver), true), CanEditMultipleObjects]
    public class GunnerHitReceiverEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerStream)), CanEditMultipleObjects]
    public class GunnerStreamEditor : GunnerInspectorBase { }
}
