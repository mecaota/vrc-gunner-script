using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// 開いているシーンの全 U# 部品の点検を1つの一覧にまとめる。行をクリックするとそのオブジェクトを選ぶ。ほかのパッケージの部品も、
    /// <see cref="IGunnerCheckProvider"/> を登録すればここに出る。
    /// </summary>
    public class GunnerCheckWindow : EditorWindow
    {
        Vector2 scroll;
        List<GunnerCheck> rows;

        public static void Open()
        {
            var w = GetWindow<GunnerCheckWindow>();
            w.titleContent = new GUIContent("Gunner Check");
            w.Refresh();
            w.Show();
        }

        void OnEnable() => Refresh();

        void OnHierarchyChange()
        {
            Refresh();
            Repaint();
        }

        void Refresh()
        {
            rows = new List<GunnerCheck>();
            foreach (var b in GunnerEditorUtil.FindInScenes<UdonSharpBehaviour>()) rows.AddRange(GunnerChecks.Run(b));
            rows.Sort((a, b) => b.level.CompareTo(a.level));
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(GunnerL10n.Tr("button.recheck"), EditorStyles.toolbarButton, GUILayout.Width(80))) Refresh();
                GUILayout.FlexibleSpace();
            }
            if (rows == null) Refresh();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (rows.Count == 0) EditorGUILayout.HelpBox(GunnerL10n.Tr("check.all_ok"), MessageType.Info);
            else if (GunnerChecks.DrawRows(rows, true))
            {
                Refresh();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndScrollView();
            GunnerL10n.DrawLanguagePopup();
        }
    }
}
