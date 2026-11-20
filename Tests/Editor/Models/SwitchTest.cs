// 狙える角度 120 度の連動で、見張り → 球 → 見張りと狙いを切り替えた時のボーンの動きを確かめる。
// 見張りも球も点での狙いなので、どちらも軸と角度で追い、切り替えても表し方は変わらない。
// 合格の条件: 1フレームあたりのボーンの跳びが 0.5 度未満で、追いついた後の球の向きが狙いと一致すること。
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
        // 見張りが watchDir を2秒狙い、球が ballDir を2秒狙い、また見張りに戻って2秒。返り値 = 跳びの最大(度)。
        static float Run(Vector3 watchDir, Vector3 ballDir, int n, out float ballDirErr)
        {
            float yawLim = 30f * n, upLim = 25f * n, downLim = 12f * n;
            float fy = 0f, fp = 0f, dt = 1f / 60f;
            Quaternion prevBone = Quaternion.Identity;
            float maxJump = 0f;
            ballDirErr = 0f;
            for (int frame = 0; frame < 360; frame++)
            {
                bool ball = frame >= 120 && frame < 240;
                Vector3 v = Wish(ball ? ballDir : watchDir, 120f);
                AxisOf(v, out float ty, out float tp);
                float dy = ty - fy, dp = tp - fp;
                float left = (float)Math.Sqrt(dy * dy + dp * dp);
                float step = Math.Max(20f, left * 12f) * dt;
                if (left <= step) { fy = ty; fp = tp; } else { fy += dy * step / left; fp += dp * step / left; }
                float cy = Math.Max(-yawLim, Math.Min(yawLim, fy)), cp = Math.Max(-downLim, Math.Min(upLim, fp));
                Quaternion bone = AxisTurn(cy, cp);
                if (n > 1) bone = Quaternion.Slerp(Quaternion.Identity, bone, 1f / n);
                float change = QAngle(prevBone, bone);
                // 追従の値の動きで説明できる大きさ(1フレームの値の動き ÷ ボーンの数 + 余裕)。超えた分が跳び
                float allowed = Math.Min(left, step) / n + 0.05f;
                if (frame > 0) maxJump = Math.Max(maxJump, change - allowed);
                prevBone = bone;
                if (ball && frame == 239)
                {
                    Vector3 em = Vector3.Transform(Vector3.UnitZ, AxisTurn(fy, fp));
                    ballDirErr = UAngle(em, v);
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
            Vector3[] balls = { Dir(0f, 0f), Dir(60f, 60f), Dir(-40f, 20f) };
            foreach (int n in new[] { 1, 3 })
                foreach (var w in watch)
                {
                    float worstJump = 0f, worstDir = 0f;
                    foreach (var b in balls)
                    {
                        worstJump = Math.Max(worstJump, Run(w.dir, b, n, out float dirErr));
                        worstDir = Math.Max(worstDir, dirErr);
                    }
                    bool ok = worstJump < 0.5f && worstDir < 0.03f;
                    if (!ok) fails++;
                    sb.AppendLine($"ボーン{n}本・見張り {w.name} → 球 → 見張り: ボーンの跳び 最大 {worstJump:F2} 度、球の向きの差 最大 {worstDir:F3} 度 {(ok ? "OK" : "NG")}");
                }
            sb.AppendLine(fails == 0 ? "全部 OK" : $"NG {fails} 件");
            return sb.ToString();
        }
    }
}
