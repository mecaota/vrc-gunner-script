using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>
    /// JSON に出し入れしてよい部品の型と、その欄の一覧(許可リスト)。登録した型だけが、インスペクタの「JSON(数値)」の節、
    /// 右クリックの「Values JSON」、点検の対象になる。ほかのパッケージは [InitializeOnLoad] の static コンストラクタから
    /// <see cref="Register"/> を呼ぶ。型はぴったり一致で引く(派生の型は別に登録する)。
    /// </summary>
    public static class GunnerValueSchemas
    {
        static readonly Dictionary<Type, GunnerValueSchema> map = new Dictionary<Type, GunnerValueSchema>();
        static bool builtIn;

        /// <summary>型の欄から一覧を作って登録する。customize で除外・範囲・長さを足す。同じ型を登録し直すと置き換える。</summary>
        public static GunnerValueSchema Register(Type type, Action<GunnerValueSchema> customize = null, bool includeStrings = false)
        {
            if (type == null) return null;
            var schema = GunnerValueSchema.FromType(type, includeStrings);
            customize?.Invoke(schema);
            map[type] = schema;
            return schema;
        }

        public static GunnerValueSchema For(Type type)
        {
            EnsureBuiltIn();
            return type != null && map.TryGetValue(type, out var s) ? s : null;
        }

        public static bool Has(Type type) => For(type) != null;

        static void EnsureBuiltIn()
        {
            if (builtIn) return;
            builtIn = true;
            // GunnerRig の数値。linkExtraEmitter は linkExtraBones(参照)と番号が対なので出さない。
            // aimMaxAngles は射出口と番号が対なので、今の長さと同じ長さだけ受ける(既定の決まり)
            Register(typeof(GunnerRig), s => s
                .Exclude("linkExtraEmitter")
                .NonNegative()
                .HardRange("maxHits", 1, GunnerRig.SyncSlots));
        }
    }

    /// <summary>
    /// 部品の数値の欄を JSON に書き出す・JSON から読み込む(Unity の側)。書き込みは SerializedObject 経由で、
    /// U# 部品は <see cref="GunnerEditorUtil.Edit"/>(プロキシの確定つき)を使う。UpdateProxy / CopyUdonToProxy は使わない。
    /// 赤が1つでもあれば何も書かない。JSON に無い欄は今の値のまま。読み込んだ JSON の場所(GUID)と中身のハッシュは、
    /// この端末のユーザー設定(UserSettings)に控え、点検が「読み込んだ後に JSON が変わった」を黄で知らせる。
    /// シーンや部品には何も足さない。保存はしない(呼んだ側がシーンを保存する)。
    /// </summary>
    public static class GunnerValueBake
    {
        const string LinkPrefix = "Mecaota.Gunner.ValuesJson.";
        static readonly Dictionary<int, GunnerValueRead> last = new Dictionary<int, GunnerValueRead>();
        static readonly Dictionary<int, string> linkKeys = new Dictionary<int, string>();
        static readonly Dictionary<string, KeyValuePair<Hash128, string>> textHashes = new Dictionary<string, KeyValuePair<Hash128, string>>();

        public static string Hash(string text) => Hash128.Compute(text ?? "").ToString();

        public static GunnerValueRead LastResult(Component c) => c != null && last.TryGetValue(c.GetInstanceID(), out var r) ? r : null;

        // ---------------- 読み書き(SerializedObject) ----------------

        /// <summary>今の値(一覧の欄だけ。見つからない・型が合わない欄は飛ばす)。</summary>
        public static List<KeyValuePair<string, object>> Export(SerializedObject so, GunnerValueSchema schema)
        {
            var list = new List<KeyValuePair<string, object>>();
            if (so == null || schema == null) return list;
            so.Update();
            foreach (var f in schema.Fields)
            {
                var p = so.FindProperty(f.name);
                if (p != null && ReadProp(p, f, out object v)) list.Add(new KeyValuePair<string, object>(f.name, v));
            }
            return list;
        }

        public static List<KeyValuePair<string, object>> Export(Component c)
        {
            var schema = c != null ? GunnerValueSchemas.For(c.GetType()) : null;
            return schema == null ? new List<KeyValuePair<string, object>>() : Export(new SerializedObject(c), schema);
        }

        /// <summary>今の配列の長さを引く関数(読み込みの長さの点検用)。</summary>
        public static Func<string, int> Lengths(SerializedObject so)
        {
            return name =>
            {
                var p = so != null ? so.FindProperty(name) : null;
                return p != null && p.isArray && p.propertyType != SerializedPropertyType.String ? p.arraySize : -1;
            };
        }

        /// <summary>JSON のファイル1つを、この部品の型の一覧で読む(書かない)。</summary>
        public static GunnerValueRead Read(Component c, string text)
        {
            var schema = c != null ? GunnerValueSchemas.For(c.GetType()) : null;
            if (schema == null)
            {
                var r = new GunnerValueRead();
                r.Red("values.json.not_registered", c != null ? c.GetType().Name : "-");
                return r;
            }
            var read = GunnerValueJson.ReadFile(text, schema, Lengths(new SerializedObject(c)));
            read.hash = Hash(text);
            return read;
        }

        /// <summary>読んだ値を SerializedObject に書く(赤のある結果は渡さない)。ApplyModifiedProperties は呼ぶ側。</summary>
        public static void WriteTo(SerializedObject so, GunnerValueSchema schema, GunnerValueRead r)
        {
            if (so == null || schema == null || r == null || r.HasError) return;
            foreach (var kv in r.values)
            {
                var f = schema.Find(kv.Key);
                var p = so.FindProperty(kv.Key);
                if (f == null || p == null || !WriteProp(p, f, kv.Value))
                    Debug.LogWarning("[Gunner] JSON: " + kv.Key + " was not written (no matching field)", so.targetObject);
            }
        }

        /// <summary>読んだ値を部品に書く(Undo つき。U# 部品はプロキシを確定する)。赤があれば何もしない。</summary>
        public static bool Apply(Component c, GunnerValueSchema schema, GunnerValueRead r)
        {
            if (c == null || schema == null || r == null || r.HasError) return false;
            if (c is UdonSharpBehaviour u) GunnerEditorUtil.Edit(u, so => WriteTo(so, schema, r));
            else
            {
                var so = new SerializedObject(c);
                so.Update();
                WriteTo(so, schema, r);
                so.ApplyModifiedProperties();
            }
            return true;
        }

        static bool ReadProp(SerializedProperty p, GunnerValueField f, out object v)
        {
            v = null;
            switch (f.kind)
            {
                case GunnerValueKind.Int:
                    if (p.propertyType != SerializedPropertyType.Integer && p.propertyType != SerializedPropertyType.Enum) return false;
                    v = p.intValue; return true;
                case GunnerValueKind.Float:
                    if (p.propertyType != SerializedPropertyType.Float) return false;
                    v = p.floatValue; return true;
                case GunnerValueKind.Bool:
                    if (p.propertyType != SerializedPropertyType.Boolean) return false;
                    v = p.boolValue; return true;
                case GunnerValueKind.String:
                    if (p.propertyType != SerializedPropertyType.String) return false;
                    v = p.stringValue; return true;
                case GunnerValueKind.Vector2:
                    if (p.propertyType != SerializedPropertyType.Vector2) return false;
                    v = new[] { p.vector2Value.x, p.vector2Value.y }; return true;
                case GunnerValueKind.Vector3:
                    if (p.propertyType != SerializedPropertyType.Vector3) return false;
                    var v3 = p.vector3Value;
                    v = new[] { v3.x, v3.y, v3.z }; return true;
                case GunnerValueKind.Vector4:
                    if (p.propertyType != SerializedPropertyType.Vector4) return false;
                    var v4 = p.vector4Value;
                    v = new[] { v4.x, v4.y, v4.z, v4.w }; return true;
                case GunnerValueKind.Color:
                    if (p.propertyType != SerializedPropertyType.Color) return false;
                    var c = p.colorValue;
                    v = new[] { c.r, c.g, c.b, c.a }; return true;
            }
            if (!p.isArray || p.propertyType == SerializedPropertyType.String) return false;
            int n = p.arraySize;
            switch (f.kind)
            {
                case GunnerValueKind.IntArray:
                {
                    var a = new int[n];
                    for (int k = 0; k < n; k++) a[k] = p.GetArrayElementAtIndex(k).intValue;
                    v = a; return true;
                }
                case GunnerValueKind.FloatArray:
                {
                    var a = new float[n];
                    for (int k = 0; k < n; k++) a[k] = p.GetArrayElementAtIndex(k).floatValue;
                    v = a; return true;
                }
                case GunnerValueKind.BoolArray:
                {
                    var a = new bool[n];
                    for (int k = 0; k < n; k++) a[k] = p.GetArrayElementAtIndex(k).boolValue;
                    v = a; return true;
                }
            }
            return false;
        }

        static bool WriteProp(SerializedProperty p, GunnerValueField f, object v)
        {
            switch (f.kind)
            {
                case GunnerValueKind.Int:
                    if (!(v is int i) || (p.propertyType != SerializedPropertyType.Integer && p.propertyType != SerializedPropertyType.Enum)) return false;
                    p.intValue = i; return true;
                case GunnerValueKind.Float:
                    if (!(v is float x) || p.propertyType != SerializedPropertyType.Float) return false;
                    p.floatValue = x; return true;
                case GunnerValueKind.Bool:
                    if (!(v is bool b) || p.propertyType != SerializedPropertyType.Boolean) return false;
                    p.boolValue = b; return true;
                case GunnerValueKind.String:
                    if (!(v is string s) || p.propertyType != SerializedPropertyType.String) return false;
                    p.stringValue = s; return true;
                case GunnerValueKind.Vector2:
                    if (!(v is float[] a2) || a2.Length != 2 || p.propertyType != SerializedPropertyType.Vector2) return false;
                    p.vector2Value = new Vector2(a2[0], a2[1]); return true;
                case GunnerValueKind.Vector3:
                    if (!(v is float[] a3) || a3.Length != 3 || p.propertyType != SerializedPropertyType.Vector3) return false;
                    p.vector3Value = new Vector3(a3[0], a3[1], a3[2]); return true;
                case GunnerValueKind.Vector4:
                    if (!(v is float[] a4) || a4.Length != 4 || p.propertyType != SerializedPropertyType.Vector4) return false;
                    p.vector4Value = new Vector4(a4[0], a4[1], a4[2], a4[3]); return true;
                case GunnerValueKind.Color:
                    if (!(v is float[] ac) || ac.Length != 4 || p.propertyType != SerializedPropertyType.Color) return false;
                    p.colorValue = new Color(ac[0], ac[1], ac[2], ac[3]); return true;
            }
            if (!p.isArray || p.propertyType == SerializedPropertyType.String) return false;
            switch (f.kind)
            {
                case GunnerValueKind.IntArray:
                    if (!(v is int[] ia)) return false;
                    p.arraySize = ia.Length;
                    for (int k = 0; k < ia.Length; k++) p.GetArrayElementAtIndex(k).intValue = ia[k];
                    return true;
                case GunnerValueKind.FloatArray:
                    if (!(v is float[] fa)) return false;
                    p.arraySize = fa.Length;
                    for (int k = 0; k < fa.Length; k++) p.GetArrayElementAtIndex(k).floatValue = fa[k];
                    return true;
                case GunnerValueKind.BoolArray:
                    if (!(v is bool[] ba)) return false;
                    p.arraySize = ba.Length;
                    for (int k = 0; k < ba.Length; k++) p.GetArrayElementAtIndex(k).boolValue = ba[k];
                    return true;
            }
            return false;
        }

        // ---------------- JSON のファイルとの結び付き(この端末のユーザー設定に控える) ----------------

        static string LinkKey(Component c)
        {
            if (c == null) return null;
            int id = c.GetInstanceID();
            if (linkKeys.TryGetValue(id, out string key)) return key;
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(c);
            key = gid.identifierType == 0 ? null : LinkPrefix + gid;
            // 保存前のシーンでは識別子が決まらないので覚えない(保存後に引き直す)
            if (key != null && c.gameObject.scene.IsValid() && !string.IsNullOrEmpty(c.gameObject.scene.path)) linkKeys[id] = key;
            return key;
        }

        /// <summary>結び付けた JSON の GUID と、読み込んだ時のハッシュ(まだ読み込んでいなければ "")。無ければ false。</summary>
        public static bool GetLink(Component c, out string guid, out string hash)
        {
            guid = hash = "";
            string key = LinkKey(c);
            if (key == null) return false;
            string v = EditorUserSettings.GetConfigValue(key);
            if (string.IsNullOrEmpty(v)) return false;
            int bar = v.IndexOf('|');
            guid = bar < 0 ? v : v.Substring(0, bar);
            hash = bar < 0 ? "" : v.Substring(bar + 1);
            return guid.Length > 0;
        }

        static void SetLink(Component c, string guid, string hash)
        {
            string key = LinkKey(c);
            if (key == null) return;
            EditorUserSettings.SetConfigValue(key, string.IsNullOrEmpty(guid) ? "" : guid + "|" + (hash ?? ""));
        }

        public static TextAsset JsonOf(Component c)
        {
            if (!GetLink(c, out string guid, out _)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        /// <summary>JSON を結び付ける(まだ読み込まない。点検が「読み込んでいない」と知らせる)。null で外す。</summary>
        public static void SetJson(Component c, TextAsset json)
        {
            if (c == null) return;
            if (json == null) { SetLink(c, "", ""); return; }
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(json, out string guid, out long _);
            GetLink(c, out string oldGuid, out string oldHash);
            SetLink(c, guid, guid == oldGuid ? oldHash : "");
        }

        public static void Unlink(Component c) => SetLink(c, "", "");

        /// <summary>JSON の中身のハッシュ(ファイルが変わった時だけ計算し直す)。</summary>
        public static string TextHash(TextAsset json)
        {
            if (json == null) return "";
            string path = AssetDatabase.GetAssetPath(json);
            var dep = string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
            if (!string.IsNullOrEmpty(path) && textHashes.TryGetValue(path, out var cached) && cached.Key == dep) return cached.Value;
            string h = Hash(json.text);
            if (!string.IsNullOrEmpty(path))
            {
                if (textHashes.Count > 256) textHashes.Clear();
                textHashes[path] = new KeyValuePair<Hash128, string>(dep, h);
            }
            return h;
        }

        /// <summary>0 = 結び付けなし、1 = 最新、2 = まだ読み込んでいない、3 = 読み込んだ後に JSON が変わった、4 = JSON が見つからない。</summary>
        public static int LinkState(Component c)
        {
            if (!GetLink(c, out string guid, out string hash)) return 0;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var json = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (json == null) return 4;
            if (string.IsNullOrEmpty(hash)) return 2;
            return TextHash(json) == hash ? 1 : 3;
        }

        // ---------------- 読み込む・書き出す(操作) ----------------

        /// <summary>JSON を読んで、赤が無ければ書き、結び付けて控える。結果は LastResult にも残る。</summary>
        public static GunnerValueRead Import(Component c, TextAsset json)
        {
            GunnerValueRead r;
            if (c == null || json == null)
            {
                r = new GunnerValueRead();
                r.Red("values.json.no_file");
                if (c != null) last[c.GetInstanceID()] = r;
                return r;
            }
            r = Read(c, json.text);
            last[c.GetInstanceID()] = r;
            if (r.HasError) return r;
            Apply(c, GunnerValueSchemas.For(c.GetType()), r);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(json, out string guid, out long _);
            SetLink(c, guid, r.hash);
            return r;
        }

        /// <summary>今の値を JSON に書き出す(BOM なしの UTF-8)。保存先は Assets の下でユーザーが選ぶ。書いたファイルを結び付ける。</summary>
        public static bool ExportToFile(Component c)
        {
            var schema = c != null ? GunnerValueSchemas.For(c.GetType()) : null;
            if (schema == null) return false;
            string path = EditorUtility.SaveFilePanelInProject(GunnerL10n.Tr("values.export_title"), c.gameObject.name + "-" + schema.typeName, "json", "");
            if (string.IsNullOrEmpty(path)) return false;
            string text = GunnerValueJson.WriteFile(schema, Export(c));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (json == null) return false;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(json, out string guid, out long _);
            SetLink(c, guid, Hash(json.text));
            return true;
        }

        /// <summary>結果をコンソールへ(赤はエラー、黄は警告)。</summary>
        public static void Log(Component c, GunnerValueRead r)
        {
            if (r == null) return;
            foreach (var i in r.issues)
            {
                string text = "[Gunner] JSON: " + IssueText(i);
                if (i.error) Debug.LogError(text, c);
                else Debug.LogWarning(text, c);
            }
        }

        public static string IssueText(GunnerValueIssue i)
        {
            string text = GunnerL10n.Tr(i.key);
            return text.Contains("{0}") ? text.Replace("{0}", i.arg ?? "") : (i.arg != null ? text + " (" + i.arg + ")" : text);
        }

        // ---------------- 右クリックのメニュー ----------------

        [MenuItem("CONTEXT/MonoBehaviour/Values JSON/Import...")]
        static void MenuImport(MenuCommand cmd)
        {
            var c = cmd.context as Component;
            if (c == null) return;
            string abs = EditorUtility.OpenFilePanel(GunnerL10n.Tr("values.import_title"), Application.dataPath, "json");
            if (string.IsNullOrEmpty(abs)) return;
            string rel = ToProjectPath(abs);
            var json = rel != null ? AssetDatabase.LoadAssetAtPath<TextAsset>(rel) : null;
            if (json == null)
            {
                EditorUtility.DisplayDialog("Gunner", GunnerL10n.Tr("values.outside_project"), "OK");
                return;
            }
            var r = Import(c, json);
            Log(c, r);
            EditorUtility.DisplayDialog("Gunner", Summary(r), "OK");
        }

        [MenuItem("CONTEXT/MonoBehaviour/Values JSON/Import...", true)]
        static bool MenuImportOk(MenuCommand cmd) => cmd.context != null && GunnerValueSchemas.Has(cmd.context.GetType());

        [MenuItem("CONTEXT/MonoBehaviour/Values JSON/Export...")]
        static void MenuExport(MenuCommand cmd) => ExportToFile(cmd.context as Component);

        [MenuItem("CONTEXT/MonoBehaviour/Values JSON/Export...", true)]
        static bool MenuExportOk(MenuCommand cmd) => cmd.context != null && GunnerValueSchemas.Has(cmd.context.GetType());

        public static string Summary(GunnerValueRead r)
        {
            if (r == null) return "";
            int red = r.Count(true), yellow = r.Count(false);
            return red > 0 ? GunnerL10n.Tr("values.result.failed").Replace("{0}", red.ToString())
                           : GunnerL10n.Tr("values.result.ok").Replace("{0}", r.values.Count.ToString()).Replace("{1}", yellow.ToString());
        }

        static string ToProjectPath(string abs)
        {
            string a = abs.Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/');
            if (a.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase)) return "Assets" + a.Substring(data.Length);
            return null;
        }
    }

    /// <summary>インスペクタの「JSON(数値)」の節。登録した型の部品で、1つだけ選んでいる時に出す(既定は閉じている)。</summary>
    public static class GunnerValueGUI
    {
        const string FoldState = "Mecaota.Gunner.Fold.values.section";

        public static void Draw(Component c)
        {
            if (c == null || !GunnerValueSchemas.Has(c.GetType())) return;
            EditorGUILayout.Space();
            bool open = SessionState.GetBool(FoldState, false);
            bool next = EditorGUILayout.Foldout(open, GunnerL10n.Tr("values.section"), true, EditorStyles.foldoutHeader);
            if (next != open) SessionState.SetBool(FoldState, next);
            if (!next) return;

            EditorGUILayout.LabelField(GunnerL10n.Tr("values.help"), EditorStyles.wordWrappedMiniLabel);
            var json = GunnerValueBake.JsonOf(c);
            var picked = (TextAsset)EditorGUILayout.ObjectField(GunnerL10n.Content("values.json"), json, typeof(TextAsset), false);
            if (picked != json)
            {
                GunnerValueBake.SetJson(c, picked);
                json = picked;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(json == null))
                {
                    if (GUILayout.Button(GunnerL10n.Tr("values.import")))
                    {
                        var r = GunnerValueBake.Import(c, json);
                        GunnerValueBake.Log(c, r);
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button(GunnerL10n.Tr("values.export")))
                {
                    GunnerValueBake.ExportToFile(c);
                    GUIUtility.ExitGUI();
                }
            }

            var last = GunnerValueBake.LastResult(c);
            if (last == null) return;
            EditorGUILayout.HelpBox(GunnerValueBake.Summary(last), last.HasError ? MessageType.Error : MessageType.Info);
            foreach (var i in last.issues)
                EditorGUILayout.HelpBox(GunnerValueBake.IssueText(i), i.error ? MessageType.Error : MessageType.Warning);
        }
    }

    /// <summary>JSON を結び付けた部品の点検: 見つからない、まだ読み込んでいない、読み込んだ後に変わった(どれも黄)。</summary>
    public sealed class GunnerValueChecks : IGunnerCheckProvider
    {
        public void Collect(Component component, List<GunnerCheck> into)
        {
            if (component == null || !GunnerValueSchemas.Has(component.GetType())) return;
            int state = GunnerValueBake.LinkState(component);
            if (state <= 1) return;
            var json = GunnerValueBake.JsonOf(component);
            string name = json != null ? json.name : "-";
            if (state == 4)
                into.Add(new GunnerCheck(GunnerCheckLevel.Warning, "values.check.missing", component, () => GunnerValueBake.Unlink(component)));
            else
                into.Add(new GunnerCheck(GunnerCheckLevel.Warning, state == 2 ? "values.check.not_read" : "values.check.stale", component,
                    () => GunnerValueBake.Log(component, GunnerValueBake.Import(component, json)), name));
        }
    }

    [InitializeOnLoad]
    static class GunnerValueChecksRegister
    {
        static GunnerValueChecksRegister() => GunnerChecks.Register(new GunnerValueChecks());
    }
}
