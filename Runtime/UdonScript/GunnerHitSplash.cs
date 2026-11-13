using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // 受け手の見本: 着弾した所で飛び散りの粒子を弾けさせる(弾、水)。弾の中身は何も知らない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerHitSplash : GunnerHitReceiver
    {
        public GunnerRig targetRig;                 // Start で自分を登録する(エディタが rig.receivers に入れておいてもよい)
        public ParticleSystem splash;         // World シミュレーション空間。Transform の +Z = 飛び散る向き
        public int burst = 12;
        public bool onWorld = true;
        public bool onMoving = true;
        public bool onPlayers = true;
        public bool onStream = true;          // HitKeep の連射は一定間隔で届く: 見本では単発だけにしたいこともある
        public AudioSource sound;             // 任意。着弾で鳴らす
        public float surfaceOffset = 0.02f;   // 面から離すメートル。面の中で弾けないように

        private const float NormalProbe = 0.3f;

        private void Start()
        {
            if (targetRig != null) targetRig._Register(this);
        }

        public override void _OnHit(GunnerRig rig)
        {
            // 遅れて入った人には古い着弾が戻される: それに飛び散りを出すと、ずっと後に弾けてしまう。
            if (rig == null || rig.hitReplay || !rig.hitResolved || splash == null) return;
            if (rig.hitStream && !onStream) return;
            int kind = rig.hitKind;
            if (kind == GunnerRig.KindWorld && !onWorld) return;
            if (kind == GunnerRig.KindMoving && !onMoving) return;
            if (kind == GunnerRig.KindPlayer && !onPlayers) return;

            Vector3 point = rig.hitPoint;
            Vector3 incoming = Vector3.forward;
            if (rig.hitEmitter >= 0 && rig.hitEmitter < rig.emitters.Length && rig.emitters[rig.hitEmitter] != null)
            {
                Vector3 to = point - rig.emitters[rig.hitEmitter].position;
                if (to.sqrMagnitude > 0.000001f) incoming = to.normalized;
            }
            // ここでまた面が見つかれば面で跳ね返す。体や探りが外れた時は、弾の来た向きへ飛び散らせる。
            Vector3 normal = -incoming;
            RaycastHit hit;
            if (kind != GunnerRig.KindPlayer
                && Physics.Raycast(point - incoming * NormalProbe, incoming, out hit, NormalProbe * 2f, rig.hitMask.value, QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                point = hit.point;
            }
            splash.transform.SetPositionAndRotation(point + normal * surfaceOffset, Quaternion.LookRotation(normal));
            splash.Emit(Mathf.Max(1, burst));
            if (sound != null)
            {
                sound.transform.position = point;
                sound.Play();
            }
        }
    }
}
