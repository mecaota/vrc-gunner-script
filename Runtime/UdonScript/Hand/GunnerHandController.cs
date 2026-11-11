using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // Controller of the hand-held shooter: switches it on and off, runs the trigger press / release and the collision window,
    // and gathers every way of ending into one place. Has no synced state: the rig syncs the shots and the aim.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerHandController : UdonSharpBehaviour
    {
        public const int CollideAlways = 0, CollideAfterRelease = 1;

        public GunnerRig rig;
        public GunnerPickupInput input;
        public GunnerStickAim aim;                // optional: no stick aim
        public GunnerMoveHold hold;               // optional: the player can walk while shooting

        public int emitter = -1;                  // single shots: -1 = every emitter
        public int payload;                       // passed to the rig as is; what it means is up to the projectile
        // AfterRelease: a held stream passes through everything; on release the first thing its particles touch is hit once.
        public int collideMode = CollideAlways;
        public float releaseWindow = 3f;          // AfterRelease: seconds the released particles may still hit, at most
        public bool followBone;                   // turn the rig's bone link on while held (restored on end)
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
                // No Update will close the window later; give the rig its own setting back now.
                closePending = false;
                if (rig != null) rig._SetCollision(rig.streamCollide);
            }
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            // Update switches it on again on the next frame when the pickup is still held.
            if (Utilities.IsValid(player) && player.isLocal) Deactivate();
        }

        // Called by the input on pick up / drop and every frame here: hand changes and a disabled rig end it too.
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

        // ---------------- Switching ----------------

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

        // Safe to call any number of times. A forced end never hits anything: collision stays off until the particles are gone.
        private void Deactivate()
        {
            if (!active) return;
            active = false;
            triggerDown = false;
            bool wasStreaming = streaming;
            streaming = false;
            if (hold != null) hold._Release();
            if (rig == null) return;
            // The aim goes first, so the stop and bone-off below send "no aim" and remote emitters return to rest.
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

        // Only a stream this client is running: someone who took the rig since is left alone.
        private void StopOwnStream()
        {
            if (Networking.IsOwner(rig.gameObject) && rig._IsStreaming()) rig.StopStream();
        }

        // ---------------- Per frame ----------------

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

        // Closes early once nothing released is in the air, so a later stream from someone else is not caught by it.
        private void StepWindow()
        {
            if (rig == null) { closePending = false; return; }
            if (Time.time < closeAt && rig._IsStreamInFlight()) return;
            closePending = false;
            rig._SetCollision(active && collideMode == CollideAfterRelease ? false : rig.streamCollide);
        }
    }
}
