using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // 射出口1つの設定。GunnerRig が起動時に読む。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerEmitter : UdonSharpBehaviour
    {
        public Transform pivot;                   // +Z = 休み方向
        public ParticleSystem shotParticle;       // World シミュレーション空間
        public int fireMode = GunnerRig.FireSingle;
        public float aimMaxAngle;                 // 0 以下 = GunnerRig.DefaultAimLimit

        public AudioSource fireSource;
        public AudioClip fireClip;
        public float fireMinInterval;
        public float shotSpeed = 25f;
        public float maxRange = 30f;
        public float castRadius = 0.05f;

        public AudioSource impactSource;
        public AudioClip impactClip;
        public float impactMinInterval;
        public ParticleSystem impactParticle;
        public int impactBurst = 1;

        public float streamRate = 12f;
        public int streamHitMode = GunnerStream.HitOnce;
        public float streamHitInterval = 0.25f;
        public int streamCollide = 1;

        public Transform[] linkBones = new Transform[0];  // 根元 → 先端。先端より上は先端の祖先に限る
        public float boneStrength = 1f;
        public float boneYawMax = 30f;
        public float bonePitchUpMax = 30f;
        public float bonePitchDownMax = 15f;
        public float boneSmooth = 10f;
        public float boneMinSpeed = 30f;
        public float boneMaxSpeed;
    }
}
