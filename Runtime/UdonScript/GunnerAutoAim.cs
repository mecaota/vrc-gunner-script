using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 足元が範囲(BoxCollider)の中にいる人を、rig が自動で狙って撃つ。GunnerRig と同じ GameObject には置かない。
    // 探して撃つのは rig の持ち主の端末だけ(_FireWith は呼んだ端末が rig を取るので、各自が撃つと取り合いになる)。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerAutoAim : UdonSharpBehaviour
    {
        private const float MinEyeHeight = 0.2f;

        public GunnerRig rig;                     // 対象の rig
        public BoxCollider zone;                  // 範囲の箱。空 = 同じ GameObject の BoxCollider
        public float interval = 0.2f;             // 探して向ける間隔(秒)
        public float fireInterval = 3f;           // 撃つ間隔(秒)
        public float aimHeight = 0.7f;            // 狙う高さ(相手の目の高さに対する割合)
        public int emitterMask = -1;              // 狙わせる射出口の組(ビット i = 射出口 i。-1 = 全部)
        public int[] firePayloads;                // 射出口ごとに撃つ時に渡す整数。空・短い所は 0

        private VRCPlayerApi[] players = new VRCPlayerApi[16];
        private float nextTickAt;
        private bool wasOwner;
        private bool aiming;            // 持ち主: この端末が rig の狙いを書いた(撃つ合間に1回戻す)
        private float lastFireAt = -1000f;

        private void Start()
        {
            if (zone == null) zone = GetComponent<BoxCollider>();
        }

        private void OnDisable()
        {
            // 端末内の狙いを戻すだけ(rig は取らず、同期もしない)。持ち主なら、戻した狙いは rig が次の送信で伝える。
            if (aiming && rig != null) StopAim();
            aiming = false;
            // 有効に戻った時も、持ち主になった瞬間と同じく fireInterval を待ってから撃つ。
            wasOwner = false;
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextTickAt) return;
            nextTickAt = now + Mathf.Max(interval, 0.02f);
            if (rig == null || zone == null) return;

            bool owner = Networking.IsOwner(rig.gameObject);
            if (owner && !wasOwner)
            {
                // 前の持ち主の狙いが残っていることがあるので、交代の直後に1回戻す。
                aiming = true;
                // 交代の直後は撃たず、fireInterval を待つ(前の持ち主の直前の1発と続けて撃たないように)。
                lastFireAt = now;
            }
            if (!owner)
            {
                // 持ち主でない端末は、同期する操作(撃つ、rig を取る)をしない。
                // 持ち主だった時に書いた狙いが端末内に残っていると、新しい持ち主の同期した狙いより優先されてしまうので、
                // 端末内の値だけ戻す(ResetAim と _SetAimEmitters は rig を取らず、同期もしない)。
                if (aiming) StopAim();
                wasOwner = false;
                aiming = false;
                return;
            }
            wasOwner = true;
            Step(now);
        }

        private void Step(float now)
        {
            // 撃つ前のアニメを待っている間は、狙いを保つ。
            if (rig._IsPreFiring()) return;
            // 撃つ前の1〜2 tick から狙い始め、間隔が来た tick で撃つ(ボーンが向く時間を取る。フレームのずれで狙う tick が抜けないよう、窓は tick 2つ分)。それ以外は休みの向きへ戻す。
            float waited = now - lastFireAt;
            int e = -1;
            Vector3 point = Vector3.zero;
            if (waited >= fireInterval - 2f * Mathf.Max(interval, 0.02f))
            {
                // 1. 足元が範囲の中にいる人のうち、範囲の中心に一番近い人。
                Vector3 center = zone.transform.TransformPoint(zone.center);
                VRCPlayerApi target = null;
                float best = float.MaxValue;
                int count = VRCPlayerApi.GetPlayerCount();
                if (players.Length < count) players = new VRCPlayerApi[count + 8];
                VRCPlayerApi.GetPlayers(players);
                for (int i = 0; i < count; i++)
                {
                    VRCPlayerApi p = players[i];
                    if (!Utilities.IsValid(p)) continue;
                    Vector3 pos = p.GetPosition();
                    if (!InZone(pos)) continue;
                    float d = (pos - center).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    target = p;
                }
                // 2. 狙う点から一番近い射出口(emitterMask の組の中)。壁と射程は見ない。
                if (target != null)
                {
                    float eye = Mathf.Max(target.GetAvatarEyeHeightAsMeters(), MinEyeHeight);
                    point = target.GetPosition() + Vector3.up * (eye * aimHeight);
                    e = NearestEmitter(point);
                }
            }
            if (e < 0)
            {
                if (aiming) StopAim();
                aiming = false;
                return;
            }

            // 3. その射出口だけを狙う組に入れ、狙う点へ向ける。間隔が来ていれば撃つ(この順で呼ぶ)。
            rig._SetAimEmitters(1 << e);
            rig._SetAimTarget(point);
            aiming = true;
            if (waited < fireInterval) return;
            lastFireAt = now;
            rig._FireWith(e, firePayloads != null && e < firePayloads.Length ? firePayloads[e] : 0);
        }

        private int NearestEmitter(Vector3 point)
        {
            Transform[] list = rig.emitters;
            if (list == null) return -1;
            int count = Mathf.Min(list.Length, rig._EmitterCount());
            int pick = -1;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (list[i] == null || (emitterMask & (1 << i)) == 0) continue;
                float d = (list[i].position - point).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                pick = i;
            }
            return pick;
        }

        // 箱の座標系で比べるので、回転や拡大縮小が掛かっていても動く。
        private bool InZone(Vector3 position)
        {
            Vector3 p = zone.transform.InverseTransformPoint(position) - zone.center;
            Vector3 h = zone.size * 0.5f;
            return Mathf.Abs(p.x) <= h.x && Mathf.Abs(p.y) <= h.y && Mathf.Abs(p.z) <= h.z;
        }

        // 狙いを外して休みの向きへ戻す。
        private void StopAim()
        {
            rig.ResetAim();
            rig._SetAimEmitters(-1);
        }
    }
}
