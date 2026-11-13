using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 手持ちの発射器の本体: 入り切り、トリガーの押す・放す、当たり判定の窓を受け持ち、
    // 終わり方をすべて1か所に集める。同期する状態は持たない: 弾と狙いは rig が同期する。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerHandController : UdonSharpBehaviour
    {
        public const int CollideAlways = 0, CollideAfterRelease = 1;

        public GunnerRig rig;
        public GunnerPickupInput input;
        public GunnerStickAim aim;                // 任意: 無ければスティックで狙わない
        public GunnerMoveHold hold;               // 任意: 撃っている間もプレイヤーが歩ける

        public int emitter = -1;                  // 単発: -1 = 全部の射出口
        public int payload;                       // rig へそのまま渡す。意味は弾が決める
        // AfterRelease: 押している間の連射は何でも素通りし、放すとその粒子が最初に触れたものに1回当たる。
        public int collideMode = CollideAlways;
        public float releaseWindow = 3f;          // AfterRelease: 放した粒子がまだ当たってよい秒数の上限
        public bool followBone;                   // 持っている間は rig の骨の連動を on にする(終わったら戻す)
        public bool resetAimOnEnd = true;

        private bool active;
        private bool triggerDown;
        private bool streaming;
        private bool aimSent;
        private bool boneChanged;
        private bool closePending;
        private float closeAt;

        private void Update()
        {
            _Refresh();
            if (active) StepAim();
            if (closePending) StepWindow();
        }

        private void OnDisable()
        {
            Deactivate();
            if (closePending)
            {
                // 後で窓を閉じる Update が無いので、rig の設定を今すぐ戻す。
                closePending = false;
                if (rig != null) rig._SetCollision(rig.streamCollide);
            }
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            // まだ持っていれば、次のフレームに Update が入れ直す。
            if (Utilities.IsValid(player) && player.isLocal) Deactivate();
        }

        // 拾う・落とす時に入力から、またここで毎フレーム呼ばれる。持つ手が変わった時や rig が無効になった時も終える。
        public void _Refresh()
        {
            bool want = Want();
            if (want == active) return;
            if (want) Activate();
            else Deactivate();
        }

        public void _OnUseDown()
        {
            _Refresh();
            if (!active || triggerDown) return;
            triggerDown = true;
            if (rig.fireMode != GunnerRig.FireStream)
            {
                rig._FireWith(emitter, payload);
                return;
            }
            if (collideMode == CollideAfterRelease)
            {
                closePending = false;
                rig._SetCollision(false);
            }
            rig._StartStreamWith(payload);
            streaming = rig._IsStreaming();
        }

        public void _OnUseUp()
        {
            if (!triggerDown) return;
            triggerDown = false;
            if (!streaming) return;
            streaming = false;
            if (rig == null) return;
            StopOwnStream();
            if (collideMode == CollideAfterRelease) OpenWindow(true);
        }

        public bool _IsActive() { return active; }

        // ---------------- 入り切り ----------------

        private bool Want()
        {
            return rig != null && rig.gameObject.activeInHierarchy && input != null && input._Allowed();
        }

        private void Activate()
        {
            active = true;
            triggerDown = false;
            aimSent = false;
            if (hold != null) hold._Hold();
            if (aim != null) aim._Reset();
            if (followBone && !rig._IsBoneFollowing())
            {
                rig._SetBoneFollow(true);
                boneChanged = rig._IsBoneFollowing();
            }
            if (collideMode == CollideAfterRelease && !closePending) rig._SetCollision(false);
        }

        // 何回呼んでもよい。強制的な終わりでは何にも当てない: 粒子が消えるまで当たり判定は off のまま。
        private void Deactivate()
        {
            if (!active) return;
            active = false;
            triggerDown = false;
            bool wasStreaming = streaming;
            streaming = false;
            if (hold != null) hold._Release();
            if (rig == null) return;
            // 狙いを先に外す。下の停止と骨の連動 off が「狙いなし」を送り、ほかの人の画面の射出口が休みに戻る。
            if (resetAimOnEnd) rig.ResetAim();
            if (wasStreaming)
            {
                StopOwnStream();
                if (collideMode == CollideAfterRelease) OpenWindow(false);
            }
            else if (collideMode == CollideAfterRelease && !closePending) rig._SetCollision(rig.streamCollide);
            if (boneChanged)
            {
                boneChanged = false;
                if (Networking.IsOwner(rig.gameObject)) rig._SetBoneFollow(false);
            }
        }

        // この端末が動かしている連射だけ: その後で rig を取った人のものには触らない。
        private void StopOwnStream()
        {
            if (Networking.IsOwner(rig.gameObject) && rig._IsStreaming()) rig.StopStream();
        }

        // ---------------- 毎フレーム ----------------

        private void StepAim()
        {
            if (aim == null || input == null) return;
            input._ReadStick();
            bool changed = aim._Step(input.stickX, input.stickY, Time.deltaTime);
            if (!changed && aimSent) return;
            rig._SetAimAngles(aim.yaw, aim.pitch);
            aimSent = true;
        }

        private void OpenWindow(bool collide)
        {
            rig._SetCollision(collide);
            closePending = true;
            closeAt = Time.time + Mathf.Max(releaseWindow, 0.1f);
        }

        // 放したものが何も飛んでいなければ早めに閉じる。後でほかの人が始めた連射を拾わないように。
        private void StepWindow()
        {
            if (rig == null) { closePending = false; return; }
            if (Time.time < closeAt && rig._IsStreamInFlight()) return;
            closePending = false;
            rig._SetCollision(active && collideMode == CollideAfterRelease ? false : rig.streamCollide);
        }
    }
}
