// 狙える角度が 90 度を超える連動の追従を、動く相手で確かめる(休みの座標系、ボーン1本、なめらかさ 12、最低 20 度/秒、狙える角度 120)。
// 3つの式を比べる: 直す前(左右・上下)、頭越しの案(第5回、不合格)、軸と角度(第5回の2往復目、採用)。
// 見るもの: 射出口の向きそのもの(世界方向)の最大の速さ(度/秒、最初の寄りの後)、狙いとのずれ、20 度を超えるずれの回数、
// 上の軸(ロール)の最大の速さ、最後の 1.5 秒の向きの幅(終わりの向きからの最大の角度)。
using System;
using System.Numerics;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class SwingTest
    {
        const float Deg = (float)(Math.PI / 180.0);
        const int Before = 0, OverTop = 1, Axis = 2;

        static float UAngle(Vector3 a, Vector3 b)
        {
            float d = Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b))));
            return (float)Math.Acos(d) / Deg;
        }
        static Quaternion EulerU(float x, float y, float z) =>
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * Deg);
        static Vector3 Dir(float yaw, float elev) => Vector3.Transform(Vector3.UnitZ, EulerU(-elev, yaw, 0));
        static Vector3 Wish(Vector3 w, float limit)
        {
            w = Vector3.Normalize(w);
            float a = UAngle(Vector3.UnitZ, w);
            if (a <= limit) return w;
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, w);
            if (axis.LengthSquared() < 1e-12f) axis = Vector3.UnitY;
            return Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), limit * Deg));
        }
        // GunnerRig.AxisTurn の写し
        static Quaternion AxisTurn(float yaw, float pitch)
        {
            float angle = (float)Math.Sqrt(yaw * yaw + pitch * pitch);
            if (angle < 0.000001f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(new Vector3(-pitch / angle, yaw / angle, 0f), angle * Deg);
        }

        struct Result { public float maxErr, maxSpeed, maxRollSpeed, endSpread; public int big; }

        static Result Run(Func<float, Vector3> path, float dur, int method)
        {
            float fy = 0f, fp = 0f, dt = 1f / 60f;
            var r = new Result();
            Vector3 prev = Vector3.UnitZ, prevUp = Vector3.UnitY;
            bool inBig = false;
            var tail = new System.Collections.Generic.List<Vector3>();
            float end = dur + 3f;
            for (float t = 0f; t < end; t += dt)
            {
                Vector3 v = Wish(path(Math.Min(1f, t / dur)), 120f);
                float ty, tp;
                if (method == Axis)
                {
                    float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    ty = 0f; tp = 0f;
                    if (rho > 0.000001f) { float th = (float)Math.Atan2(rho, v.Z) / Deg; ty = th * v.X / rho; tp = th * v.Y / rho; }
                }
                else
                {
                    ty = (float)Math.Atan2(v.X, v.Z) / Deg;
                    tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                    if (method == OverTop && v.Z < 0f)
                    {
                        float oy = (float)Math.Atan2(-v.X, -v.Z) / Deg;
                        float op = tp >= 0f ? 180f - tp : -180f - tp;
                        if (Math.Abs(oy - fy) + Math.Abs(op - fp) < Math.Abs(ty - fy) + Math.Abs(tp - fp)) { ty = oy; tp = op; }
                    }
                    float pole = Math.Abs(tp); if (pole > 90f) pole = 180f - pole;
                    if (method == OverTop ? pole > 80f : (tp > 80f || tp < -80f)) ty = fy;
                }
                float dy = ty - fy, dp = tp - fp;
                float left = (float)Math.Sqrt(dy * dy + dp * dp);
                float step = Math.Max(20f, left * 12f) * dt;
                if (left <= step) { fy = ty; fp = tp; } else { fy += dy * step / left; fp += dp * step / left; }
                // 射出口の向きと上の軸(GunnerRig.AimEmitter の写し)
                Vector3 em, up;
                if (method == Axis)
                {
                    var q = AxisTurn(fy, fp);
                    em = Vector3.Transform(Vector3.UnitZ, q);
                    up = Vector3.Transform(Vector3.UnitY, q);
                }
                else
                {
                    var q = EulerU(-fp, fy, 0);
                    em = Vector3.Transform(Vector3.UnitZ, q);
                    up = (fp > 80f || fp < -80f) ? Vector3.Transform(Vector3.UnitY, q)
                        : Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, em) * em);
                }
                float err = UAngle(em, v);
                if (t > 1.5f)
                {
                    r.maxErr = Math.Max(r.maxErr, err);
                    r.maxSpeed = Math.Max(r.maxSpeed, UAngle(prev, em) / dt);
                    r.maxRollSpeed = Math.Max(r.maxRollSpeed, UAngle(prevUp, up) / dt);
                    if (err > 20f && !inBig) { r.big++; inBig = true; }
                    if (err < 5f) inBig = false;
                }
                if (t > end - 1.5f) tail.Add(em);
                prev = em; prevUp = up;
            }
            foreach (var d in tail) r.endSpread = Math.Max(r.endSpread, UAngle(d, tail[tail.Count - 1]));
            return r;
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            // A: 背中の上(真後ろ・仰角 60)→ 左後ろ仰角 30 → 水平に回って左前 45 度
            Func<float, Vector3> a = s =>
            {
                Vector3 d;
                if (s < 0.3f) { float u = s / 0.3f; d = Dir(180f - 40f * u + 0.01f, 60f - 30f * u); }
                else { float w = (s - 0.3f) / 0.7f; d = Dir(140f - 185f * w, 30f); }
                return new Vector3(-d.X, d.Y, d.Z);
            };
            // B: 真後ろの上(仰角 65)で左右に ±3 度(元の揺れの条件)
            Func<float, Vector3> b = s => Dir(180f + 3f * (float)Math.Sin(s * 40f), 65f);
            // C: 背中の上から左横(仰角 30)へ降り、前後に ±10 度行き来(z = 0 をまたぐ)
            Func<float, Vector3> c = s =>
            {
                if (s < 0.2f) { float u = s / 0.2f; return Dir(-(180f - 90f * u) - 0.01f, 60f - 30f * u); }
                return Dir(-90f + 10f * (float)Math.Sin((s - 0.2f) * 20f), 30f);
            };
            // D: 真後ろの至近に当たる向き(休みから約 110 度・真上寄り)で ±3 度
            Func<float, Vector3> d = s => Dir(180f + 3f * (float)Math.Sin(s * 30f), 70f + 3f * (float)Math.Cos(s * 23f));
            // E: 前の普通の相手(休みから 40 度以内)をゆっくり左右に動かす(既定の範囲)
            Func<float, Vector3> e = s => Dir(-30f + 60f * s, 10f);
            (string name, Func<float, Vector3> path, float dur)[] cases =
            {
                ("背中の上→左横→左前", a, 8f), ("真後ろ上 仰角65 で ±3度", b, 8f), ("背中の上→左横で前後に ±10度", c, 10f),
                ("真後ろ寄りの真上 ±3度", d, 8f), ("前の相手を左右へ", e, 6f),
            };
            string[] names = { "直す前", "頭越しの案", "軸と角度" };
            foreach (var cs in cases)
            {
                var res = new Result[3];
                for (int m = 0; m < 3; m++) res[m] = Run(cs.path, cs.dur, m);
                var line = new StringBuilder();
                for (int m = 0; m < 3; m++)
                    line.Append($" [{names[m]}] ずれ最大 {res[m].maxErr:F0}度・速さ最大 {res[m].maxSpeed:F0}度/秒・ロールの速さ最大 {res[m].maxRollSpeed:F0}度/秒・20度超え {res[m].big}回・終わりの幅 {res[m].endSpread:F1}度;");
                // 合格: 軸と角度で、最初の寄りの後に 20 度を超えるずれが無く、射出口とロールの速さが 300 度/秒以下、終わりの幅 3 度以下
                var ax = res[Axis];
                bool ok = ax.big == 0 && ax.maxSpeed <= 300f && ax.maxRollSpeed <= 300f && ax.endSpread <= 3f;
                if (!ok) fails++;
                sb.AppendLine($"{cs.name}:{line} {(ok ? "OK" : "NG")}");
            }

            // 参考: 狙える角度 120 の連動の球を軸と角度で追った場合との比べ。球は今までの左右・上下の式のまま(ユーザー決定 11-13)なので、
            // ここは「もし軸と角度にしたら」向きは同じでも、ボーンと射出口のねじれが斜めでどれだけ変わるかを出す。
            {
                float maxDirDiff = 0f, maxRoll = 0f;
                for (float y = -60f; y <= 60f; y += 5f)
                    for (float p = -60f; p <= 60f; p += 5f)
                    {
                        Vector3 v = Wish(Dir(y, p), 60f);
                        // 左右・上下の式
                        float ey = (float)Math.Atan2(v.X, v.Z) / Deg, ep = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                        var qe = EulerU(-ep, ey, 0);
                        // 軸と角度
                        float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                        float ay = 0f, ap = 0f;
                        if (rho > 1e-6f) { float th = (float)Math.Atan2(rho, v.Z) / Deg; ay = th * v.X / rho; ap = th * v.Y / rho; }
                        var qa = AxisTurn(ay, ap);
                        maxDirDiff = Math.Max(maxDirDiff, UAngle(Vector3.Transform(Vector3.UnitZ, qe), Vector3.Transform(Vector3.UnitZ, qa)));
                        maxRoll = Math.Max(maxRoll, UAngle(Vector3.Transform(Vector3.UnitY, qe), Vector3.Transform(Vector3.UnitY, qa)));
                    }
                bool ok = maxDirDiff < 0.05f;
                if (!ok) fails++;
                sb.AppendLine($"参考(球を軸と角度にした場合): 射出口の向きの差 最大 {maxDirDiff:F3}度、ねじれの差 最大 {maxRoll:F1}度(球は今までの式なので、実際には変わらない) {(ok ? "OK" : "NG")}");
            }
            // 扇のずらしは射出口のローカル(右 = +X)で掛かる。休みの後ろ側の上・下を狙っても、射出口の右が休みの右と逆にならないこと。
            {
                int flipped = 0, flippedBefore = 0, total = 0;
                for (float yaw = -180f; yaw <= 180f; yaw += 10f)
                    for (float elev = -80f; elev <= 80f; elev += 10f)
                    {
                        Vector3 v = Wish(Dir(yaw, elev), 120f);
                        // 休みの後ろ側の、上か下寄り(|y| > |x|。左右・上下の式で頭越しにロールしていた所)だけ。
                        // 横寄りの後ろは休みの右と狙いの向きがほぼ平行になり、右の比べ方が決まらないので数えない(ロールの速さは上の動く相手で見る)。
                        if (v.Z >= 0f || Math.Abs(v.Y) <= Math.Abs(v.X)) continue;
                        total++;
                        float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                        float th = (float)Math.Atan2(rho, v.Z) / Deg;
                        var qa = AxisTurn(th * v.X / rho, th * v.Y / rho);
                        // 右の軸を、狙いの向きに垂直な面で休みの右と比べる
                        Vector3 right = Vector3.Transform(Vector3.UnitX, qa);
                        Vector3 restRight = Vector3.Normalize(Vector3.UnitX - Vector3.Dot(Vector3.UnitX, v) * v);
                        if (restRight.LengthSquared() > 0.01f && Vector3.Dot(right, restRight) < 0f) flipped++;
                        float ey = (float)Math.Atan2(v.X, v.Z) / Deg, ep = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                        Vector3 rightBefore = Vector3.Transform(Vector3.UnitX, EulerU(-ep, ey, 0));
                        if (restRight.LengthSquared() > 0.01f && Vector3.Dot(rightBefore, restRight) < 0f) flippedBefore++;
                    }
                bool ok = flipped == 0;
                if (!ok) fails++;
                sb.AppendLine($"休みの後ろ側を狙う時の射出口の右(扇の左右): 軸と角度で逆になる向き {flipped}/{total}(参考: 左右・上下では {flippedBefore}) {(ok ? "OK" : "NG")}");
            }
            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }
    }
}
