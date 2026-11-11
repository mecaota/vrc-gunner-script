using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // Turns a stick value into aim angles (degrees, yaw right +, pitch up +). Holds no input and talks to no rig.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerStickAim : UdonSharpBehaviour
    {
        public const int ModeRate = 0, ModePosition = 1;

        public int mode = ModeRate;         // Rate: the tilt sets the turn speed and the aim stays put. Position: the tilt is the angle.
        public float turnSpeed = 90f;       // degrees per second at full tilt (Rate)
        public float range = 60f;           // degrees each way
        public float deadZone = 0.15f;
        public float positionEase = 12f;    // Position: per second, share of the remaining angle closed (0 = jump at once)

        [System.NonSerialized] public float yaw;
        [System.NonSerialized] public float pitch;

        public void _Reset()
        {
            yaw = 0f;
            pitch = 0f;
        }

        // True when the angles changed.
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

        // Inside the dead zone is zero; outside it the tilt is rescaled so a small push past the edge is still a small turn.
        private float Shape(float v)
        {
            float dz = Mathf.Clamp(deadZone, 0f, 0.95f);
            float a = Mathf.Abs(v);
            if (a <= dz) return 0f;
            return Mathf.Sign(v) * Mathf.Clamp01((a - dz) / (1f - dz));
        }
    }
}
