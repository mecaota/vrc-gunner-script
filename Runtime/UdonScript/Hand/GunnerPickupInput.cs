using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 入力だけを読む: ピックアップを持つ手とトリガー。VRC_Pickup の GameObject に置く必要がある。
    // ピックアップのイベントは、そこにある behaviour にしか届かないため。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerPickupInput : UdonSharpBehaviour
    {
        public const int HandLeft = 0, HandRight = 1, HandEither = 2;

        public GunnerHandController controller;
        public VRC_Pickup pickup;                 // 空 = この GameObject の VRC_Pickup
        public int hand = HandLeft;               // VR だけ: 撃ってよい手。デスクトップは手が1つ

        [System.NonSerialized] public bool held;

        private bool vrKnown, vr;

        private void Start()
        {
            if (pickup == null) pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        }

        // ローカルのプレイヤーが撃ってよい手で持っている時に true(デスクトップ: 持っていればいつでも)。
        public bool _Allowed()
        {
            if (!held || pickup == null) return false;
            VRC_Pickup.PickupHand current = pickup.currentHand;
            if (current == VRC_Pickup.PickupHand.None) return false;
            if (!IsVR()) return true;
            if (hand == HandEither) return true;
            if (hand == HandRight) return current == VRC_Pickup.PickupHand.Right;
            return current == VRC_Pickup.PickupHand.Left;
        }

        private bool IsVR()
        {
            if (vrKnown) return vr;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            vr = local.IsUserInVR();
            vrKnown = true;
            return vr;
        }

        public override void OnPickup()
        {
            held = true;
            if (controller != null) controller._Refresh();
        }

        public override void OnDrop()
        {
            held = false;
            if (controller != null) controller._Refresh();
        }

        public override void OnPickupUseDown()
        {
            if (controller != null) controller._OnUseDown();
        }

        public override void OnPickupUseUp()
        {
            if (controller != null) controller._OnUseUp();
        }
    }
}
