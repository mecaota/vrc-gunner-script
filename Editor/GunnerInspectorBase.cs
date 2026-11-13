using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// gunner の全部品で共通のインスペクタの並び: U# のヘッダ、上に点検の行、フィールド(ラベルと説明はすべて
    /// 文字列表から。キーは "&lt;Type&gt;.&lt;field&gt;")、下に言語の選択。
    /// "&lt;key&gt;.opt" の項目("A|B|C")がある int のフィールドは選択式で描く。
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

            // 最初の1つだけでなく、選んだオブジェクトをすべて点検する。
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

        /// <summary>既定の中身: 見えるシリアライズ済みフィールドを宣言順にすべて。</summary>
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

        // フィールドの文がある最も派生した型を使う。ほかのパッケージの派生クラスは基底のラベルをそのまま使える。
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

    // ほかのパッケージの受け手も、独自のエディタが無ければ同じ並びになる。
    [CustomEditor(typeof(GunnerHitReceiver), true), CanEditMultipleObjects]
    public class GunnerHitReceiverEditor : GunnerInspectorBase { }

    [CustomEditor(typeof(GunnerStream)), CanEditMultipleObjects]
    public class GunnerStreamEditor : GunnerInspectorBase { }
}
