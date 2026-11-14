// verify-uses: Editor/GunnerJson.cs
// verify-uses: Editor/GunnerValueSchema.cs
// verify-uses: Editor/GunnerValueJson.cs
// verify-uses: Tools~/verify/pure/ValueJsonStubs.cs
// 数値の JSON の道具(Editor の GunnerJson・GunnerValueSchema・GunnerValueJson。Unity に依存しない部分)を確かめる。
// 実行: Tools~/verify/build.sh(run-pure.ps1 が上の verify-uses のファイルと一緒にコンパイルして Run を呼ぶ)。
// 見るもの: 読み書きの往復、読めない JSON、欄の選び方(参照・隠し・同期・private・文字列を除く)、
// 部分の読み込み(無い欄は書かない)、赤(型・長さ・範囲・無限大)と黄(知らないキー・丸め)。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Mecaota.Gunner.EditorTools;
using ValueJsonFakes;

public static class ValueJsonTest
{
    static int fails;
    static StringBuilder sb;

    static void Check(bool ok, string what)
    {
        if (!ok) fails++;
        sb.AppendLine((ok ? "OK  " : "NG  ") + what);
    }

    static GunnerValueRead ReadFile(string text, GunnerValueSchema schema, Func<string, int> lengths = null) =>
        GunnerValueJson.ReadFile(text, schema, lengths);

    static bool HasIssue(GunnerValueRead r, string key, bool error)
    {
        foreach (var i in r.issues) if (i.key == key && i.error == error) return true;
        return false;
    }

    static string Wrap(string type, string values) => "{ \"type\": \"" + type + "\", \"version\": 1, \"values\": " + values + " }";

    public static string Run()
    {
        fails = 0;
        sb = new StringBuilder();

        // ---------------- 1. JSON の読み書き ----------------
        {
            string text = "﻿{ \"a\": 1, \"b\": [1.5, -2e3, true, null, \"x\\\"y\\u3042\"], \"c\": { \"d\": {} } }";
            bool ok = GunnerJson.TryParse(text, out object v, out string err);
            var o = v as GunnerJsonObject;
            Check(ok && o != null && o.Count == 3, "1a 読める(BOM・入れ子・エスケープ): " + err);
            o.TryGet("b", out object b);
            var list = b as List<object>;
            Check(list != null && (double)list[0] == 1.5 && (double)list[1] == -2000 && (bool)list[2] && list[3] == null && (string)list[4] == "x\"yあ", "1b 値の型(double・bool・null・string)");
            string back = GunnerJson.Write(v);
            Check(GunnerJson.TryParse(back, out object v2, out _) && GunnerJson.Write(v2) == back, "1c 書いて読み直すと同じ");

            string[] bad = { "{ \"a\": 1, }", "{ \"a\": 1, \"a\": 2 }", "{ \"a\": 1 } x", "{ \"a\": \"abc }", "{ \"a\": 01 }", "[1, 2", "{ a: 1 }", "{ \"a\": tru }", "" };
            int rejected = 0;
            foreach (var t in bad) if (!GunnerJson.TryParse(t, out _, out string e) && !string.IsNullOrEmpty(e) && e.Contains(":")) rejected++;
            Check(rejected == bad.Length, "1d 読めない JSON を全部断る(行:桁つき) " + rejected + "/" + bad.Length);

            var fo = new GunnerJsonObject();
            fo.Set("f", 0.15f);
            fo.Set("g", new List<object> { 6f, 4f, 6.5f });
            fo.Set("n", float.NaN);
            Check(GunnerJson.Write(fo) == "{\n  \"f\": 0.15,\n  \"g\": [6, 4, 6.5],\n  \"n\": null\n}", "1e float は短い形、数の配列は1行、NaN は null");
            GunnerJson.TryParse("{ \"big\": 1e999 }", out object big, out _);
            ((GunnerJsonObject)big).TryGet("big", out object bv);
            Check(bv is double bd && double.IsPositiveInfinity(bd), "1f 桁あふれは無限大として読む(使う側が赤にする)");
        }

        // ---------------- 2. 欄の選び方 ----------------
        var schema = GunnerValueSchema.FromType(typeof(FakeShooter));
        {
            var names = new List<string>();
            foreach (var f in schema.Fields) names.Add(f.name);
            string got = string.Join(",", names);
            Check(got == "aimTime,magazine,maxCombo,watchRange,watchOnly,serializedPrivate,perEmitter,center,tint", "2a 入る欄と順番: " + got);
            Check(schema.Find("maxCombo").softMax == 4 && schema.Find("maxCombo").softMin == 0, "2b [Range] は丸めの範囲");
            var withStrings = GunnerValueSchema.FromType(typeof(FakeShooter), true);
            Check(withStrings.Find("phaseEvent") != null && schema.Find("phaseEvent") == null, "2c 文字列は includeStrings の時だけ");
            Check(schema.Find("center").kind == GunnerValueKind.Vector3 && schema.Find("tint").kind == GunnerValueKind.Color && schema.Find("perEmitter").kind == GunnerValueKind.FloatArray, "2d ベクトル・色・配列の型");
        }
        schema.NonNegative();
        Check(schema.Find("aimTime").hardMin == 0 && double.IsNegativeInfinity(schema.Find("center").hardMin), "2e NonNegative は数だけ(ベクトル・色は除く)");

        // ---------------- 3. 書き出し → 読み込みの往復 ----------------
        {
            var fake = new FakeShooter();
            var values = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("aimTime", fake.aimTime),
                new KeyValuePair<string, object>("magazine", fake.magazine),
                new KeyValuePair<string, object>("maxCombo", fake.maxCombo),
                new KeyValuePair<string, object>("watchRange", fake.watchRange),
                new KeyValuePair<string, object>("watchOnly", fake.watchOnly),
                new KeyValuePair<string, object>("serializedPrivate", 2f),
                new KeyValuePair<string, object>("perEmitter", fake.perEmitter),
                new KeyValuePair<string, object>("center", new[] { 0f, 1.5f, -2f }),
                new KeyValuePair<string, object>("tint", new[] { fake.tint.r, fake.tint.g, fake.tint.b, fake.tint.a }),
            };
            string file = GunnerValueJson.WriteFile(schema, values, "メモ");
            var r = ReadFile(file, schema, name => name == "perEmitter" ? 3 : -1);
            Check(r.issues.Count == 0 && r.values.Count == values.Count, "3a 書き出した JSON を読むと赤・黄 0、全部の欄 (" + r.issues.Count + ")");
            bool same = true;
            foreach (var kv in values)
            {
                r.TryGet(kv.Key, out object got);
                same &= Same(kv.Value, got);
            }
            Check(same, "3b 読んだ値が書き出した値と同じ(float・int・bool・配列・ベクトル・色)");
            Check(GunnerValueJson.WriteFile(schema, r.values, "メモ") == file, "3c 読んだ値をもう一度書くと同じ文字列");
        }

