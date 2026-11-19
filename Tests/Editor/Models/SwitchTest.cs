// 狙える角度 120 の連動で、見張り(向きでの狙い)→ 球(角度での狙い)→ 見張りと替えた時のボーンの動きを確かめる。
// 今の式(ユーザー決定 11-13): 球だけ今までの左右・上下に移し替える(SwitchFollow)。切り替えた瞬間にボーンが1回動くことは許容。
// 比べる式(取り込まない案 a): 球も軸と角度で追う(移し替えが無い)。
// 合格の条件: 1フレームのボーンの跳びが 25 度以内(ねじれの差の最大 約20度+余裕)、球の向きは同じ。
// 見るもの: 1フレームのボーンの回転の変化が、追従の値の動き(と上限の丸め)で説明できる大きさを超えた分(跳び)。
using System;
using System.Numerics;
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class SwitchTest
    {
        const float Deg = (float)(Math.PI / 180.0);

        static float UAngle(Vector3 a, Vector3 b)
        {
            float d = Math.Max(-1f, Math.Min(1f, Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b))));
            return (float)Math.Acos(d) / Deg;
        }
        static float QAngle(Quaternion a, Quaternion b)
        {
            float d = Math.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2f * (float)Math.Acos(Math.Min(1f, d)) / Deg;
        }
        static Quaternion EulerU(float x, float y, float z) =>
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, y * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, x * Deg) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, z * Deg);
        static Vector3 Dir(float yaw, float elev) => Vector3.Transform(Vector3.UnitZ, EulerU(-elev, yaw, 0));
        static Vector3 Wish(Vector3 w, float limit)
        {
            w = Vector3.Normalize(w);
            if (UAngle(Vector3.UnitZ, w) <= limit) return w;
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, w);
            if (axis.LengthSquared() < 1e-12f) axis = Vector3.UnitY;
            return Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), limit * Deg));
        }
        static Quaternion AxisTurn(float yaw, float pitch)
        {
            float angle = (float)Math.Sqrt(yaw * yaw + pitch * pitch);
            if (angle < 0.000001f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(new Vector3(-pitch / angle, yaw / angle, 0f), angle * Deg);
        }
        static void AxisOf(Vector3 v, out float ty, out float tp)
        {
            float rho = (float)Math.Sqrt(v.X * v.X + v.Y * v.Y);
            ty = 0f; tp = 0f;
            if (rho > 0.000001f) { float th = (float)Math.Atan2(rho, v.Z) / Deg; ty = th * v.X / rho; tp = th * v.Y / rho; }
        }
        static void EulerOf(Vector3 v, out float ty, out float tp)
        {
            ty = (float)Math.Atan2(v.X, v.Z) / Deg;
            tp = (float)Math.Atan2(v.Y, Math.Sqrt(v.X * v.X + v.Z * v.Z)) / Deg;
        }

        // 見張りが watchDir を2秒狙い、球がスティック (stickYaw, stickPitch) で2秒狙い、また見張りに戻って2秒。
        // switchRep = 球の間だけ左右・上下に移し替える(取り消した式)。返り値 = 跳びの最大(度)。
        static float Run(Vector3 watchDir, float stickYaw, float stickPitch, int n, bool switchRep, out float ballDirErr)
        {
            float yawLim = 30f * n, upLim = 25f * n, downLim = 12f * n;
            float fy = 0f, fp = 0f, dt = 1f / 60f;
            bool euler = false;
            Quaternion prevBone = Quaternion.Identity;
            float maxJump = 0f;
            ballDirErr = 0f;
            for (int frame = 0; frame < 360; frame++)
            {
                bool ball = frame >= 120 && frame < 240;
                Vector3 v;
                if (ball) v = Wish(Dir(stickYaw, stickPitch), 60f);   // 球: 角度での狙い、min(120, 可動域 60) で切る
                else v = Wish(watchDir, 120f);
                bool wantEuler = switchRep && ball;
                if (wantEuler != euler)
                {
                    // 取り消した SwitchFollow の写し(同じ向きのまま値を移し替える)
                    Vector3 cur = Vector3.Transform(Vector3.UnitZ, euler ? EulerU(-fp, fy, 0) : AxisTurn(fy, fp));
                    euler = wantEuler;
                    if (euler) EulerOf(cur, out fy, out fp); else AxisOf(cur, out fy, out fp);
                }
                float ty, tp;
                if (euler) { EulerOf(v, out ty, out tp); if (tp > 80f || tp < -80f) ty = fy; }
                else AxisOf(v, out ty, out tp);
                float dy = ty - fy, dp = tp - fp;
                float left = (float)Math.Sqrt(dy * dy + dp * dp);
                float step = Math.Max(20f, left * 12f) * dt;
                if (left <= step) { fy = ty; fp = tp; } else { fy += dy * step / left; fp += dp * step / left; }
                float cy = Math.Max(-yawLim, Math.Min(yawLim, fy)), cp = Math.Max(-downLim, Math.Min(upLim, fp));
                Quaternion bone = euler ? EulerU(-cp, cy, 0) : AxisTurn(cy, cp);
                if (n > 1) bone = Quaternion.Slerp(Quaternion.Identity, bone, 1f / n);
                float change = QAngle(prevBone, bone);
                // 追従の値の動きで説明できる大きさ(1フレームの値の動き ÷ ボーンの数 + 余裕)。超えた分が跳び
                float allowed = Math.Min(left, step) / n + 0.05f;
                if (frame > 0) maxJump = Math.Max(maxJump, change - allowed);
                prevBone = bone;
                if (ball && frame == 239)
                {
                    // 球の射出口の向き(追いついた後)を、今までの左右・上下の式の向きと比べる
                    Vector3 em = Vector3.Transform(Vector3.UnitZ, euler ? EulerU(-fp, fy, 0) : AxisTurn(fy, fp));
                    EulerOf(v, out float ey, out float ep);
                    ballDirErr = UAngle(em, Vector3.Transform(Vector3.UnitZ, EulerU(-ep, ey, 0)));
                }
            }
            return Math.Max(0f, maxJump);
        }

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            (string name, Vector3 dir)[] watch =
            {
                ("休みから 60 度(斜め)", Dir(45f, 45f)), ("休みから 90 度(横)", Dir(90f, 0f)),
                ("休みから 120 度(後ろ上)", Wish(Dir(170f, 50f), 120f)), ("背中の上(真後ろ上 仰角 65)", Dir(180f, 65f)),
            };
            (float y, float p)[] sticks = { (0f, 0f), (60f, 60f), (-40f, 20f) };
            foreach (int n in new[] { 1, 3 })
                foreach (var w in watch)
                {
                    float worstNew = 0f, worstOld = 0f, worstDir = 0f;
                    foreach (var st in sticks)
                    {
                        worstNew = Math.Max(worstNew, Run(w.dir, st.y, st.p, n, true, out float dirErr));
                        worstOld = Math.Max(worstOld, Run(w.dir, st.y, st.p, n, false, out _));
                        worstDir = Math.Max(worstDir, dirErr);
                    }
                    bool ok = worstNew < 25f && worstDir < 0.03f;
                    if (!ok) fails++;
                    sb.AppendLine($"ボーン{n}本・見張り {w.name} → 球 → 見張り: 切り替えの瞬間のボーンの動き 最大 {worstNew:F1} 度(許容。案 a なら {worstOld:F2} 度)、球の向きの差 最大 {worstDir:F3} 度 {(ok ? "OK" : "NG")}");
                }
            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }
    }
}
