using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Mecaota.Gunner
{
    // Reads input only: which hand holds the pickup, the aim stick, and the trigger. Must sit on the VRC_Pickup's GameObject,
    // because pickup events reach only the behaviours there.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerPickupInput : UdonSharpBehaviour
    {
        public const int HandLeft = 0, HandRight = 1, HandEither = 2;
        public const int DesktopMoveKeys = 0, DesktopArrowKeys = 1;

        public GunnerHandController controller;
        public VRC_Pickup pickup;                 // empty = the VRC_Pickup on this GameObject
        public int hand = HandLeft;               // VR only: the hand that may shoot. Desktop has one hand.
        public int desktopStick = DesktopMoveKeys;
        public bool invertVertical;

        [System.NonSerialized] public bool held;
        [System.NonSerialized] public float stickX;
        [System.NonSerialized] public float stickY;

        private float moveX, moveY;
        private bool vrKnown, vr;

        private void Start()
        {
            if (pickup == null) pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        }

        // True when the local player holds it in a hand that may shoot (desktop: whenever held).
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

        // Fresh stick values for this frame (the move events arrive only when the value changes).
        public void _ReadStick()
        {
            float x = moveX, y = moveY;
            if (!IsVR() && desktopStick == DesktopArrowKeys)
            {
                x = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                y = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            }
            stickX = x;
            stickY = invertVertical ? -y : y;
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
            moveX = 0f; moveY = 0f;
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

        public override void InputMoveHorizontal(float value, UdonInputEventArgs args) { moveX = value; }

        public override void InputMoveVertical(float value, UdonInputEventArgs args) { moveY = value; }
    }
}