        // ---------------- 4. 部分の読み込み・知らないキー ----------------
        {
            var r = ReadFile(Wrap("FakeShooter", "{ \"aimTime\": 0.8, \"typo\": 1, \"rig\": 3 }"), schema);
            Check(!r.HasError && r.values.Count == 1 && r.TryGet("aimTime", out object a) && (float)a == 0.8f, "4a 無い欄は書かない(aimTime だけ)");
            Check(r.Count(false) == 2 && HasIssue(r, GunnerValueJson.KeyUnknown, false), "4b 知らないキー・参照の欄は黄で飛ばす");
            var empty = ReadFile(Wrap("FakeShooter", "{}"), schema);
            Check(!empty.HasError && empty.values.Count == 0 && empty.issues.Count == 0, "4c 空の values = 何も書かない(今と同じ)");
        }

        // ---------------- 5. 赤 ----------------
        {
            var cases = new (string json, string key, string what)[]
            {
                ("{ \"type\": \"Other\", \"version\": 1, \"values\": {} }", GunnerValueJson.KeyWrongType, "別の型"),
                ("{ \"version\": 1, \"values\": {} }", GunnerValueJson.KeyWrongType, "type なし"),
                ("{ \"type\": \"FakeShooter\", \"version\": 2, \"values\": {} }", GunnerValueJson.KeyVersion, "version 2"),
                ("{ \"type\": \"FakeShooter\", \"version\": 1 }", GunnerValueJson.KeyNoValues, "values なし"),
                ("[1]", GunnerValueJson.KeyNotObject, "最上位が配列"),
                ("{ \"type\": ", GunnerValueJson.KeyParse, "壊れた JSON"),
                (Wrap("FakeShooter", "{ \"aimTime\": \"0.8\" }"), GunnerValueJson.KeyBadType, "数に文字列"),
                (Wrap("FakeShooter", "{ \"watchOnly\": 1 }"), GunnerValueJson.KeyBadType, "真偽に数"),
                (Wrap("FakeShooter", "{ \"aimTime\": -1 }"), GunnerValueJson.KeyOutOfRange, "負の秒数"),
                (Wrap("FakeShooter", "{ \"aimTime\": 1e999 }"), GunnerValueJson.KeyBadValue, "無限大"),
                (Wrap("FakeShooter", "{ \"aimTime\": 1e300 }"), GunnerValueJson.KeyBadValue, "float に入らない数"),
                (Wrap("FakeShooter", "{ \"magazine\": 3e10 }"), GunnerValueJson.KeyBadValue, "int に入らない数"),
                (Wrap("FakeShooter", "{ \"perEmitter\": [1, 2] }"), GunnerValueJson.KeyLength, "対の配列の長さ違い"),
                (Wrap("FakeShooter", "{ \"perEmitter\": [1, -2, 3] }"), GunnerValueJson.KeyOutOfRange, "配列の要素が負"),
                (Wrap("FakeShooter", "{ \"center\": [1, 2] }"), GunnerValueJson.KeyLength, "ベクトルの成分の数"),
                (Wrap("FakeShooter", "{ \"tint\": [1, 0, \"a\", 1] }"), GunnerValueJson.KeyBadType, "色に文字列"),
                (Wrap("FakeShooter", "[]"), GunnerValueJson.KeyNoValues, "values が配列"),
            };
            foreach (var c in cases)
            {
                var r = ReadFile(c.json, schema, name => name == "perEmitter" ? 3 : -1);
                Check(r.HasError && HasIssue(r, c.key, true), "5 赤: " + c.what + " → " + c.key);
            }
            // 赤があっても、ほかの欄の黄・値は集まる(使う側が何も書かないことは HasError で決まる)
            var mixed = ReadFile(Wrap("FakeShooter", "{ \"aimTime\": 0.5, \"magazine\": -3 }"), schema);
            Check(mixed.HasError, "5b 1つでも赤なら HasError(使う側は何も書かない)");
        }

