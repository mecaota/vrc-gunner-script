using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mecaota.Gunner.EditorTools
{
    /// <summary>JSON のオブジェクト。キーの順番を保つ(書き出しの並びと、知らないキーを知らせる順番のため)。</summary>
    public sealed class GunnerJsonObject
    {
        readonly Dictionary<string, object> map = new Dictionary<string, object>();
        readonly List<string> keys = new List<string>();

        public IReadOnlyList<string> Keys => keys;
        public int Count => keys.Count;
        public bool Has(string key) => key != null && map.ContainsKey(key);
        public bool TryGet(string key, out object value) => map.TryGetValue(key ?? "", out value);

        public void Set(string key, object value)
        {
            if (key == null) return;
            if (!map.ContainsKey(key)) keys.Add(key);
            map[key] = value;
        }
    }

    /// <summary>
    /// エディタ用の小さな JSON の読み書き(Unity に依存しない)。読んだ値の型: オブジェクト = <see cref="GunnerJsonObject"/>、
    /// 配列 = List&lt;object&gt;、数 = double、真偽 = bool、文字列 = string、null = null。
    /// 同じキーが2回ある、深すぎる、末尾に余計な文字がある JSON は読めないものとして扱う(打ち間違いを見逃さないため)。
    /// 実行時(Udon)では使わない。
    /// </summary>
    public static class GunnerJson
    {
        public const int MaxDepth = 64;

        // ---------------- 読む ----------------

        /// <summary>読めたら true。読めなければ error に「行:桁 理由」。先頭の BOM は飛ばす。</summary>
        public static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;
            var p = new Parser(text ?? "");
            try
            {
                p.SkipBom();
                p.SkipSpace();
                value = p.ReadValue(0);
                p.SkipSpace();
                if (!p.AtEnd) throw p.Fail("extra text after the value");
                return true;
            }
            catch (FormatException e)
            {
                value = null;
                error = e.Message;
                return false;
            }
        }

        sealed class Parser
        {
            readonly string s;
            int i;

            public Parser(string text) { s = text; }

            public bool AtEnd => i >= s.Length;

            public void SkipBom()
            {
                if (i < s.Length && s[i] == '﻿') i++;
            }

            public FormatException Fail(string why)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new FormatException(line + ":" + col + " " + why);
            }

            public void SkipSpace()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Fail("too deep");
                if (AtEnd) throw Fail("unexpected end");
                char c = s[i];
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Fail("unexpected character '" + c + "'");
            }

            bool Word(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
                int end = i + w.Length;
                if (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_')) return false;
                i = end;
                return true;
            }

            GunnerJsonObject ReadObject(int depth)
            {
                var o = new GunnerJsonObject();
                i++; // {
                SkipSpace();
                if (!AtEnd && s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || s[i] != '"') throw Fail("expected a key");
                    string key = ReadString();
                    if (o.Has(key)) throw Fail("duplicate key \"" + key + "\"");
                    SkipSpace();
                    if (AtEnd || s[i] != ':') throw Fail("expected ':'");
                    i++;
                    SkipSpace();
                    o.Set(key, ReadValue(depth + 1));
                    SkipSpace();
                    if (AtEnd) throw Fail("unexpected end");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw Fail("expected ',' or '}'");
                }
            }

            List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                i++; // [
                SkipSpace();
                if (!AtEnd && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipSpace();
                    list.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (AtEnd) throw Fail("unexpected end");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Fail("expected ',' or ']'");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                i++; // "
                while (true)
                {
                    if (AtEnd) throw Fail("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw Fail("control character in a string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Fail("unterminated string");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Fail("bad \\u escape");
                            int code;
                            if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Fail("bad \\u escape");
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: throw Fail("bad escape '\\" + e + "'");
                    }
                }
            }

            // 0〜9 だけ(char.IsDigit はほかの文字の数字も通すため使わない)
            static bool Digit(char c) => c >= '0' && c <= '9';

            object ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                if (AtEnd || !Digit(s[i])) throw Fail("bad number");
                if (s[i] == '0') i++;
                else while (!AtEnd && Digit(s[i])) i++;
                if (!AtEnd && s[i] == '.')
                {
                    i++;
                    if (AtEnd || !Digit(s[i])) throw Fail("bad number");
                    while (!AtEnd && Digit(s[i])) i++;
                }
                if (!AtEnd && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (!AtEnd && (s[i] == '+' || s[i] == '-')) i++;
                    if (AtEnd || !Digit(s[i])) throw Fail("bad number");
                    while (!AtEnd && Digit(s[i])) i++;
                }
                string t = s.Substring(start, i - start);
                double v;
                // 桁あふれ(1e999 など)は、.NET の版によって例外か無限大になる。どちらも無限大として返し、使う側が赤にする
                try { v = double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture); }
                catch (OverflowException) { v = t[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity; }
                return v;
            }
        }

        // ---------------- 書く ----------------

        /// <summary>
        /// 値を JSON の文字列にする(2文字の字下げ)。数だけ・真偽だけの配列は1行に並べる。
        /// 2行目以降の行頭に baseIndent を付ける(ほかの JSON の途中に差し込む時に使う)。
        /// float は float の短い形(0.15 など)で書く。NaN と無限大は null(読み直すと赤になる)。
        /// </summary>
        public static string Write(object value, string baseIndent = "")
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, baseIndent ?? "");
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, string indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string str: Quote(sb, str); return;
                case float f: sb.Append(Number(f)); return;
                case double d: sb.Append(Number(d)); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case GunnerJsonObject o: WriteObject(sb, o, indent); return;
                case IList list: WriteList(sb, list, indent); return;
            }
            Quote(sb, v.ToString());
        }

        static void WriteObject(StringBuilder sb, GunnerJsonObject o, string indent)
        {
            if (o.Count == 0) { sb.Append("{}"); return; }
            string inner = indent + "  ";
            sb.Append("{\n");
            for (int k = 0; k < o.Count; k++)
            {
                string key = o.Keys[k];
                o.TryGet(key, out object v);
                sb.Append(inner);
                Quote(sb, key);
                sb.Append(": ");
                WriteValue(sb, v, inner);
                sb.Append(k < o.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(indent).Append('}');
        }

        static void WriteList(StringBuilder sb, IList list, string indent)
        {
            if (list.Count == 0) { sb.Append("[]"); return; }
            bool flat = true;
            foreach (var e in list) if (e is GunnerJsonObject || (e is IList && !(e is string))) { flat = false; break; }
            if (flat)
            {
                sb.Append('[');
                for (int k = 0; k < list.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    WriteValue(sb, list[k], indent);
                }
                sb.Append(']');
                return;
            }
            string inner = indent + "  ";
            sb.Append("[\n");
            for (int k = 0; k < list.Count; k++)
            {
                sb.Append(inner);
                WriteValue(sb, list[k], inner);
                sb.Append(k < list.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(indent).Append(']');
        }

        public static string Number(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return "null";
            return f.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        public static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
