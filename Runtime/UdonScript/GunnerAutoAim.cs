using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 範囲(トリガーの BoxCollider)に入った人を、rig が自動で狙って撃つ。
    // 付け方: トリガーのコライダーと同じ GameObject に付ける。GunnerRig と同じ GameObject には置かない。
    // 範囲内の一覧は全端末が持つ(トリガーの知らせはどの端末にも全プレイヤー分届く)。探して向けて撃つのは rig の持ち主の端末だけ
    // (_FireWith は呼んだ端末が rig を取るので、各自が撃つと取り合いになる)。同期もネットワークイベントも持たない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerAutoAim : UdonSharpBehaviour
    {
        // 一覧に持てる人数の上限。足りない時は、それより後に入った人を一覧に入れない。
        private const int MaxListed = 128;
        // 位置の検算で「外す」時だけ箱をこれだけ広げて見る(メートル)。トリガーは体のカプセルが触れた時点で入り、
        // 検算は足元の1点を見るため。ゆとりが無いと、縁にいる人を外した後に Enter がもう一度来ず、中にいるのに一覧から消える。
        // 「足す」時のゆとりは 0(足元が箱の中にある人だけ)。
        private const float RemoveSlack = 0.75f;
        private const float MinEyeHeight = 0.2f;

        public GunnerRig rig;                     // 対象の rig
        public BoxCollider zone;                  // 範囲の箱。空 = 同じ GameObject の BoxCollider
        public float interval = 0.2f;             // 探して向ける間隔(秒)
        public float fireInterval = 3f;           // 撃つ間隔(秒)
        public float aimHeight = 0.7f;            // 狙う高さ(相手の目の高さに対する割合)
        public float switchWait = 0.5f;           // 射出口を替えてから、次に替えてよくなるまでの秒数
        public int[] firePayloads;                // 射出口ごとに撃つ時に渡す整数。空・短い所は 0
        public float recheckInterval = 1f;        // 一覧を位置から直す間隔(秒)。0 以下 = 直さない(持ち主になった時の1回だけ)

        // ---------------- 端末ごとの状態 ----------------

        private int[] listed = new int[MaxListed];   // 範囲内の playerId。0..listedCount-1 が有効。重複なし
        private int listedCount;
        private VRCPlayerApi[] players = new VRCPlayerApi[16];

        private float nextTickAt;
        private float nextRecheckAt;
        private bool wasOwner;
        private bool aiming;            // 持ち主: この端末が rig の狙いを書いた(一覧が空になった時に1回戻す)
        private int current = -1;       // 持ち主: 今使っている射出口
        private float switchedAt = -1000f;
        private float lastFireAt = -1000f;

        private void Start()
        {
            if (zone == null) zone = GetComponent<BoxCollider>();
        }

        private void OnEnable()
        {
            // 止まっていた間の出入りは届いていないので、次の Update ですぐに検算する。
            nextTickAt = 0f;
            nextRecheckAt = 0f;
        }

        private void OnDisable()
        {
            // 端末内の狙いを戻すだけ(rig は取らず、同期もしない)。持ち主なら、戻した狙いは rig が次の送信で伝える。
            if (aiming && rig != null) StopAim();
            aiming = false;
            current = -1;
            // 有効に戻った時に「持ち主になった瞬間」として検算し直す(recheckInterval が 0 以下でも取りこぼさない)。
            wasOwner = false;
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextTickAt) return;
            nextTickAt = now + Mathf.Max(interval, 0.02f);
            if (rig == null) return;

            bool owner = Networking.IsOwner(rig.gameObject);
            // 持ち主になった瞬間に1回検算する(前の持ち主から引き継いだ一覧の取りこぼしを直す)。
            if (owner && !wasOwner)
            {
                Recheck();
                // 前の持ち主の狙いが残っていることがあるので、一覧が空なら1回戻す。
                aiming = true;
                current = -1;
                // 交代の直後は撃たず、fireInterval を待つ(前の持ち主の直前の1発と続けて撃たないように)。
                lastFireAt = now;
            }
            else if (recheckInterval > 0f && now >= nextRecheckAt)
            {
                nextRecheckAt = now + recheckInterval;
                Recheck();
            }
            if (!owner)
            {
                // 持ち主でない端末は、同期する操作(撃つ、rig を取る)をしない。
                // 持ち主だった時に書いた狙いが端末内に残っていると、新しい持ち主の同期した狙いより優先されてしまうので、
                // 端末内の値だけ戻す(ResetAim と _SetAimEmitters は rig を取らず、同期もしない)。
                if (aiming) StopAim();
                wasOwner = false;
                aiming = false;
                current = -1;
                return;
            }
            wasOwner = true;
            Step(now);
        }

        // ================= 一覧(全端末) =================

        public override void OnPlayerTriggerEnter(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Add(player.playerId);
        }

        public override void OnPlayerTriggerExit(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Remove(player.playerId);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Remove(player.playerId);
        }

        // リスポーンした本人の端末にだけ届く。ほかの端末の一覧は、位置の検算(recheckInterval)で直る。
        // リスポーン地点が箱の中なら、次の検算で足し直す。
        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player)) return;
            Remove(player.playerId);
        }

        private void Add(int id)
        {
            for (int i = 0; i < listedCount; i++) if (listed[i] == id) return;
            if (listedCount >= listed.Length) return;
            listed[listedCount] = id;
            listedCount++;
        }

        private void Remove(int id)
        {
            for (int i = 0; i < listedCount; i++)
            {
                if (listed[i] != id) continue;
                RemoveAt(i);
                return;
            }
        }

        // 末尾と入れ替えて外す(順番は保たない)。
        private void RemoveAt(int i)
        {
            listedCount--;
            listed[i] = listed[listedCount];
            listed[listedCount] = 0;
        }

        // テレポート、リスポーン、入室直後、椅子に座っている間は、入った・出たの知らせが来ないことがある。
        // 位置から一覧を直す: はっきり外にいる人(RemoveSlack の外)を外し、足元が箱の中にいる人を足す。
        private void Recheck()
        {
            if (zone == null) return;
            for (int i = listedCount - 1; i >= 0; i--)
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(listed[i]);
                if (!Utilities.IsValid(p) || !InZone(p.GetPosition(), RemoveSlack)) RemoveAt(i);
            }
            int count = VRCPlayerApi.GetPlayerCount();
            if (players.Length < count) players = new VRCPlayerApi[count + 8];
            VRCPlayerApi.GetPlayers(players);
            int n = Mathf.Min(count, players.Length);
            for (int i = 0; i < n; i++)
            {
                VRCPlayerApi p = players[i];
                if (!Utilities.IsValid(p)) continue;
                if (InZone(p.GetPosition(), 0f)) Add(p.playerId);
            }
        }

        // 箱の座標系で、半分の大きさ(slack メートルだけ広げる)と軸ごとに比べる。拡大縮小が掛かっていても動く。
        private bool InZone(Vector3 position, float slack)
        {
            Transform t = zone.transform;
            Vector3 p = t.InverseTransformPoint(position) - zone.center;
            Vector3 s = t.lossyScale;
            Vector3 size = zone.size;
            float hx = Mathf.Max(size.x * 0.5f + slack / Mathf.Max(Mathf.Abs(s.x), 0.01f), 0f);
            float hy = Mathf.Max(size.y * 0.5f + slack / Mathf.Max(Mathf.Abs(s.y), 0.01f), 0f);
            float hz = Mathf.Max(size.z * 0.5f + slack / Mathf.Max(Mathf.Abs(s.z), 0.01f), 0f);
            return Mathf.Abs(p.x) <= hx && Mathf.Abs(p.y) <= hy && Mathf.Abs(p.z) <= hz;
        }

        private Vector3 ZoneCenter()
        {
            if (zone == null) return transform.position;
            return zone.transform.TransformPoint(zone.center);
        }

        // ================= 探して撃つ(rig の持ち主だけ) =================

        private void Step(float now)
        {
            // 撃つ前のアニメを待っている間は、射出口を替えず、撃たない。
            if (rig._IsPreFiring()) return;
            // 1. 一覧の中で、範囲の中心に一番近い人。rig の excludeOwner が真なら、この端末の本人(= rig の持ち主)は
            //    弾が当たらないので選ばない。
            Vector3 center = ZoneCenter();
            bool skipLocal = rig.excludeOwner;
            VRCPlayerApi target = null;
            float best = float.MaxValue;
            for (int i = 0; i < listedCount; i++)
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(listed[i]);
                if (!Utilities.IsValid(p)) continue;
                if (skipLocal && p.isLocal) continue;
                float d = (p.GetPosition() - center).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                target = p;
            }
            if (target == null)
            {
                if (aiming) StopAim();
                aiming = false;
                current = -1;
                return;
            }

            // 2. 狙う点から一番近い射出口。壁と射程は見ない。替えてから switchWait 秒は替えない。
            float eye = Mathf.Max(target.GetAvatarEyeHeightAsMeters(), MinEyeHeight);
            Vector3 point = target.GetPosition() + Vector3.up * (eye * aimHeight);
            int e = NearestEmitter(point);
            if (e < 0) return;
            if (e != current)
            {
                if (current >= 0 && ValidEmitter(current) && now - switchedAt < switchWait) e = current;
                else
                {
                    current = e;
                    switchedAt = now;
                }
            }

            // 3. その射出口だけを狙う組に入れ、狙う点へ向け、間隔が空いていれば1発撃つ(この順で呼ぶ)。
            rig._SetAimEmitters(1 << e);
            rig._SetAimTarget(point);
            aiming = true;
            if (now - lastFireAt < fireInterval) return;
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
                if (list[i] == null) continue;
                float d = (list[i].position - point).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                pick = i;
            }
            return pick;
        }

        private bool ValidEmitter(int e)
        {
            Transform[] list = rig.emitters;
            return list != null && e >= 0 && e < list.Length && e < rig._EmitterCount() && list[e] != null;
        }

        // 狙いを外して休みの向きへ戻す。
        private void StopAim()
        {
            rig.ResetAim();
            rig._SetAimEmitters(-1);
        }
    }
}
