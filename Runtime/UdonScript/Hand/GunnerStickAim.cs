using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // スティックの値を狙いの角度(度。左右は右が +、上下は上が +)に変える。入力は持たず、rig とも話さない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerStickAim : UdonSharpBehaviour
    {
        public const int ModeRate = 0, ModePosition = 1;

        public int mode = ModeRate;         // Rate: 傾きで回る速さが決まり、狙いはその場に残る。Position: 傾きがそのまま角度。
        public float turnSpeed = 90f;       // 傾けきった時の1秒あたりの角度(Rate)
        public float range = 60f;           // 片側の角度
        public float deadZone = 0.15f;
        public float positionEase = 12f;    // Position: 1秒あたり、残りの角度を詰める割合(0 = すぐに飛ぶ)

        [System.NonSerialized] public float yaw;
        [System.NonSerialized] public float pitch;

        public void _Reset()
        {
            yaw = 0f;
            pitch = 0f;
        }

        // 角度が変わった時に true。
        public bool _Step(float x, float y, float dt)
        {
            float lastYaw = yaw, lastPitch = pitch;
            float limit = Mathf.Max(0f, range);
            float ax = Shape(x), ay = Shape(y);
            if (mode == ModePosition)
            {
                float ty = ax * limit, tp = ay * limit;
                if (positionEase <= 0f) { yaw = ty; pitch = tp; }
                else
                {
                    float k = 1f - Mathf.Exp(-positionEase * dt);
                    yaw = Mathf.Lerp(yaw, ty, k);
                    pitch = Mathf.Lerp(pitch, tp, k);
                }
            }
            else
            {
                yaw += ax * turnSpeed * dt;
                pitch += ay * turnSpeed * dt;
            }
            yaw = Mathf.Clamp(yaw, -limit, limit);
            pitch = Mathf.Clamp(pitch, -limit, limit);
            return yaw != lastYaw || pitch != lastPitch;
        }

        // 不感帯の中は0。外は傾きを伸ばし直し、端を少し越えた押し込みは小さな回転のままにする。
        private float Shape(float v)
        {
            float dz = Mathf.Clamp(deadZone, 0f, 0.95f);
            float a = Mathf.Abs(v);
            if (a <= dz) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - dz) / (1f - dz));
        }
    }
}
