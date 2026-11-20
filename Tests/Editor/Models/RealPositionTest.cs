// 前後に射出口を1つずつ持つ実際の形(後ろの射出口は水平から約50度下向き)で、射出口ごとの角度 100 度と 120 度の
// 「どちらも届かない帯」、等距離の線、同じ距離の決まり、角度の外の向きが端で止まること(Wish)を純計算で確かめる。
// 期待値は設計の早見表(位置は後ろの射出口から前へ何 m、軸から横へ何 m。胸の高さは床から 1.0 m)。
using System;
using System.Numerics;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class RealPositionTest
    {
        const float Deg = (float)(Math.PI / 180.0);

        static float UAngle(Vector3 a, Vector3 b)
        {
            float den = (float)Math.Sqrt(a.LengthSquared() * b.LengthSquared());
            if (den < 1e-15f) return 0f;
            float d = Math.Max(-1f, Math.Min(1f, Vector3.Dot(a, b) / den));
            return (float)Math.Acos(d) / Deg;
        }

        // 射出口0 = 後ろ、射出口1 = 前(世界の値。正面は +X)
        static readonly Vector3 BackPos = new Vector3(19.663f, 0.229f, -3.000f);
        static readonly Vector3 BackRest = Vector3.Normalize(new Vector3(-0.643f, -0.766f, 0f));
        static readonly Vector3 FrontPos = new Vector3(20.72f, 0.40f, -3.00f);
        static readonly Vector3 FrontRest = Vector3.UnitX;
        const float Chest = 1.0f;

        static Vector3 At(float forward, float side) => new Vector3(BackPos.X + forward, Chest, BackPos.Z + side);
        static float BackAngle(Vector3 p) => UAngle(BackRest, p - BackPos);
        static float FrontAngle(Vector3 p) => UAngle(FrontRest, p - FrontPos);

        // 角度が limit になる「前へ何 m」を二分法で(後ろの射出口は前へ行くほど角度が大きい、前の射出口は後ろへ行くほど大きい)
        static float BackEdge(float side, float limit)
        {
            float lo = -20f, hi = 20f;
            for (int k = 0; k < 60; k++) { float m = (lo + hi) * 0.5f; if (BackAngle(At(m, side)) <= limit) lo = m; else hi = m; }
            return lo;
        }
        static float FrontEdge(float side, float limit)
        {
            float lo = -20f, hi = 20f;
            for (int k = 0; k < 60; k++) { float m = (lo + hi) * 0.5f; if (FrontAngle(At(m, side)) <= limit) hi = m; else lo = m; }
            return hi;
        }

        // GunnerRig.Wish の写し(向きの指定 → 射出口ごとの角度の円錐に収める)。Unity の RotateTowards(角度だけ回す)を写す。
        static Vector3 Wish(Vector3 rest, Vector3 w, float limit)
        {
            w = Vector3.Normalize(w);
            float angle = UAngle(rest, w);
            if (angle <= limit) return w;
            Vector3 axis = Vector3.Cross(rest, w);
            if (axis.LengthSquared() < 1e-12f) axis = Vector3.UnitY;
            return Vector3.Transform(rest, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), limit * Deg));
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Check(string name, bool ok, string detail) { if (!ok) fails++; sb.AppendLine($"{name}: {detail} {(ok ? "OK" : "NG")}"); }
            const float Tol = 0.03f;

            // 1. 100 度の早見表
            (float side, float back, float front)[] t100 = { (1f, -0.55f, 0.86f), (2f, -0.34f, 0.69f), (3f, -0.09f, 0.52f), (4f, 0.18f, 0.35f), (5f, 0.45f, 0.17f), (6f, 0.72f, 0.00f) };
            foreach (var r in t100)
            {
                float b = BackEdge(r.side, 100f), f = FrontEdge(r.side, 100f);
                string gap = f > b ? $"どちらも届かない {f - b:F2} m" : $"両方届く {b - f:F2} m";
                Check($"100度 横 {r.side} m", Math.Abs(b - r.back) <= Tol && Math.Abs(f - r.front) <= Tol, $"後ろ {b:F2} 以下(表 {r.back})、前 {f:F2} 以上(表 {r.front})、{gap}");
            }
            // 2. 120 度の早見表
            (float side, float back, float front)[] t120 = { (0.5f, -0.19f, 0.61f), (1f, 0.06f, 0.39f), (1.5f, 0.43f, 0.13f), (2f, 0.88f, -0.14f), (3f, 1.91f, -0.70f), (6f, 5.37f, -2.42f) };
            foreach (var r in t120)
            {
                float b = BackEdge(r.side, 120f), f = FrontEdge(r.side, 120f);
                string gap = f > b ? $"どちらも届かない {f - b:F2} m" : $"両方届く {b - f:F2} m";
                Check($"120度 横 {r.side} m", Math.Abs(b - r.back) <= Tol && Math.Abs(f - r.front) <= Tol, $"後ろ {b:F2} 以下(表 {r.back})、前 {f:F2} 以上(表 {r.front})、{gap}");
            }
            // 3. 帯の有無: 100 度では横 2 m に帯、120 度では横 1.5 m 以上で帯なし
            {
                bool gap100at2 = FrontEdge(2f, 100f) > BackEdge(2f, 100f);
                bool none120 = true;
                for (float s = 1.5f; s <= 6.001f; s += 0.1f) if (FrontEdge(s, 120f) > BackEdge(s, 120f)) none120 = false;
                Check("100度は横 2 m に帯がある・120度は横 1.5〜6 m に帯が無い", gap100at2 && none120, "");
            }
            // 4. 等距離の線(前へ 0.42 m、横の距離によらない)と、同じ距離なら番号の小さいほう(後ろ)
            {
                float maxDev = 0f;
                foreach (float s in new[] { 1f, 2f, 3f, 6f })
                {
                    float lo = -2f, hi = 2f;
                    for (int k = 0; k < 60; k++) { float m = (lo + hi) * 0.5f; var p = At(m, s); if (Vector3.Distance(p, BackPos) < Vector3.Distance(p, FrontPos)) lo = m; else hi = m; }
                    maxDev = Math.Max(maxDev, Math.Abs(lo - 0.42f));
                }
                Check("等距離の線 = 後ろから前へ 0.42 m(横 1/2/3/6 m)", maxDev <= Tol, $"最大のずれ {maxDev:F3} m");
                // _SolveNearest の選び方(届く射出口の距離が同じなら番号の小さいほう)を写して、同じ距離の点で後ろ(0)を選ぶ
                float[] d = { 2.18f, 2.18f };
                int pick = -1;
                for (int i = 0; i < 2; i++) if (pick < 0 || d[i] < d[pick]) pick = i;
                Check("同じ距離なら番号の小さいほう", pick == 0, $"選んだ {pick}");
            }
            // 5. 横 2 m の選ばれ方の表(距離 ±0.03 m、角度 ±2 度)
            {
                (float fwd, float dF, float aF, float dB, float aB)[] rows =
                {
                    (2.0f, 2.29f, 66f, 2.93f, 130f), (1.06f, 2.09f, 90f, 2.39f, 122f), (0.69f, 2.12f, 100f, 2.25f, 117f),
                    (0.53f, 2.15f, 104f, 2.21f, 115f), (0.42f, 2.18f, 107f, 2.18f, 113f), (0.0f, 2.34f, 117f, 2.14f, 106f),
                    (-0.34f, 2.51f, 124f, 2.17f, 100f), (-1.0f, 2.93f, 135f, 2.37f, 89f),
                };
                foreach (var r in rows)
                {
                    var p = At(r.fwd, 2f);
                    float dF = Vector3.Distance(p, FrontPos), dB = Vector3.Distance(p, BackPos), aF = FrontAngle(p), aB = BackAngle(p);
                    bool ok = Math.Abs(dF - r.dF) <= Tol && Math.Abs(dB - r.dB) <= Tol && Math.Abs(aF - r.aF) <= 2f && Math.Abs(aB - r.aB) <= 2f;
                    Check($"横 2 m・前へ {r.fwd} m", ok, $"前 {dF:F2} m / {aF:F0}度、後ろ {dB:F2} m / {aB:F0}度");
                }
            }
            // 6. 真後ろの至近(後ろの射出口の角度 119 / 107 / 88 度)
            {
                (float back, float expect)[] rows = { (0.3f, 119f), (0.5f, 107f), (1.0f, 88f) };
                foreach (var r in rows)
                {
                    float a = BackAngle(At(-r.back, 0f));
                    Check($"真後ろ {r.back} m", Math.Abs(a - r.expect) <= 2f, $"{a:F1} 度(表 {r.expect})");
                }
            }
            // 7. 角度の外の向きを渡すと、射出口ごとの角度の端で止まる(aimMaxAngles を見る)
            {
                float maxErr = 0f; int outside = 0;
                var rnd = new Random(7);
                foreach (float limit in new[] { 60f, 100f, 120f })
                {
                    for (int k = 0; k < 2000; k++)
                    {
                        var w = Vector3.Normalize(new Vector3((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1)));
                        float a = UAngle(FrontRest, w);
                        var got = Wish(FrontRest, w, limit);
                        float ga = UAngle(FrontRest, got);
                        if (a > limit)
                        {
                            outside++;
                            maxErr = Math.Max(maxErr, Math.Abs(ga - limit));
                            // 端の向きは、休みの向きと渡した向きの作る面の上(相手の側へ寄る)
                            float plane = Math.Abs(Vector3.Dot(got, Vector3.Normalize(Vector3.Cross(FrontRest, w))));
                            maxErr = Math.Max(maxErr, plane / Deg);
                        }
                        else maxErr = Math.Max(maxErr, UAngle(got, w));
                    }
                }
                Check("角度の外の向きは端で止まる(60/100/120 度、各 2000 方向)", maxErr < 0.05f && outside > 0, $"端との差の最大 {maxErr:F4} 度、角度の外 {outside} 件");
                // 横 1 m・前へ 0.2 m(120 度でもどちらも届かない)の相手へ、近いほうの射出口が端まで向く
                var p = At(0.2f, 1f);
                bool frontNearer = Vector3.Distance(p, FrontPos) < Vector3.Distance(p, BackPos);
                Vector3 pos = frontNearer ? FrontPos : BackPos, rest = frontNearer ? FrontRest : BackRest;
                var edge = Wish(rest, p - pos, 120f);
                float edgeAngle = UAngle(rest, edge), toTarget = UAngle(edge, p - pos);
                Check("届かない帯の相手へ近いほうが端まで向く(横 1 m・前へ 0.2 m)", Math.Abs(edgeAngle - 120f) < 0.05f,
                    $"{(frontNearer ? "前" : "後ろ")}の射出口、休みから {edgeAngle:F1} 度、相手との差 {toTarget:F1} 度");
            }

            // 8. ボーンを動かすモデルでの反復計算: 見張りが「今の射出口の位置から相手の胸への向き」を 0.2 秒ごとに見て、
            //    0.5 秒たち 8 度以上変わった時だけ書き直す(見張りの今の書き方のまま)。後ろはボーン3本の鎖、前は頭のボーン1本
            //    (なめらかさ 12、最低 20 度/秒)、射出口ごとの角度 120。胸には ±5 mm の揺れ、休みの座標系には毎フレーム
            //    ±0.3 度の揺れ(Animator の書き換え)を足す。射出口の向きそのもの(世界方向)を 0.1 秒ごとに記録し、
            //    最後の 1.5 秒の幅(終わりの向きからの最大の角度)と、最初の寄り(2 秒まで)の後の最大の速さを見る。
            //    axis = 軸と角度の表し方(StepLinks・AimEmitter の写し)。直す前 = 左右・上下。
            {
                Vector3 buttUp = Vector3.Normalize(new Vector3(-0.766f, 0.643f, 0f));
                Vector3 headPos = new Vector3(20.53f, 0.46f, -3.00f);
                (string name, bool front, Vector3 chest)[] cases =
                {
                    ("後ろ・真後ろ 0.3 m", false, At(-0.3f, 0f)),
                    ("後ろ・真後ろ 0.5 m", false, At(-0.5f, 0f)),
                    ("後ろ・真後ろ 1 m", false, At(-1.0f, 0f)),
                    ("後ろ・5 m 後ろ", false, At(-5.0f, 0f)),
                    ("前・背中の上(胸 1.6 m)", true, new Vector3(BackPos.X + 0.5f, 1.6f, BackPos.Z)),
                    ("前・真上 3 m", true, new Vector3(20.19f, 3.2f, -3.0f)),
                    ("前・頭の後ろ上の至近(約 0.6 m、約116度)", true, new Vector3(20.45f, 0.95f, -3.0f)),
                    ("前・頭の後ろ上の至近(約 0.7 m、約119度)", true, new Vector3(20.38f, 1.0f, -3.0f)),
                };
                foreach (var c in cases)
                {
                    float oldSpread = Simulate(c.front, c.chest, false, headPos, buttUp, out float oldSpeed, out float oldAng);
                    float newSpread = Simulate(c.front, c.chest, true, headPos, buttUp, out float newSpeed, out float newAng);
                    Check($"揺れ: {c.name}", newSpread <= 3f && newSpeed <= 300f,
                        $"軸と角度: 最後の1.5秒の幅 {newSpread:F1} 度・寄りの後の最大の速さ {newSpeed:F0} 度/秒(休みから {newAng:F0} 度)/ 直す前: {oldSpread:F1} 度・{oldSpeed:F0} 度/秒(休みから {oldAng:F0} 度)");
                }
                // 既定(狙える角度が 90 度以下)の連動と、90 度を超える連動の球(角度での狙い)は、今までの左右・上下の式をそのまま通る。
                // StepLinks の分け方(狙える角度 > 90 かつ角度での狙いでない時だけ軸と角度)を写し、目標の角度が直す前とビット単位で同じことを見る。
                {
                    var rnd2 = new Random(11); int diff = 0, count = 0;
                    for (int k = 0; k < 20000; k++)
                    {
                        var v = Vector3.Normalize(new Vector3((float)(rnd2.NextDouble() * 2 - 1), (float)(rnd2.NextDouble() * 2 - 1), (float)(rnd2.NextDouble() * 2 - 1)));
                        if (v.Z < 0f) continue; // 90 度以下の円錐の中だけ(Wish が切った後)
                        count++;
                        float fy0 = (float)(rnd2.NextDouble() * 120 - 60);
                        foreach (var (limitDeg, angleMode) in new[] { (60f, false), (90f, false), (120f, true) })
                        {
                            bool useAxis = limitDeg > 90f && !angleMode;
                            float ty = (float)Math.Atan2(v.X, v.Z) / Deg, tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                            float oy = ty, op = tp;
                            if (op > 80f || op < -80f) oy = fy0;   // 直す前
                            float ny = 0f, np = 0f;
                            if (useAxis) { ny = float.NaN; }
                            else { ny = ty; np = tp; if (np > 80f || np < -80f) ny = fy0; }
                            if (oy != ny || op != np) diff++;
                        }
                    }
                    Check("既定(狙える角度 90 度以下)と 120 度の球の目標の角度は直す前とビット単位で同じ", diff == 0, $"{count} 方向 × 3 通り(60 度・90 度の見張り、120 度の球)で違い {diff} 件");
                }
            }

            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }

        static Quaternion EulerU(float x, float y, float z) =>
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * Deg);

        // GunnerRig.AxisTurn の写し
        static Quaternion AxisTurn(float yaw, float pitch)
        {
            float angle = (float)Math.Sqrt(yaw * yaw + pitch * pitch);
            if (angle < 0.000001f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(new Vector3(-pitch / angle, yaw / angle, 0f), angle * Deg);
        }

        // ボーンで動く射出口のモデル。返り値 = 最後の 1.5 秒の射出口の向き(世界方向)の幅。
        static float Simulate(bool front, Vector3 chestBase, bool axis, Vector3 headPos, Vector3 buttUp, out float maxSpeed, out float lastAngle)
        {
            Vector3 f = front ? FrontRest : BackRest;
            Vector3 u = front ? Vector3.UnitY : buttUp;
            Vector3 r = Vector3.Cross(u, f);
            Vector3 emRest = front ? FrontPos : BackPos;
            Vector3 ToRest(Vector3 w) => new Vector3(Vector3.Dot(w, r), Vector3.Dot(w, u), Vector3.Dot(w, f));
            Vector3 ToWorld(Vector3 l) => r * l.X + u * l.Y + f * l.Z;
            // 後ろはボーン3本の鎖(1本 0.15 m と仮定、1本あたり左右 30・上 25・下 12、鎖全体で 90・75・36)。前は頭のボーン1本。
            int n = front ? 1 : 3;
            const float seg = 0.15f;
            Vector3 pivot = front ? headPos : emRest - f * (seg * 3f);
            Vector3 offset = ToRest(emRest - pivot);
            float yawMax = 30f * n, upMax = 25f * n, downMax = 12f * n;
            const float smooth = 12f, minSpeed = 20f, limit = 120f, flip = 80f;
            float fy = 0f, fp = 0f;
            bool sent = false; Vector3 sentDir = default; float sentAt = -10f, nextCheck = 0f;
            Vector3 aimDir = default; bool aimed = false;
            var rnd = new Random(3);
            float dt = 1f / 60f;
            var dirs = new System.Collections.Generic.List<Vector3>();
            float nextSample = 0f;
            maxSpeed = 0f;
            Vector3 prevWorld = ToWorld(Vector3.UnitZ);
            for (float t = 0f; t < 10f; t += dt)
            {
                float turnY = Math.Max(-yawMax, Math.Min(yawMax, fy));
                float turnP = Math.Max(-downMax, Math.Min(upMax, fp));
                Quaternion turn = axis ? AxisTurn(turnY, turnP) : EulerU(-turnP, turnY, 0);
                Vector3 emPos;
                if (n == 1) emPos = pivot + ToWorld(Vector3.Transform(offset, turn));
                else
                {
                    // 鎖: 全体の回転を n 等分し、k 本目のボーンの先は q^k で曲がる
                    Quaternion q = Quaternion.Slerp(Quaternion.Identity, turn, 1f / n);
                    Quaternion acc = Quaternion.Identity; Vector3 local = Vector3.Zero;
                    for (int k = 0; k < n; k++) { acc = acc * q; local += Vector3.Transform(Vector3.UnitZ, acc) * seg; }
                    emPos = pivot + ToWorld(local);
                }
                if (t >= nextCheck)
                {
                    nextCheck += 0.2f;
                    var chest = chestBase + new Vector3((float)(rnd.NextDouble() - 0.5) * 0.01f, (float)(rnd.NextDouble() - 0.5) * 0.01f, (float)(rnd.NextDouble() - 0.5) * 0.01f);
                    Vector3 dir = Vector3.Normalize(chest - emPos);
                    if (!sent || (t - sentAt >= 0.5f && UAngle(dir, sentDir) >= 8f))
                    {
                        sent = true; sentDir = dir; sentAt = t; aimDir = dir; aimed = true;
                    }
                }
                // StepLinks の写し
                float ty = 0f, tp = 0f;
                if (aimed)
                {
                    Vector3 w = Wish(f, aimDir, limit);
                    var wobble = EulerU((float)(rnd.NextDouble() - 0.5) * 0.6f, (float)(rnd.NextDouble() - 0.5) * 0.6f, 0f);
                    Vector3 v = Vector3.Transform(ToRest(w), wobble);
                    if (axis)
                    {
                        float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                        if (rho > 0.000001f) { float th = (float)Math.Atan2(rho, v.Z) / Deg; ty = th * v.X / rho; tp = th * v.Y / rho; }
                    }
                    else
                    {
                        ty = (float)Math.Atan2(v.X, v.Z) / Deg;
                        tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                        if (tp > flip || tp < -flip) ty = fy;
                    }
                }
                float dy = ty - fy, dp = tp - fp;
                float left = (float)Math.Sqrt(dy * dy + dp * dp);
                float step = Math.Max(minSpeed, left * smooth) * dt;
                if (left <= step) { fy = ty; fp = tp; } else { fy += dy * step / left; fp += dp * step / left; }
                Quaternion emTurn = axis ? AxisTurn(fy, fp) : EulerU(-fp, fy, 0);
                Vector3 emWorld = ToWorld(Vector3.Transform(Vector3.UnitZ, emTurn));
                if (t > 2f) maxSpeed = Math.Max(maxSpeed, UAngle(prevWorld, emWorld) / dt);
                prevWorld = emWorld;
                if (t >= nextSample) { nextSample += 0.1f; dirs.Add(emWorld); }
            }
            float spread = 0f;
            var last = dirs[dirs.Count - 1];
            for (int i = dirs.Count - 15; i < dirs.Count; i++) spread = Math.Max(spread, UAngle(dirs[i], last));
            lastAngle = UAngle(f, last);
            return spread;
        }
    }
}