        // ---------------- 6. 黄(丸め) ----------------
        {
            var r = ReadFile(Wrap("FakeShooter", "{ \"magazine\": 2.6, \"maxCombo\": 9 }"), schema);
            r.TryGet("magazine", out object m);
            r.TryGet("maxCombo", out object c);
            Check(!r.HasError && (int)m == 3 && HasIssue(r, GunnerValueJson.KeyRounded, false), "6a 整数の欄の小数は四捨五入して黄");
            Check((int)c == 4 && HasIssue(r, GunnerValueJson.KeyClamped, false), "6b 丸めの範囲の外は丸めて黄");
            var lens = ReadFile(Wrap("FakeShooter", "{ \"perEmitter\": [1, 2] }"), schema, null);
            Check(!lens.HasError, "6c 今の長さが分からない時は長さを比べない");
            var fixedLen = schema.Copy().Length("perEmitter", 4);
            Check(ReadFile(Wrap("FakeShooter", "{ \"perEmitter\": [1, 2, 3] }"), fixedLen, n => 3).HasError, "6d 決まった長さ(Length)は今の長さより優先");
            var free = schema.Copy().AnyLength("perEmitter", 5);
            Check(!ReadFile(Wrap("FakeShooter", "{ \"perEmitter\": [1, 2] }"), free, n => 3).HasError
                  && ReadFile(Wrap("FakeShooter", "{ \"perEmitter\": [1, 2, 3, 4, 5, 6] }"), free, n => 3).HasError, "6e 自由な長さは上限まで");
        }

        // ---------------- 7. ほかの JSON の節として読む ----------------
        {
            GunnerJson.TryParse("{ \"aim\": { \"watchRange\": 6, \"shooter\": { \"aimTime\": 0.9, \"watchRange\": 3 } } }", out object root, out _);
            ((GunnerJsonObject)root).TryGet("aim", out object aim);
            ((GunnerJsonObject)aim).TryGet("shooter", out object shooter);
            var section = schema.Copy().Exclude("watchRange", "watchOnly");
            var r = GunnerValueJson.ReadValues(shooter, section, "aim.shooter", null);
            bool unknownPath = false;
            foreach (var i in r.issues) if (i.key == GunnerValueJson.KeyUnknown && i.arg == "aim.shooter.watchRange") unknownPath = true;
            Check(!r.HasError && r.values.Count == 1 && unknownPath, "7a 除いた欄は節では知らないキー(場所つきで黄)");
            var into = new GunnerValueRead();
            GunnerValueJson.ReadValues(new List<object>(), section, "aim.zone", null, into);
            Check(into.HasError && HasIssue(into, GunnerValueJson.KeyBadType, true), "7b 節がオブジェクトでなければ赤");
        }

        sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
        return sb.ToString();
    }

    static bool Same(object a, object b)
    {
        if (a is float[] fa && b is float[] fb)
        {
            if (fa.Length != fb.Length) return false;
            for (int i = 0; i < fa.Length; i++) if (fa[i] != fb[i]) return false;
            return true;
        }
        return Equals(a, b);
    }
}
