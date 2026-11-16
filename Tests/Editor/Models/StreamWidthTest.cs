// 射出口ごとの、つないだ線の幅(GunnerStream._Init)を確かめる。
// 式は GunnerStream._Init の写し: 倍率 = streamWidthScales[e](空・短い・0 以下・NaN は 1)、幅 = max(streamWidth × 倍率, 0.0005)。
// 合格の条件: 空の配列では全部の射出口が今までの幅(max(streamWidth, 0.0005))とビット単位で同じ。決まりどおりの所で 1 倍になる。
using System;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class StreamWidthTest
    {
        // GunnerStream._Init の写し(式を変えたら、ここも直す)
        static float[] Widths(float streamWidth, float[] scales, int count)
        {
            var widths = new float[count];
            for (int e = 0; e < count; e++)
            {
                float scale = scales != null && e < scales.Length && scales[e] > 0f ? scales[e] : 1f;
                widths[e] = Math.Max(streamWidth * scale, 0.0005f);
            }
            return widths;
        }

        // 0.4.0 の幅(射出口ごとの倍率が無かった時)
        static float Old(float streamWidth) => Math.Max(streamWidth, 0.0005f);

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Check(string name, bool ok, string detail)
            {
                if (!ok) fails++;
                sb.AppendLine($"{name}: {detail} {(ok ? "OK" : "NG")}");
            }

            float[] bases = { 0.02f, 0.0125f, 0.0003f, 0f, 0.1f };
            foreach (float w in bases)
            {
                foreach (var (label, scales) in new (string, float[])[] { ("null", null), ("空", new float[0]) })
                {
                    var got = Widths(w, scales, 3);
                    bool same = true;
                    for (int e = 0; e < got.Length; e++) same &= BitConverter.SingleToInt32Bits(got[e]) == BitConverter.SingleToInt32Bits(Old(w));
                    Check($"幅 {w}・倍率 {label}", same, $"全部の射出口が 0.4.0 と同じ幅({Old(w)})");
                }
            }

            var a = Widths(0.02f, new[] { 1f, 2f }, 4);
            Check("短い配列", a[0] == 0.02f && a[1] == 0.04f && a[2] == 0.02f && a[3] == 0.02f, $"{a[0]}, {a[1]}, {a[2]}, {a[3]}(足りない所は 1 倍)");

            var b = Widths(0.02f, new[] { 0f, -1f, float.NaN, float.NegativeInfinity, 0.5f }, 5);
            Check("0 以下・NaN", b[0] == 0.02f && b[1] == 0.02f && b[2] == 0.02f && b[3] == 0.02f && b[4] == 0.01f, $"{b[0]}, {b[1]}, {b[2]}, {b[3]}, {b[4]}(0・負・NaN・-∞ は 1 倍)");

            var c = Widths(0.02f, new[] { 0.001f, 0.01f }, 2);
            Check("下限", c[0] == 0.0005f && c[1] == Math.Max(0.02f * 0.01f, 0.0005f), $"{c[0]}, {c[1]}(0.0005 より細くしない)");

            var d = Widths(0.02f, new[] { 1f, 1.5f, 3f }, 2);
            Check("長い配列", d.Length == 2 && d[0] == 0.02f && d[1] == 0.02f * 1.5f, $"{d[0]}, {d[1]}(余る所は使わない)");

            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }
    }
}
