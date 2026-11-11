using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// Editor-only string table (Japanese / English).
    /// Each package keeps Editor/strings.tsv (columns: key, ja, en). Other packages add
    /// their own table with <see cref="AddTable"/>. Lookup falls back: chosen language -> ja -> key.
    /// Do not use [Tooltip]; draw every label and tooltip through <see cref="Tr"/>.
    /// </summary>
    public static class GunnerL10n
    {
        public const string Japanese = "ja";
        public const string English = "en";
        const string PrefKey = "Mecaota.Gunner.Language"; // "", "ja" or "en". Empty = follow the editor.
        const string GunnerTableGuid = "a8c460bbc0ac4ae09789c3dfcacb83cd"; // Editor/strings.tsv

        static readonly List<string> tablePaths = new List<string>();
        static Dictionary<string, string[]> table; // key -> { ja, en }

        /// <summary>Current language: the saved choice, else the editor language (Japanese -> ja, others -> en).</summary>
        public static string Language
        {
            get
            {
                string saved = EditorPrefs.GetString(PrefKey, "");
                if (saved == Japanese || saved == English) return saved;
                return EditorPrefs.GetString("Editor.kEditorLocale", "") == "Japanese" ? Japanese : English;
            }
            set => EditorPrefs.SetString(PrefKey, value == Japanese || value == English ? value : "");
        }

        /// <summary>The saved choice as is: "" (follow the editor), "ja" or "en".</summary>
        public static string SavedChoice => EditorPrefs.GetString(PrefKey, "");

        /// <summary>Language popup (follow the editor / 日本語 / English) for the bottom of inspectors and the check window.</summary>
        public static void DrawLanguagePopup()
        {
            string saved = SavedChoice;
            int index = saved == Japanese ? 1 : saved == English ? 2 : 0;
            string[] options = { Tr("gunner.language.auto"), "日本語", "English" };
            int next = EditorGUILayout.Popup(Tr("gunner.language"), index, options);
            if (next == index) return;
            Language = next == 1 ? Japanese : next == 2 ? English : "";
            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>Registers another package's strings.tsv (asset path). Later tables override earlier keys.</summary>
        public static void AddTable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || tablePaths.Contains(assetPath)) return;
            tablePaths.Add(assetPath);
            table = null;
        }

        /// <summary>Text for the key in the current language.</summary>
        public static string Tr(string key) => Tr(key, Language);

        public static string Tr(string key, string language)
        {
            if (TryGet(key, language, out string text)) return text;
            if (language != Japanese && TryGet(key, Japanese, out text)) return text;
            return key;
        }

        /// <summary>True if the key has text in the given language (no fallback). For checks.</summary>
        public static bool Has(string key, string language) => TryGet(key, language, out _);

        /// <summary>Label with its tooltip (key + ".tip"); the tooltip is empty when the key has none.</summary>
        public static GUIContent Content(string key)
        {
            string tip = Tr(key + ".tip");
            return new GUIContent(Tr(key), tip == key + ".tip" ? "" : tip);
        }

        public static void Reload() => table = null;

        static bool TryGet(string key, string language, out string text)
        {
            EnsureLoaded();
            text = null;
            if (key == null || !table.TryGetValue(key, out string[] row)) return false;
            text = language == Japanese ? row[0] : row[1];
            return !string.IsNullOrEmpty(text);
        }

        static void EnsureLoaded()
        {
            if (table != null) return;
            table = new Dictionary<string, string[]>();
            Load(AssetDatabase.GUIDToAssetPath(GunnerTableGuid));
            foreach (string path in tablePaths) Load(path);
        }

        static void Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            string fullPath = Path.GetFullPath(assetPath); // resolves "Packages/<id>/..." to the real folder
            if (!File.Exists(fullPath)) return;
            string[] lines = File.ReadAllLines(fullPath, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++) // line 0 is the header
            {
                if (lines[i].Length == 0 || lines[i][0] == '#') continue;
                string[] cols = lines[i].Split('\t');
                if (cols[0].Length == 0) continue;
                table[cols[0]] = new[] { Cell(cols, 1), Cell(cols, 2) };
            }
        }

        static string Cell(string[] cols, int i) => i < cols.Length ? cols[i].Replace("\\n", "\n") : "";

        [MenuItem("Tools/Gunner/Debug/Print Localization Test")]
        static void PrintTest()
        {
            Reload();
            Debug.Log($"[Gunner] locale='{EditorPrefs.GetString("Editor.kEditorLocale", "")}' language={Language}\n" +
                      $"ja: {Tr("gunner.test.hello", Japanese)}\nen: {Tr("gunner.test.hello", English)}\n" +
                      $"en missing -> ja: {Tr("gunner.test.missing_en", English)}\nunknown -> key: {Tr("gunner.test.unknown")}");
        }
    }
}
