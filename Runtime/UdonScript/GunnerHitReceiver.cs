using UdonSharp;

namespace Mecaota.Gunner
{
    // Base for anything that reacts to a GunnerRig's hits: override only what you need, read the hit from the rig's hit* fields.
    public abstract class GunnerHitReceiver : UdonSharpBehaviour
    {
        // True: the rig calls _OnGunnerLateStep every frame and fills hitStart / hitWorld / hitLive for it. Off by default so a
        // rig whose receivers only react to hits does no per-frame end-point work.
        public bool wantsLateStep;

        // Owner only, on the rig's projectile receiver only: the record is about to be written. rig.hitPayload may be changed.
        public virtual void _OnOwnerHit(GunnerRig rig) { }

        // Owner only, on the projectile receiver only: right before the records go out (also after _RetireHit + _Commit).
        public virtual void _OnOwnerCommit(GunnerRig rig) { }

        // Every client: a hit arrived (rig.hitReplay = restored for a late joiner: no effects or sounds).
        public virtual void _OnHit(GunnerRig rig) { }

        // Every client: a hit that had arrived was retired, cleared or overwritten.
        public virtual void _OnHitGone(GunnerRig rig) { }

        // 全員: 単発が射出口を出た時と、連射が始まった時。中身は rig.fireEmitter / fireStream / fireReplay で読む。
        // 発射の途中で呼ばれるので、ここから撃ち直したり記録を消したりしない。
        public virtual void _OnFire(GunnerRig rig) { }

        // Every client, once per frame, at the end of the rig's PostLateUpdate (wantsLateStep only).
        public virtual void _OnGunnerLateStep(GunnerRig rig) { }

        // Every client: the rig's owner changed (check Networking.IsOwner(rig.gameObject) yourself).
        public virtual void _OnGunnerOwner(GunnerRig rig) { }
    }
}
