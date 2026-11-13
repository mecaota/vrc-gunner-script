using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>読み込みの1行。error = 赤(何も書かない)、偽 = 黄(丸めて、または飛ばして書く)。key は文字列表のキー、arg は "{0}" に入る。</summary>
    public sealed class GunnerValueIssue
    {
        public bool error;
        public string key;
        public string arg;
    }

    /// <summary>
    /// 読み込みの結果。values は型つきの値(int、float、bool、string、ベクトル・色は float[]、int[]、float[]、bool[])で、
    /// JSON にあった欄だけが入る(無い欄は書かない = 今の値のまま)。
    /// </summary>
    public sealed class GunnerValueRead
    {
        public readonly List<GunnerValueIssue> issues = new List<GunnerValueIssue>();
        public readonly List<KeyValuePair<string, object>> values = new List<KeyValuePair<string, object>>();
        public string hash = "";

        public bool HasError
        {
            get
            {
                foreach (var i in issues) if (i.error) return true;
                return false;
            }
        }

        public int Count(bool error)
        {
            int n = 0;
            foreach (var i in issues) if (i.error == error) n++;
            return n;
        }

        public bool TryGet(string name, out object value)
        {
            foreach (var kv in values)
            {
                if (kv.Key != name) continue;
                value = kv.Value;
                return true;
            }
            value = null;
            return false;
        }

        public void Red(string key, string arg = null) => issues.Add(new GunnerValueIssue { error = true, key = key, arg = arg });
        public void Yellow(string key, string arg = null) => issues.Add(new GunnerValueIssue { error = false, key = key, arg = arg });
    }

    /// <summary>
    /// 数値の欄を JSON で出し入れする(Unity に依存しない部分)。ファイルの形:
    /// { "type": "型の名前", "version": 1, "note": "任意のメモ", "values": { "欄の名前": 値, ... } }
    /// 読み込みの決まり: JSON に無い欄は触らない。知らないキーは黄で飛ばす。型の違い・NaN や無限大・赤の範囲の外・
    /// 配列の長さの違いは赤。整数の欄の小数・丸めの範囲の外は丸めて黄。赤が1つでもあれば、使う側は何も書かない。
    /// </summary>
    public static class GunnerValueJson
    {
        public const int FormatVersion = 1;

        public const string KeyParse = "values.json.parse";
        public const string KeyNotObject = "values.json.not_object";
        public const string KeyVersion = "values.json.version";
        public const string KeyWrongType = "values.json.wrong_type";
        public const string KeyNoValues = "values.json.no_values";
        public const string KeyUnknown = "values.json.unknown_key";
        public const string KeyBadType = "values.json.bad_type";
        public const string KeyBadValue = "values.json.bad_value";
        public const string KeyOutOfRange = "values.json.out_of_range";
        public const string KeyClamped = "values.json.clamped";
        public const string KeyRounded = "values.json.rounded";
        public const string KeyLength = "values.json.length";

        static readonly string[] TopKeys = { "type", "version", "note", "values" };

        // ---------------- 読む ----------------

        /// <summary>
        /// ファイル1つを読む。currentLength(欄の名前 → 今の配列の長さ。分からなければ -1)は、長さの決まっていない配列の点検に使う。
        /// null なら長さを比べない。
        /// </summary>
        public static GunnerValueRead ReadFile(string text, GunnerValueSchema schema, Func<string, int> currentLength)
        {
            var r = new GunnerValueRead();
            if (!GunnerJson.TryParse(text, out object root, out string error))
            {
                r.Red(KeyParse, error);
                return r;
            }
            var top = root as GunnerJsonObject;
            if (top == null)
            {
                r.Red(KeyNotObject);
                return r;
            }
            foreach (var key in top.Keys) if (Array.IndexOf(TopKeys, key) < 0) r.Yellow(KeyUnknown, key);

            top.TryGet("type", out object type);
            if (!(type is string typeName) || typeName != schema.typeName)
            {
                r.Red(KeyWrongType, (type is string s ? s : "-") + " / " + schema.typeName);
                return r;
            }
            if (!top.TryGet("version", out object ver) || !IsNumber(ver) || Convert.ToDouble(ver, CultureInfo.InvariantCulture) != FormatVersion)
            {
                r.Red(KeyVersion, ver == null ? "-" : Convert.ToString(ver, CultureInfo.InvariantCulture));
                return r;
            }
            if (!top.TryGet("values", out object values) || !(values is GunnerJsonObject))
            {
                r.Red(KeyNoValues);
                return r;
            }
            return ReadValues(values, schema, "", currentLength, r);
        }

        /// <summary>
        /// 値のオブジェクト1つを読む(ほかの JSON の節に入っている時もこれを使う)。path は知らせる時の場所("aim.shooter" など、"" = 欄の名前だけ)。
        /// into を渡すと、その結果に足す。
        /// </summary>
        public static GunnerValueRead ReadValues(object node, GunnerValueSchema schema, string path, Func<string, int> currentLength, GunnerValueRead into = null)
        {
            var r = into ?? new GunnerValueRead();
            var o = node as GunnerJsonObject;
            if (o == null)
            {
                r.Red(KeyBadType, string.IsNullOrEmpty(path) ? "values" : path);
                return r;
            }
            foreach (var key in o.Keys) if (schema.Find(key) == null) r.Yellow(KeyUnknown, Join(path, key));
            foreach (var f in schema.Fields)
            {
                if (!o.TryGet(f.name, out object v)) continue;
                string at = Join(path, f.name);
                if (ReadField(r, f, v, at, currentLength, out object typed)) r.values.Add(new KeyValuePair<string, object>(f.name, typed));
            }
            return r;
        }

        static bool ReadField(GunnerValueRead r, GunnerValueField f, object v, string at, Func<string, int> currentLength, out object typed)
        {
            typed = null;
            switch (f.kind)
            {
                case GunnerValueKind.Int:
                    if (!Scalar(r, f, v, at, out double n)) return false;
                    typed = (int)n;
                    return true;
                case GunnerValueKind.Float:
                    if (!Scalar(r, f, v, at, out double x)) return false;
                    typed = (float)x;
                    return true;
                case GunnerValueKind.Bool:
                    if (!(v is bool b)) { r.Red(KeyBadType, at); return false; }
                    typed = b;
                    return true;
                case GunnerValueKind.String:
                    if (!(v is string s)) { r.Red(KeyBadType, at); return false; }
                    typed = s;
                    return true;
                case GunnerValueKind.Vector2:
                case GunnerValueKind.Vector3:
                case GunnerValueKind.Vector4:
                case GunnerValueKind.Color:
                    return Vector(r, f, v, at, out typed);
            }

            // 配列
            var list = v as List<object>;
            if (list == null) { r.Red(KeyBadType, at); return false; }
            if (f.length >= 0)
            {
                if (list.Count != f.length) { r.Red(KeyLength, at + ": " + list.Count + " / " + f.length); return false; }
            }
            else if (f.keepLength)
            {
                int now = currentLength != null ? currentLength(f.name) : -1;
                if (now >= 0 && list.Count != now) { r.Red(KeyLength, at + ": " + list.Count + " / " + now); return false; }
            }
            else if (list.Count > f.maxLength)
            {
                r.Red(KeyLength, at + ": " + list.Count + " > " + f.maxLength);
                return false;
            }
            bool ok = true;
            switch (f.kind)
            {
                case GunnerValueKind.IntArray:
                {
                    var a = new int[list.Count];
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (Scalar(r, f, list[k], at + "[" + k + "]", out double e)) a[k] = (int)e;
                        else ok = false;
                    }
                    typed = a;
                    break;
                }
                case GunnerValueKind.FloatArray:
                {
                    var a = new float[list.Count];
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (Scalar(r, f, list[k], at + "[" + k + "]", out double e)) a[k] = (float)e;
                        else ok = false;
                    }
                    typed = a;
                    break;
                }
                default:
                {
                    var a = new bool[list.Count];
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (list[k] is bool e) a[k] = e;
                        else { r.Red(KeyBadType, at + "[" + k + "]"); ok = false; }
                    }
                    typed = a;
                    break;
                }
            }
            return ok;
        }

        // 数1つ(整数・小数の欄、配列の要素)。範囲と丸めを当てる。赤なら false
        static bool Scalar(GunnerValueRead r, GunnerValueField f, object v, string at, out double d)
        {
            d = 0;
            if (!IsNumber(v)) { r.Red(KeyBadType, at); return false; }
            d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) { r.Red(KeyBadValue, at); return false; }
            if (f.IsInt)
            {
                if (d < int.MinValue || d > int.MaxValue) { r.Red(KeyBadValue, at); return false; }
                double whole = Math.Round(d, MidpointRounding.AwayFromZero);
                if (whole != d)
                {
                    r.Yellow(KeyRounded, at + " = " + F(whole));
                    d = whole;
                }
            }
            else if (Math.Abs(d) > float.MaxValue) { r.Red(KeyBadValue, at); return false; }
            if (d < f.hardMin || d > f.hardMax)
            {
                r.Red(KeyOutOfRange, at + " = " + F(d) + " (" + Bound(f.hardMin) + " … " + Bound(f.hardMax) + ")");
                return false;
            }
            if (d < f.softMin || d > f.softMax)
            {
                d = d < f.softMin ? f.softMin : f.softMax;
                if (f.IsInt) d = Math.Round(d, MidpointRounding.AwayFromZero);
                r.Yellow(KeyClamped, at + " = " + F(d));
            }
            return true;
        }

        static bool Vector(GunnerValueRead r, GunnerValueField f, object v, string at, out object typed)
        {
            typed = null;
            int n = f.Components;
            var list = v as List<object>;
            if (list == null) { r.Red(KeyBadType, at); return false; }
            if (list.Count != n) { r.Red(KeyLength, at + ": " + list.Count + " / " + n); return false; }
            var a = new float[n];
            for (int k = 0; k < n; k++)
            {
                if (!IsNumber(list[k])) { r.Red(KeyBadType, at + "[" + k + "]"); return false; }
                double d = Convert.ToDouble(list[k], CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > float.MaxValue) { r.Red(KeyBadValue, at + "[" + k + "]"); return false; }
                a[k] = (float)d;
            }
            typed = a;
            return true;
        }

        static bool IsNumber(object v) =>
            v is double || v is float || v is int || v is long || v is short || v is byte || v is decimal || v is uint || v is ulong;

        static string Join(string path, string key) => string.IsNullOrEmpty(path) ? key : path + "." + key;

        static string F(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

        static string Bound(double d) => double.IsInfinity(d) ? "" : F(d);

        // ---------------- 書く ----------------

        /// <summary>型つきの値(Export の結果)を、一覧の順で JSON のオブジェクトにする。一覧に無い名前は書かない。</summary>
        public static GunnerJsonObject ToJson(GunnerValueSchema schema, IList<KeyValuePair<string, object>> values)
        {
            var o = new GunnerJsonObject();
            foreach (var f in schema.Fields)
            {
                object v = null;
                bool found = false;
                foreach (var kv in values)
                {
                    if (kv.Key != f.name) continue;
                    v = kv.Value;
                    found = true;
                    break;
                }
                if (!found) continue;
                o.Set(f.name, Plain(v));
            }
            return o;
        }

        static object Plain(object v)
        {
            switch (v)
            {
                case float[] fa: { var l = new List<object>(); foreach (var e in fa) l.Add(e); return l; }
                case int[] ia: { var l = new List<object>(); foreach (var e in ia) l.Add(e); return l; }
                case bool[] ba: { var l = new List<object>(); foreach (var e in ba) l.Add(e); return l; }
            }
            return v;
        }

        /// <summary>ファイル1つの文字列(最後に改行)。note は空なら書かない。</summary>
        public static string WriteFile(GunnerValueSchema schema, IList<KeyValuePair<string, object>> values, string note = null)
        {
            var top = new GunnerJsonObject();
            top.Set("type", schema.typeName);
            top.Set("version", FormatVersion);
            if (!string.IsNullOrEmpty(note)) top.Set("note", note);
            top.Set("values", ToJson(schema, values));
            return GunnerJson.Write(top) + "\n";
        }
    }
}
