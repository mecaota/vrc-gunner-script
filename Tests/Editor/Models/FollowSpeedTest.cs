// 追従の回る速さの上限(GunnerRig.boneMaxSpeeds)を、動く相手で確かめる。射出口は原点で休みの向き +Z、相手の胸を狙う。
// 追従の式は GunnerRig.StepLinks / StepFollowLimited の写し(なめらかさ 10、最低 30 度/秒、加速の上限 = 上限 / 0.25 秒)。
// 見るもの: 射出口の向きそのものの最大の回る速さ(度/秒)と最大の加速(度/秒²)、相手が止まった後に追いつくか、
// 上限 0(既定)で今の式とビット単位で同じか。
using System;
using System.Numerics;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class FollowSpeedTest
    {
        const float Deg = (float)(Math.PI / 180.0);
        const float Smooth = 10f, MinSpeed = 30f, RampTime = 0.25f, Dt = 1f / 60f;
        const float Tolerance = 1e-4f;   // 度・度/秒。0 と比べる時の許容
        static bool Near(float a, float b) => Math.Abs(a - b) <= Tolerance;

        static float UAngle(Vector3 a, Vector3 b)
        {
            float d = Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b))));
            return (float)Math.Acos(d) / Deg;
        }
        static Quaternion EulerU(float x, float y, float z) =>
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * Deg);
        static Quaternion AxisTurn(float yaw, float pitch)
        {
            float angle = (float)Math.Sqrt(yaw * yaw + pitch * pitch);
            if (angle < 0.000001f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(new Vector3(-pitch / angle, yaw / angle, 0f), angle * Deg);
        }
        static Vector3 Wish(Vector3 w, float limit)
        {
            w = Vector3.Normalize(w);
            if (UAngle(Vector3.UnitZ, w) <= limit) return w;
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, w);
            if (axis.LengthSquared() < 1e-12f) axis = Vector3.UnitY;
            return Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), limit * Deg));
        }

        class Follow
        {
            public float fy, fp, vy, vp;
            public bool axis;
            public float maxSpeed;
            public bool snapped;          // このフレームで目標を越えて(または止まって)目標に合わせた
            public float snapSpeed;       // その直前の速さ(度/秒。止まった時の「止まり方の強さ」)
            public void Step(Vector3 v) { StepDt(v, Dt); }
            public void StepDt(Vector3 v, float dt)
            {
                snapped = false;
                float ty, tp;
                if (axis)
                {
                    float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    ty = 0f; tp = 0f;
                    if (rho > 0.000001f) { float th = (float)Math.Atan2(rho, v.Z) / Deg; ty = th * v.X / rho; tp = th * v.Y / rho; }
                }
                else
                {
                    ty = (float)Math.Atan2(v.X, v.Z) / Deg;
                    tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                    if (tp > 80f || tp < -80f) ty = fy;
                }
                float dy = ty - fy, dp = tp - fp;
                float left = (float)Math.Sqrt(dy * dy + dp * dp);
                if (maxSpeed > 0f)
                {
                    // StepFollowLimited の写し
                    float h = dt < 0.1f ? dt : 0.1f;
                    if (h <= 0f) return;
                    float accel = maxSpeed / RampTime;
                    float want = Math.Min(Math.Max(MinSpeed, left * Smooth), maxSpeed);
                    want = Math.Min(want, (float)Math.Sqrt(2f * accel * left));
                    want = Math.Min(want, left / h);
                    float wy = 0f, wp = 0f;
                    if (left > 0.000001f) { wy = dy / left * want; wp = dp / left * want; }
                    float cy = wy - vy, cp = wp - vp;
                    float change = (float)Math.Sqrt(cy * cy + cp * cp);
                    float most = accel * h;
                    if (change > most) { cy = cy * most / change; cp = cp * most / change; }
                    vy += cy; vp += cp;
                    fy += vy * h; fp += vp * h;
                    float ry = ty - fy, rp = tp - fp;
                    bool passed = ry * dy + rp * dp <= 0f;
                    bool settled = ry * ry + rp * rp < 0.01f * 0.01f && vy * vy + vp * vp <= most * most;
                    if (passed || settled)
                    {
                        float sp = (float)Math.Sqrt(vy * vy + vp * vp);
                        if (passed && sp > most) { snapped = true; snapSpeed = sp; }
                        fy = ty; fp = tp; vy = 0f; vp = 0f;
                    }
                }
                else
                {
                    // 今までの式
                    float step = Math.Max(MinSpeed, left * Smooth) * dt;
                    if (left <= step) { fy = ty; fp = tp; }
                    else { fy = fy + dy * step / left; fp = fp + dp * step / left; }
                }
            }
            public Vector3 Dir => Vector3.Transform(Vector3.UnitZ, axis ? AxisTurn(fy, fp) : EulerU(-fp, fy, 0));
        }

        struct Res { public float maxSpeed, maxAccel, endErr, settle, accelAt, maxStop; public int stops; }

        // path: 時刻 → 相手の胸の位置(射出口から見た)。dur 秒動き、その後 3 秒止まる。
        static Res Run(Func<float, Vector3> path, float dur, float maxSpeed, bool axis, float limit)
        {
            var f = new Follow { maxSpeed = maxSpeed, axis = axis };
            var r = new Res { settle = -1f };
            Vector3 prev = Vector3.UnitZ, prevVel = Vector3.Zero;
            // 最初は止まった相手に追いついた状態から始める
            for (int k = 0; k < 600; k++) f.Step(Wish(path(0f), limit));
            prev = f.Dir;
            float end = dur + 3f;
            int skipAccel = 0;
            for (float t = 0f; t < end; t += Dt)
            {
                Vector3 target = Wish(path(Math.Min(t, dur)), limit);
                f.Step(target);
                Vector3 cur = f.Dir;
                Vector3 vel = (cur - prev) / Dt;                     // 1秒あたりの向きの変化(ラジアン相当)
                float speed = vel.Length() / Deg;
                // 加速は球面に沿う成分だけ(向きの球の上を回る時の、中心へ向かう成分は「急さ」ではないので除く)
                Vector3 acc = (vel - prevVel) / Dt;
                Vector3 n = Vector3.Normalize(cur);
                acc -= Vector3.Dot(acc, n) * n;
                float accel = acc.Length() / Deg;
                r.maxSpeed = Math.Max(r.maxSpeed, speed);
                // 目標に追いついて止まったフレーム(とその次)は、止まり方として別に数える(行き過ぎない代わりに、そこで速さが 0 になる)
                if (f.snapped) { r.stops++; r.maxStop = Math.Max(r.maxStop, f.snapSpeed); skipAccel = 2; }
                if (t > Dt && skipAccel == 0 && accel > r.maxAccel) { r.maxAccel = accel; r.accelAt = t; }
                if (skipAccel > 0) skipAccel--;
                float err = UAngle(cur, target);
                if (t > dur && err < 0.5f && r.settle < 0f) r.settle = t - dur;
                if (t > dur && err >= 0.5f) r.settle = -1f;
                r.endErr = err;
                prev = cur; prevVel = vel;
            }
            return r;
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            // 相手の胸は射出口より 0.8 m 高い、前方の位置
            (string name, Func<float, Vector3> path, float dur, bool axis, float limit)[] cases =
            {
                ("走る相手が 2 m 前を 6 m/秒で横切る", t => new Vector3(-6f + 6f * t, 0.8f, 2f), 2f, false, 60f),
                ("歩く相手が急に向きを変える(1.5 m/秒、0.5 秒で反転)", t => new Vector3(t < 1f ? -1f + 1.5f * t : 0.5f - 1.5f * (t - 1f), 0.8f, 2.5f), 2f, false, 60f),
                ("ジャンプ(2 m 前、0.6 秒で 0.5 m 上がって戻る)", t => new Vector3(0.5f, 0.8f + (t < 0.6f ? 0.5f * (float)Math.Sin(Math.PI * t / 0.6) : 0f), 2f), 1f, false, 60f),
                ("届いた位置が 1 m 跳ぶ(3 m 前)", t => new Vector3(t < 0.5f ? 0f : 1f, 0.8f, 3f), 1f, false, 60f),
                ("届いた位置が 3 m 跳ぶ(3 m 前)", t => new Vector3(t < 0.5f ? -1.5f : 1.5f, 0.8f, 3f), 1f, false, 60f),
                ("後ろ寄りで 3 m 跳ぶ(狙える角度 120、軸と角度)", t => new Vector3(t < 0.5f ? -1.5f : 1.5f, 1.5f, -0.8f), 1f, true, 120f),
            };
            const float Limit = 120f;
            foreach (var c in cases)
            {
                var off = Run(c.path, c.dur, 0f, c.axis, c.limit);
                var on = Run(c.path, c.dur, Limit, c.axis, c.limit);
                // 向きの速さは 2つの値の速さ以下(左右・上下では上を向くほど小さい)。加速は上限の 1.15 倍まで(向きの曲がりのぶんの余裕)
                // 追いついて止まる時の直前の速さは上限の半分(60 度/秒)まで(1フレームで止まっても目立たない大きさ)。回数は参考
                bool ok = on.maxSpeed <= Limit * 1.02f && on.maxAccel <= Limit / RampTime * 1.15f && on.endErr < 0.5f && on.settle >= 0f
                    && on.maxStop <= Limit * 0.5f;
                if (!ok) fails++;
                sb.AppendLine($"{c.name}: 上限 {Limit} 度/秒 → 最大の速さ {on.maxSpeed:F0} 度/秒・最大の加速 {on.maxAccel:F0} 度/秒²・追いついて止まる {on.stops} 回(止まる直前の速さ 最大 {on.maxStop:F0} 度/秒)・止まってから追いつくまで {on.settle:F2} 秒 / 上限なし(今まで) → {off.maxSpeed:F0} 度/秒・{off.maxAccel:F0} 度/秒² {(ok ? "OK" : "NG")}");
            }
            // 上限 0 は今までの式とビット単位で同じ(StepFollowLimited を通らない。同じ相手で今の式を2回走らせて比べる意味は無いので、
            // GunnerRig の分岐の写しが「上限 0 → 今の式」になっていることを、別に書いた今の式と比べる)
            {
                int diff = 0;
                var rnd = new Random(5);
                for (int k = 0; k < 2000; k++)
                {
                    float fy = (float)(rnd.NextDouble() * 120 - 60), fp = (float)(rnd.NextDouble() * 120 - 60);
                    var v = Vector3.Normalize(new Vector3((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() + 0.1)));
                    var f = new Follow { fy = fy, fp = fp, maxSpeed = 0f };
                    f.Step(v);
                    float ty = (float)Math.Atan2(v.X, v.Z) / Deg, tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
                    if (tp > 80f || tp < -80f) ty = fy;
                    float dy = ty - fy, dp = tp - fp, left = (float)Math.Sqrt(dy * dy + dp * dp);
                    float step = Math.Max(MinSpeed, left * Smooth) * Dt;
                    float ey, ep;
                    if (left <= step) { ey = ty; ep = tp; } else { ey = fy + dy * step / left; ep = fp + dp * step / left; }
                    if (f.fy != ey || f.fp != ep) diff++;
                }
                bool ok = diff == 0;
                if (!ok) fails++;
                sb.AppendLine($"上限 0(既定)は今までの式とビット単位で同じ: 2000 通りで違い {diff} 件 {(ok ? "OK" : "NG")}");
            }
            // フレームの刻み(10/20/30/60/90 fps)と、動いている途中の 0.4 秒の処理落ち1回: 60 度先の目標へ回って止まる、
            // 休みへ戻る。行き過ぎ(目標を越えた量)が 0、止まった後の値が目標とちょうど同じ(休みでは 0)であること。
            foreach (float maxSp in new[] { 120f, 180f })
            {
                var line = new StringBuilder();
                bool ok = true;
                foreach (float fps in new[] { 10f, 20f, 30f, 60f, 90f, -60f })
                {
                    bool hitch = fps < 0f;
                    float dt = 1f / Math.Abs(fps);
                    var f = new Follow { maxSpeed = maxSp };
                    Vector3 goal = Vector3.Transform(Vector3.UnitZ, EulerU(0, 60f, 0));
                    float over = 0f;
                    // 回る(3 秒)
                    for (float t = 0f; t < 3f; t += dt)
                    {
                        float step = hitch && t >= 0.2f && t < 0.2f + dt ? 0.4f : dt;
                        f.StepDt(goal, step);
                        over = Math.Max(over, f.fy - (float)Math.Atan2(goal.X, goal.Z) / Deg);
                    }
                    float goalYaw = (float)Math.Atan2(goal.X, goal.Z) / Deg;
                    // 浮動小数の比べには許容を置く(Unity の Mono では中間の精度が違い、0 や同じ値がわずかにずれることがある)
                    bool stopped = Near(f.fy, goalYaw) && Near(f.fp, 0f) && Near(f.vy, 0f) && Near(f.vp, 0f);
                    // 休みへ戻る(3 秒)
                    float overBack = 0f;
                    for (float t = 0f; t < 3f; t += dt)
                    {
                        float step = hitch && t >= 0.2f && t < 0.2f + dt ? 0.4f : dt;
                        f.StepDt(Vector3.UnitZ, step);
                        overBack = Math.Max(overBack, -f.fy);
                    }
                    bool zero = Near(f.fy, 0f) && Near(f.fp, 0f) && Near(f.vy, 0f) && Near(f.vp, 0f);
                    bool k = over <= Tolerance && overBack <= Tolerance && stopped && zero;
                    if (!k) ok = false;
                    line.Append($" {(hitch ? "60fps+処理落ち0.4秒" : fps + "fps")}: 行き過ぎ {Math.Max(over, overBack):F3}度・止まる {(stopped ? "はい" : "いいえ")}・休みで0 {(zero ? "はい" : "いいえ")};");
                }
                if (!ok) fails++;
                sb.AppendLine($"上限 {maxSp} 度/秒のフレームの刻み:{line} {(ok ? "OK" : "NG")}");
            }
            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }
    }
}
