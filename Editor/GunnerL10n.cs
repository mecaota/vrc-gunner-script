using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// エディタ専用の文字列表(日本語 / 英語)。
    /// 各パッケージは Editor/strings.tsv(列: key, ja, en)を持つ。ほかのパッケージは
    /// <see cref="AddTable"/> で自分の表を足す。引けない時は 選んだ言語 → ja → キー の順に戻る。
    /// [Tooltip] は使わない。ラベルと説明はすべて <see cref="Tr"/> を通して描く。
    /// </summary>
    public static class GunnerL10n
    {
        public const string Japanese = "ja";
        public const string English = "en";
        const string PrefKey = "Mecaota.Gunner.Language"; // ""、"ja"、"en" のいずれか。空 = エディタに合わせる。
        const string GunnerTableGuid = "a8c460bbc0ac4ae09789c3dfcacb83cd"; // Editor/strings.tsv

        static readonly List<string> tablePaths = new List<string>();
        static Dictionary<string, string[]> table; // キー → { ja, en }

        /// <summary>今の言語: 保存した選択、無ければエディタの言語(日本語 → ja、ほか → en)。</summary>
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

        /// <summary>保存した選択そのまま: ""(エディタに合わせる)、"ja"、"en"。</summary>
        public static string SavedChoice => EditorPrefs.GetString(PrefKey, "");

        /// <summary>インスペクタと点検ウィンドウの下に出す言語の選択(エディタに合わせる / 日本語 / English)。</summary>
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

        /// <summary>ほかのパッケージの strings.tsv(アセットのパス)を登録する。後の表が前のキーを上書きする。</summary>
        public static void AddTable(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || tablePaths.Contains(assetPath)) return;
            tablePaths.Add(assetPath);
            table = null;
        }

        /// <summary>今の言語でのキーの文。</summary>
        public static string Tr(string key) => Tr(key, Language);

        public static string Tr(string key, string language)
        {
            if (TryGet(key, language, out string text)) return text;
            if (language != Japanese && TryGet(key, Japanese, out text)) return text;
            return key;
        }

        /// <summary>指定の言語でキーに文があれば true(戻りなし)。点検用。</summary>
        public static bool Has(string key, string language) => TryGet(key, language, out _);

        /// <summary>説明(キー + ".tip")付きのラベル。キーに説明が無ければ説明は空。</summary>
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
            string fullPath = Path.GetFullPath(assetPath); // "Packages/<id>/..." を実際のフォルダに解決する
            if (!File.Exists(fullPath)) return;
            string[] lines = File.ReadAllLines(fullPath, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++) // 0行目は見出し
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
