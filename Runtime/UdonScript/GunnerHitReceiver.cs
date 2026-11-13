using UdonSharp;

namespace Mecaota.Gunner
{
    // GunnerRig の着弾に反応するものの基底: 必要なものだけ override し、着弾は rig の hit* フィールドから読む。
    public abstract class GunnerHitReceiver : UdonSharpBehaviour
    {
        // true: rig が毎フレーム _OnGunnerLateStep を呼び、そのために hitStart / hitWorld / hitLive を埋める。既定は off。
        // 着弾に反応するだけの受け手しかない rig は、毎フレームの端点の計算をしない。
        public bool wantsLateStep;

        // 持ち主だけ、rig の弾の受け手だけ: 記録を書く直前。rig.hitPayload を変えてよい。
        public virtual void _OnOwnerHit(GunnerRig rig) { }

        // 持ち主だけ、弾の受け手だけ: 記録を送る直前(_RetireHit + _Commit の後にも呼ぶ)。
        public virtual void _OnOwnerCommit(GunnerRig rig) { }

        // 全員: 着弾が届いた(rig.hitReplay = 遅れて入った人のために戻したもの。効果も音も出さない)。
        public virtual void _OnHit(GunnerRig rig) { }

        // 全員: 届いていた着弾が引退した・消された・上書きされた。
        public virtual void _OnHitGone(GunnerRig rig) { }

        // 全員: 単発が射出口を出た時と、連射が始まった時。中身は rig.fireEmitter / fireStream / fireReplay / firePayload で読む。
        // 発射の途中で呼ばれるので、ここから撃ち直したり記録を消したりしない。
        public virtual void _OnFire(GunnerRig rig) { }

        // 全員、毎フレーム1回、rig の PostLateUpdate の最後に(wantsLateStep の時だけ)。
        public virtual void _OnGunnerLateStep(GunnerRig rig) { }

        // 全員: rig の持ち主が変わった(Networking.IsOwner(rig.gameObject) は自分で確かめる)。
        public virtual void _OnGunnerOwner(GunnerRig rig) { }
    }
}
