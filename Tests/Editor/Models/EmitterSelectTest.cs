// GunnerRig 0.4.0 の射出口の選び方・射出口ごとの角度・狙う組・暴れ止めを、Unity なしの純計算で確かめる。
// 式は GunnerRig.cs の _SolveWithin / _SolveFrom / _SolveNearest / Wish / AimLimit / _SetAimEmitters / StepLinks の
// 暴れ止め / SteadyUp をそのまま写したもの(Vector3.Angle は Unity と同じ式)。
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class EmitterSelectTest
    {
        const float Deg = (float)(Math.PI / 180.0);
        const float FlipPitch = 80f;

        static float UAngle(Vector3 a, Vector3 b)
        {
            float den = (float)Math.Sqrt(a.LengthSquared() * b.LengthSquared());
            if (den < 1e-15f) return 0f;
            float d = Math.Max(-1f, Math.Min(1f, Vector3.Dot(a, b) / den));
            return (float)Math.Acos(d) / Deg;
        }

        // rig の写し(必要な所だけ)
        class Rig
        {
            public Vector3[] pos;
            public Vector3[] rest;      // 休みの向き(ワールド、長さ1)
            public float aimMaxAngle = 60f;
            public float[] aimMaxAngles;
            public float maxRange = 30f;
            public List<(Vector3 c, float r)> walls = new List<(Vector3, float)>();
            public Vector3 solveDir;
            public float solveDistance;
            public int solveCasts;
            public int aimEmitters;

            public int Count => pos.Length;

            public bool Blocked(Vector3 o, Vector3 d, float dist)
            {
                foreach (var w in walls)
                {
                    float t = Vector3.Dot(w.c - o, d);
                    if (t < 0f || t > dist) continue;
                    if (Vector3.DistanceSquared(o + d * t, w.c) < w.r * w.r) return true;
                }
                return false;
            }

            public float AimLimit(int i)
            {
                if (aimMaxAngles != null && i < aimMaxAngles.Length && aimMaxAngles[i] > 0f) return aimMaxAngles[i];
                return aimMaxAngle;
            }

            // 0.3.0 / 0.4.0 の元の _SolveFor(比べる相手)
            public int OldSolveFor(Vector3 point, out Vector3 dirOut)
            {
                int best = -1; float bestAngle = 1e9f; dirOut = default;
                for (int i = 0; i < Count; i++)
                {
                    Vector3 to = point - pos[i]; float dist = to.Length();
                    if (dist < 0.0001f || dist > maxRange) continue;
                    Vector3 dir = to / dist;
                    float angle = UAngle(rest[i], dir);
                    if (angle > aimMaxAngle || angle >= bestAngle) continue;
                    if (Blocked(pos[i], dir, dist)) continue;
                    best = i; bestAngle = angle; dirOut = dir;
                }
                return best;
            }

            public int SolveWithin(Vector3 point, float maxAngle)
            {
                solveCasts = 0;
                int best = -1; float bestAngle = 1e9f;
                for (int i = 0; i < Count; i++)
                {
                    Vector3 to = point - pos[i]; float dist = to.Length();
                    if (dist < 0.0001f || dist > maxRange) continue;
                    Vector3 dir = to / dist;
                    float angle = UAngle(rest[i], dir);
                    float limit = maxAngle > 0f ? maxAngle : AimLimit(i);
                    if (angle > limit || angle >= bestAngle) continue;
                    solveCasts++;
                    if (Blocked(pos[i], dir, dist)) continue;
                    best = i; bestAngle = angle; solveDir = dir; solveDistance = dist;
                }
                return best;
            }

            public bool SolveFrom(int e, Vector3 point)
            {
                solveCasts = 0;
                if (e < 0 || e >= Count) return false;
                Vector3 to = point - pos[e]; float dist = to.Length();
                if (dist < 0.0001f || dist > maxRange) return false;
                Vector3 dir = to / dist;
                if (UAngle(rest[e], dir) > AimLimit(e)) return false;
                solveCasts = 1;
                if (Blocked(pos[e], dir, dist)) return false;
                solveDir = dir; solveDistance = dist;
                return true;
            }

            public int SolveNearest(Vector3 point, int keep, float keepMargin)
            {
                solveCasts = 0;
                var sd = new float[Count];
                int candidates = 0;
                for (int i = 0; i < Count; i++)
                {
                    sd[i] = -1f;
                    Vector3 to = point - pos[i]; float dist = to.Length();
                    if (dist < 0.0001f || dist > maxRange) continue;
                    if (UAngle(rest[i], to / dist) > AimLimit(i)) continue;
                    sd[i] = i == keep ? Math.Max(dist - keepMargin, 0f) : dist;
                    candidates++;
                }
                for (int n = 0; n < candidates; n++)
                {
                    int pick = -1;
                    for (int i = 0; i < Count; i++) { if (sd[i] < 0f) continue; if (pick < 0 || sd[i] < sd[pick]) pick = i; }
                    if (pick < 0) break;
                    sd[pick] = -1f;
                    Vector3 span = point - pos[pick]; float length = span.Length(); Vector3 way = span / length;
                    solveCasts++;
                    if (Blocked(pos[pick], way, length)) continue;
                    solveDir = way; solveDistance = length;
                    return pick;
                }
                return -1;
            }

            public void SetAimEmitters(int mask)
            {
                int all = Count >= 16 ? 0xFFFF : (1 << Count) - 1;
                int m = mask & all;
                if (m == all) m = 0;
                aimEmitters = m;
            }

            public static bool InAim(int i, int mask) => mask == 0 || (mask & (1 << i)) != 0;

            // _FireVolley が撃つ射出口の一覧
            public List<int> VolleyEmitters(int mask)
            {
                var list = new List<int>();
                for (int i = 0; i < Count; i++) if ((mask & (1 << i)) != 0 && InAim(i, aimEmitters)) list.Add(i);
                return list;
            }
        }

        // 前と後ろに射出口を1つずつ持つ形(ローカル: +X が正面、+Y が上。射出口0 =後ろ、射出口1 = 前)。口は正面・水平、お尻は後ろ向きで水平から50度下。
        static Rig TwoEmitterRig(float buttLimit, float mouthLimit, bool perEmitter)
        {
            var r = new Rig();
            r.pos = new[] { new Vector3(-0.55f, 0.30f, 0f), new Vector3(0.55f, 0.40f, 0f) };
            r.rest = new[] { Vector3.Normalize(new Vector3(-(float)Math.Cos(50 * Deg), -(float)Math.Sin(50 * Deg), 0f)), Vector3.UnitX };
            if (perEmitter) r.aimMaxAngles = new[] { buttLimit, mouthLimit };
            return r;
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Check(string name, bool ok, string detail = "")
            {
                if (!ok) fails++;
                sb.AppendLine($"{name}: {detail} {(ok ? "OK" : "NG")}");
            }
            var rnd = new Random(12345);
            Vector3 RandPoint() => new Vector3((float)(rnd.NextDouble() * 12 - 6), (float)(rnd.NextDouble() * 4 - 1), (float)(rnd.NextDouble() * 12 - 6));

            // 1. 既定値: aimMaxAngles が空なら _SolveFor(= _SolveWithin(p, 0))は元の _SolveFor とビット単位で同じ
            {
                var r = TwoEmitterRig(0, 0, false);
                r.walls.Add((new Vector3(1.5f, 0.8f, 0.5f), 0.4f));
                int diff = 0;
                for (int k = 0; k < 5000; k++)
                {
                    var p = RandPoint();
                    int a = r.OldSolveFor(p, out var da);
                    int b = r.SolveWithin(p, 0f);
                    if (a != b || (a >= 0 && !da.Equals(r.solveDir))) diff++;
                }
                Check("既定値で _SolveFor が元と同じ(5000点)", diff == 0, $"違い {diff} 件");
            }

            // 2. _SolveWithin(p, 角度) は「aimMaxAngle を一時的に書き換えて _SolveFor」と同じ結果で、rig の欄は変えない
            {
                var r = TwoEmitterRig(0, 0, false);
                int diff = 0;
                foreach (float use in new[] { 30f, 90f, 120f })
                {
                    for (int k = 0; k < 2000; k++)
                    {
                        var p = RandPoint();
                        float before = r.aimMaxAngle;
                        int b = r.SolveWithin(p, use);
                        var db = r.solveDir;
                        if (r.aimMaxAngle != before) diff++;
                        // 元のやり方(書き換えて解いて戻す)
                        r.aimMaxAngle = use;
                        int a = r.OldSolveFor(p, out var da);
                        r.aimMaxAngle = before;
                        if (a != b || (a >= 0 && !da.Equals(db))) diff++;
                    }
                }
                Check("_SolveWithin = 書き換え式の結果、rig の aimMaxAngle は 60 のまま", diff == 0 && r.aimMaxAngle == 60f, $"違い {diff} 件、aimMaxAngle {r.aimMaxAngle}");
            }

            // 3. 狙い 0/20/40/60/80 度: 射出口ごとの角度で切る所(Wish の円錐)
            {
                var sbLine = new StringBuilder();
                bool ok = true;
                foreach (float aim in new[] { 0f, 20f, 40f, 60f, 80f })
                {
                    // 休みの向き +Z から左右に aim 度
                    Vector3 rest = Vector3.UnitZ;
                    Vector3 w = new Vector3((float)Math.Sin(aim * Deg), 0, (float)Math.Cos(aim * Deg));
                    float clampDefault = Math.Min(UAngle(rest, w), 60f);    // aimMaxAngle 60
                    float clampWide = Math.Min(UAngle(rest, w), 100f);      // aimMaxAngles 100
                    float expDef = Math.Min(aim, 60f), expWide = aim;
                    if (Math.Abs(clampDefault - expDef) > 0.01f || Math.Abs(clampWide - expWide) > 0.01f) ok = false;
                    sbLine.Append($"{aim}→{clampDefault:F1}/{clampWide:F1} ");
                }
                Check("狙いの角度(既定60 / 射出口ごと100)", ok, sbLine.ToString());
                var r = TwoEmitterRig(100, 0, true);
                Check("AimLimit: [100, 0] → お尻 100、口は aimMaxAngle 60", r.AimLimit(0) == 100f && r.AimLimit(1) == 60f && r.AimLimit(5) == 60f,
                    $"{r.AimLimit(0)} / {r.AimLimit(1)} / 範囲外 {r.AimLimit(5)}");
            }

            // 4. 最寄りの射出口: 口とお尻
            {
                var r = TwoEmitterRig(100, 100, true);
                float chest = 1.3f;
                (string name, Vector3 p, int expect)[] cases =
                {
                    ("正面 3 m", new Vector3(3f, chest, 0f), 1),
                    ("真後ろ 3 m", new Vector3(-3f, chest, 0f), 0),
                    ("真横 3 m(少し前)", new Vector3(0.3f, chest, 3f), 1),
                    ("斜め後ろ 45 度", new Vector3(-2.1f, chest, 2.1f), 0),
                    ("斜め前 45 度", new Vector3(2.1f, chest, -2.1f), 1),
                    ("真後ろ至近 0.6 m", new Vector3(-1.1f, 0.6f, 0f), 0),
                };
                foreach (var c in cases)
                {
                    int got = r.SolveNearest(c.p, -1, 0f);
                    Check("最寄り: " + c.name, got == c.expect, $"選んだ {got}(期待 {c.expect})、壁の確認 {r.solveCasts} 回");
                }
                // 真上 3 m: 口からの角度は 100 度を少し超え、お尻は届かない → どちらも無し
                int up = r.SolveNearest(new Vector3(0f, 3f, 0f), -1, 0f);
                float mouthUp = UAngle(r.rest[1], new Vector3(0f, 3f, 0f) - r.pos[1]);
                Check("真上 3 m", up == -1, $"選んだ {up}(口からの角度 {mouthUp:F1} 度 > 100)");
                // 近い口が壁でふさがれた → 次に近いお尻(両方が届く真横の帯)。
                // この近似の形では、お尻 100 度は胸の高さの真横まで届かない(約112度)ので、お尻を 120 度にして確かめる。
                var rw = TwoEmitterRig(120, 100, true);
                Vector3 side = new Vector3(0.1f, chest, 3f);
                rw.walls.Add((rw.pos[1] + Vector3.Normalize(side - rw.pos[1]) * 0.5f, 0.15f));
                int got2 = rw.SolveNearest(side, -1, 0f);
                Check("口が壁でふさがれた真横 → お尻", got2 == 0 && rw.solveCasts == 2, $"選んだ {got2}、壁の確認 {rw.solveCasts} 回");
                // 角度の外の射出口は候補外(既定 60 度の rig で真後ろ → 口は外、お尻は休みから 68 度で外 → 無し)
                var r60 = TwoEmitterRig(0, 0, false);
                int got3 = r60.SolveNearest(new Vector3(-3f, chest, 0f), -1, 0f);
                Check("既定60度で真後ろ胸の高さ(お尻の休みから約68度)", got3 == -1 && r60.solveCasts == 0, $"選んだ {got3}、壁の確認 {r60.solveCasts} 回");
                // _SolveFrom
                bool fromMouthFront = r.SolveFrom(1, new Vector3(3f, chest, 0f));
                bool fromMouthBack = r.SolveFrom(1, new Vector3(-3f, chest, 0f));
                Check("_SolveFrom: 口から正面は届く・真後ろは届かない", fromMouthFront && !fromMouthBack);
            }

            // 5. ちらつき止め: 真横の境目をゆっくり行き来し、細かく揺れる相手(0.2 秒ごとに見回る)。
            //    両方が届く帯を作るため、お尻は 120 度(近似の形では 100 度だと真横の胸の高さに届かない)。
            {
                var r = TwoEmitterRig(120, 100, true);
                float margin = 0.2f, hold = 0.5f;
                int current = -1; float holdUntil = -1f, lastSwitch = -10f;
                int switches = 0, fastSwitch = 0;
                float minGap = 99f;
                var log = new StringBuilder();
                float t = 0f;
                // 角度 θ(正面 0 度、後ろ 180 度)を 60→120→60 度とゆっくり動かし、±3 度の揺れを足す
                for (int k = 0; k < 600; k++, t += 0.2f)
                {
                    float baseTheta = 90f + 30f * (float)Math.Sin(k * 2 * Math.PI / 300.0);
                    float theta = baseTheta + (k % 2 == 0 ? 3f : -3f);
                    var p = new Vector3(3f * (float)Math.Cos(theta * Deg), 1.3f, 3f * (float)Math.Sin(theta * Deg));
                    float useMargin = t < holdUntil ? 1000f : margin;
                    int got = r.SolveNearest(p, current, useMargin);
                    if (got != current)
                    {
                        if (current >= 0)
                        {
                            switches++;
                            float gap = t - lastSwitch;
                            if (gap < minGap) minGap = gap;
                            if (gap < hold - 1e-4f) fastSwitch++;
                            if (switches <= 6) log.Append($"[{t:F1}s θ{theta:F0}→{got}] ");
                        }
                        lastSwitch = t;
                        holdUntil = t + hold;
                        current = got;
                    }
                }
                // 境目付近の揺れ(±3 度、0.2 秒ごと)でも、替わるのは帯を越えた時だけ: 2往復(60→120→60 度を2回)で4回
                Check("ちらつき止め(±3度の揺れ、2往復)", fastSwitch == 0 && switches == 4,
                    $"替わった回数 {switches}、最短の間隔 {minGap:F1} 秒、0.5 秒より早い切り替え {fastSwitch} 件 {log}");
                // 帯の幅: 境目で口とお尻の距離の差が 0.2 m になる角度
                float lo = 0, hi = 0;
                for (float th = 60; th <= 120; th += 0.1f)
                {
                    var p = new Vector3(3f * (float)Math.Cos(th * Deg), 1.3f, 3f * (float)Math.Sin(th * Deg));
                    float d = Vector3.Distance(p, r.pos[0]) - Vector3.Distance(p, r.pos[1]);
                    if (lo == 0 && d < margin) lo = th;
                    if (d > -margin) hi = th;
                }
                sb.AppendLine($"  参考: 替えない帯 = 正面から {lo:F1}〜{hi:F1} 度(3 m、胸の高さ)");
                // 揺れを足さなくても切り替えは往復ごとに2回
                int current2 = -1, sw2 = 0;
                for (int k = 0; k < 600; k++)
                {
                    float theta = 90f + 30f * (float)Math.Sin(k * 2 * Math.PI / 300.0);
                    var p = new Vector3(3f * (float)Math.Cos(theta * Deg), 1.3f, 3f * (float)Math.Sin(theta * Deg));
                    int got = r.SolveNearest(p, current2, margin);
                    if (got != current2) { if (current2 >= 0) sw2++; current2 = got; }
                }
                Check("揺れなし2往復の切り替え", sw2 == 4, $"{sw2} 回(期待 4)");
                // 比べる: 余裕も待ちも無いと、同じ揺れで何度も替わる。帯より大きい揺れ(±15 度)でも、待ちで 0.5 秒以内に2回替わらない
                {
                    int cur = -1, swNone = 0, swBig = 0, fastBig = 0; float hu = -1f, ls = -10f, tt = 0f;
                    for (int k = 0; k < 600; k++, tt += 0.2f)
                    {
                        float bt = 90f + 30f * (float)Math.Sin(k * 2 * Math.PI / 300.0);
                        float th = bt + (k % 2 == 0 ? 3f : -3f);
                        var p = new Vector3(3f * (float)Math.Cos(th * Deg), 1.3f, 3f * (float)Math.Sin(th * Deg));
                        int g = r.SolveNearest(p, cur, 0f);
                        if (g != cur) { if (cur >= 0) swNone++; cur = g; }
                    }
                    cur = -1; tt = 0f;
                    for (int k = 0; k < 600; k++, tt += 0.2f)
                    {
                        float bt = 90f + 30f * (float)Math.Sin(k * 2 * Math.PI / 300.0);
                        float th = bt + (k % 2 == 0 ? 15f : -15f);
                        var p = new Vector3(3f * (float)Math.Cos(th * Deg), 1.3f, 3f * (float)Math.Sin(th * Deg));
                        int g = r.SolveNearest(p, cur, tt < hu ? 1000f : margin);
                        if (g != cur)
                        {
                            // 待ちの間に替わってよいのは、今の射出口が届かなくなった時だけ(設計どおり)
                            if (cur >= 0) { swBig++; if (tt - ls < hold - 1e-4f && r.SolveFrom(cur, p)) fastBig++; }
                            ls = tt; hu = tt + hold; cur = g;
                        }
                    }
                    Check("帯より大きい揺れ(±15度): 待ちの間は、今の射出口が届く限り替わらない", fastBig == 0, $"替わった回数 {swBig}(届かなくなって替わったものを含む)、待ちを破った切り替え {fastBig} 件(参考: 余裕も待ちも無いと ±3 度で {swNone} 回)");
                }
                // keep が届かなくなったら待たずに替える(大きな余裕を渡していても)
                var rw = TwoEmitterRig(120, 100, true);
                Vector3 back = new Vector3(-3f, 1.3f, 0f);
                int got3 = rw.SolveNearest(back, 1, 1000f);
                Check("keep(口)が角度の外 → 待たずにお尻", got3 == 0, $"選んだ {got3}");
            }

            // 6. 狙う組と一斉射
            {
                var r = TwoEmitterRig(100, 100, true);
                var cases = new (int input, int expect)[] { (-1, 0), (0, 0), (3, 0), (1, 1), (2, 2), (4, 0), (5, 1) };
                bool ok = true; var s = new StringBuilder();
                foreach (var c in cases) { r.SetAimEmitters(c.input); if (r.aimEmitters != c.expect) ok = false; s.Append($"{c.input}→{r.aimEmitters} "); }
                Check("_SetAimEmitters の正規化(射出口2つ)", ok, s.ToString());
                r.SetAimEmitters(-1);
                var all = string.Join(",", r.VolleyEmitters(-1));
                r.SetAimEmitters(1);
                var butt = string.Join(",", r.VolleyEmitters(-1));
                var none = string.Join(",", r.VolleyEmitters(2));
                Check("一斉射 = mask かつ 狙う組", all == "0,1" && butt == "0" && none == "", $"全部[{all}] お尻だけ[{butt}] 口の mask×お尻の組[{none}]");
            }

            // 7. 暴れ止め: 狙える角度が 80 度以下なら左右の保持は起きない。上下 80 度を超えると左右の目標を保つ
            {
                int triggered = 0;
                for (int k = 0; k < 20000; k++)
                {
                    // 休みの向き +Z から 60 度以内の任意の向き(Wish が切った後の向き)
                    var w = Vector3.Normalize(new Vector3((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1)));
                    if (UAngle(Vector3.UnitZ, w) > 60f) continue;
                    float tp = (float)Math.Atan2(w.Y, Math.Sqrt(w.X * w.X + w.Z * w.Z)) / Deg;
                    if (tp > FlipPitch || tp < -FlipPitch) triggered++;
                }
                Check("可動域 60 度では暴れ止めが一度も効かない(2万方向)", triggered == 0, $"効いた {triggered} 件");
                // 真上付近で細かく揺れる相手(上下 84〜89 度、数 cm の揺れ): 左右の目標が暴れない
                {
                    float followYaw = 30f, holdSwing = 0f, rawMin = 999f, rawMax = -999f;
                    for (int k = 0; k < 200; k++)
                    {
                        var w = Vector3.Normalize(new Vector3((float)(rnd.NextDouble() - 0.5) * 0.1f, 1f, (float)(rnd.NextDouble() - 0.5) * 0.1f + 0.05f));
                        float ty = (float)Math.Atan2(w.X, w.Z) / Deg;
                        float tp = (float)Math.Atan2(w.Y, Math.Sqrt(w.X * w.X + w.Z * w.Z)) / Deg;
                        rawMin = Math.Min(rawMin, ty); rawMax = Math.Max(rawMax, ty);
                        if (tp > FlipPitch || tp < -FlipPitch) ty = followYaw;
                        holdSwing = Math.Max(holdSwing, Math.Abs(ty - 30f));
                        followYaw = ty;
                    }
                    Check("真上付近で揺れる相手の左右の目標", holdSwing <= 1e-4f, $"暴れ止めあり 揺れ {holdSwing:F1} 度 / なし {rawMin:F0}〜{rawMax:F0} 度");
                }
                // SteadyUp の境目: 上下 80 度ちょうどで、保った左右の角度 = 今の左右の角度なら上の軸は今までの式と同じ
                float maxDiff = 0f;
                foreach (float yaw in new[] { -40f, 0f, 25f, 70f })
                {
                    Quaternion turn = Euler(-79.999f, yaw, 0);
                    Vector3 dir = Vector3.Transform(Vector3.UnitZ, turn);
                    Vector3 frameUp = Vector3.Transform(Vector3.UnitY, turn);
                    // LookRotation(dir, up = +Y) の上の軸
                    Vector3 lookUp = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, dir) * dir);
                    maxDiff = Math.Max(maxDiff, UAngle(frameUp, lookUp));
                }
                Check("暴れ止めの境目で上の軸が連続", maxDiff < 0.05f, $"差 {maxDiff:F4} 度");
            }

            // 8. 手に持つ球の角度での狙い(AimAngles): 射出口ごとの角度を 120 にしても、斜めいっぱいは今までの可動域 60 度で切る
            {
                // Wish の写し: limit = AimLimit(i)、角度での狙いなら min(limit, aimMaxAngle)。RotateTowards は角度だけ回す。
                Vector3 WishAngles(float yaw, float pitch, float aimMax, float[] perEmitter, bool angleMode)
                {
                    Vector3 rest = Vector3.UnitZ;
                    Vector3 w = Vector3.Transform(Vector3.UnitZ, Euler(-pitch, yaw, 0));
                    float limit = perEmitter != null && perEmitter.Length > 0 && perEmitter[0] > 0f ? perEmitter[0] : aimMax;
                    if (angleMode && aimMax < limit) limit = aimMax;
                    float a = UAngle(rest, w);
                    if (a <= limit) return w;
                    Vector3 axis = Vector3.Normalize(Vector3.Cross(rest, w));
                    return Vector3.Transform(rest, Quaternion.CreateFromAxisAngle(axis, limit * Deg));
                }
                float diagRaw = UAngle(Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, Euler(-60, 60, 0)));
                float diagNew = UAngle(Vector3.UnitZ, WishAngles(60, 60, 60f, new[] { 120f }, true));
                float diagOld = UAngle(Vector3.UnitZ, WishAngles(60, 60, 60f, new[] { 120f }, false));
                Check("球の斜めいっぱい(左右60・上下60)", diagNew <= 60.001f, $"丸めなし {diagRaw:F1} 度 / 直す前の式で射出口ごと120 {diagOld:F1} 度 / 直した式 {diagNew:F1} 度");
                // 既定(射出口ごとの角度が空)では、直した式と直す前の式がスティックの全範囲でビット単位で同じ
                int diff = 0, diff120 = 0;
                for (float y = -60f; y <= 60f; y += 2.5f)
                    for (float p = -60f; p <= 60f; p += 2.5f)
                    {
                        var a = WishAngles(y, p, 60f, null, true);
                        var b = WishAngles(y, p, 60f, null, false);
                        if (!a.Equals(b)) diff++;
                        // 射出口ごと 120 でも、直した式は既定の rig の丸めと同じ
                        var c = WishAngles(y, p, 60f, new[] { 120f }, true);
                        if (!c.Equals(b)) diff120++;
                    }
                Check("球の狙いが既定の丸めと一致(スティック全範囲 49×49 点)", diff == 0 && diff120 == 0, $"既定で違い {diff} 件、射出口ごと120で違い {diff120} 件");
                // 向き・点での狙い(見張りなど)は射出口ごとの角度で切る(80 度の向きは 120 の射出口では切られない)
                Vector3 w80 = Vector3.Transform(Vector3.UnitZ, Euler(0, 80, 0));
                float dirMode = UAngle(Vector3.UnitZ, WishAngles(80, 0, 60f, new[] { 120f }, false));
                Check("向きでの狙いは射出口ごとの角度で切る", Math.Abs(dirMode - 80f) < 0.01f, $"{dirMode:F1} 度");
            }

            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }

        static Quaternion AA(Vector3 axis, float deg) => Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), deg * Deg);
        static Quaternion Euler(float x, float y, float z) => AA(Vector3.UnitY, y) * AA(Vector3.UnitX, x) * AA(Vector3.UnitZ, z);
    }
}
