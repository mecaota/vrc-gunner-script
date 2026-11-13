using UdonSharp;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 撃っている間、ローカルのプレイヤーをその場に留める。VRChat の Immobilize だけを使い、歩く・走る速さは変えないので、
    // その速さを読むスクリプト(例:「何かに捕まっている」の判定)を惑わせない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerMoveHold : UdonSharpBehaviour
    {
        public bool holdWhileActive = true;

        // true の間、_Release は Immobilize を on のままにする。Immobilize は回数ではなく1つのフラグなので、ここで外すと
        // ほかのもの(座席、罠)が留めているプレイヤーまで放してしまう。そのほかのものがこのフラグを書く。
        public bool releaseBlocked;

        [System.NonSerialized] public bool holding;    // この部品が Immobilize を on にして、まだ戻していない

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
