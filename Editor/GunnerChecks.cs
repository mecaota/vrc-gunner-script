using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>Error = 動かない。Warning = 見た目や負荷がずれるおそれがある。</summary>
    public enum GunnerCheckLevel { Warning, Error }

    /// <summary>点検の1行: アイコン、文字列表の短い文、任意の直し方。</summary>
    public sealed class GunnerCheck
    {
        public GunnerCheckLevel level;
        public string key;          // 文字列表のキー。文中の "{0}" は arg に置き換える
        public string arg;
        public Object target;       // 点検ウィンドウで行をクリックした時に選ぶもの
        public Action fix;          // null = 直すボタンを無効で表示する

        public GunnerCheck(GunnerCheckLevel level, string key, Object target, Action fix = null, string arg = null)
        {
            this.level = level; this.key = key; this.target = target; this.fix = fix; this.arg = arg;
        }

        public string Text
        {
            get
            {
                string text = GunnerL10n.Tr(key);
                return arg != null && text.Contains("{0}") ? text.Replace("{0}", arg) : text;
            }
        }
    }

    /// <summary>
    /// 部品の点検を足す。ほかのパッケージはこれを実装し、[InitializeOnLoad] の static コンストラクタから
    /// <see cref="GunnerChecks.Register"/> を呼ぶ。知らない部品は無視する。
    /// </summary>
    public interface IGunnerCheckProvider
    {
        void Collect(Component component, List<GunnerCheck> into);
    }

    public static class GunnerChecks
    {
        static readonly List<IGunnerCheckProvider> providers = new List<IGunnerCheckProvider>();
        static GUIContent errorIcon, warnIcon;

        public static void Register(IGunnerCheckProvider provider)
        {
            if (provider != null && !providers.Contains(provider)) providers.Add(provider);
        }

        public static List<GunnerCheck> Run(Component component)
        {
            var list = new List<GunnerCheck>();
            if (component == null) return list;
            foreach (var p in providers)
            {
                try { p.Collect(component, list); }
                catch (Exception e) { Debug.LogException(e); }
            }
            list.Sort((a, b) => b.level.CompareTo(a.level));
            return list;
        }

        /// <summary>行を描く。true = 直しを実行した(呼び出し側はこのフレームの描画をやめる)。</summary>
        public static bool DrawRows(List<GunnerCheck> checks, bool selectOnClick = false)
        {
            if (errorIcon == null)
            {
                errorIcon = EditorGUIUtility.IconContent("console.erroricon.sml");
                warnIcon = EditorGUIUtility.IconContent("console.warnicon.sml");
            }
            bool fixedOne = false;
            foreach (var c in checks)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(c.level == GunnerCheckLevel.Error ? errorIcon : warnIcon, GUILayout.Width(18), GUILayout.Height(18));
                    var style = new GUIStyle(EditorStyles.label) { wordWrap = true };
                    if (selectOnClick)
                    {
                        string name = c.target != null ? c.target.name + ": " : "";
                        if (GUILayout.Button(name + c.Text, style) && c.target != null)
                        {
                            Selection.activeObject = c.target;
                            EditorGUIUtility.PingObject(c.target);
                        }
                    }
                    else GUILayout.Label(c.Text, style);
                    using (new EditorGUI.DisabledScope(c.fix == null))
                    {
                        if (GUILayout.Button(GunnerL10n.Tr("button.fix"), GUILayout.Width(60)) && c.fix != null)
                        {
                            c.fix();
                            fixedOne = true;
                        }
                    }
                }
                if (fixedOne) break;
            }
            return fixedOne;
        }
    }
}
