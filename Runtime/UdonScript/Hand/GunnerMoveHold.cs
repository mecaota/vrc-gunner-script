using UdonSharp;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // Keeps the local player in place while shooting. Uses VRChat's Immobilize only: it leaves walk and run speeds alone, so
    // scripts that read those speeds (e.g. "stuck in something" checks) are not fooled.
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerMoveHold : UdonSharpBehaviour
    {
        public bool holdWhileActive = true;

        // While true, _Release leaves Immobilize on. Immobilize is a single flag, not a count, so releasing it here would also
        // free a player that something else (a seat, a trap) is holding; that something writes this flag.
        public bool releaseBlocked;

        [System.NonSerialized] public bool holding;    // this component turned Immobilize on and has not given it back

        public void _Hold()
        {
            if (!holdWhileActive || holding) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            local.Immobilize(true);
            holding = true;
        }

        public void _Release()
        {
            if (!holding) return;
            holding = false;
            if (releaseBlocked) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local)) local.Immobilize(false);
        }
    }
}
