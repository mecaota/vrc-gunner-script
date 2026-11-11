using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // Sample receiver: bursts a splash particle system where a hit lands (bullets, water). Knows nothing about the projectile.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerHitSplash : GunnerHitReceiver
    {
        public GunnerRig targetRig;                 // registers itself on Start (the editor may also list it in rig.receivers)
        public ParticleSystem splash;         // World simulation space; +Z of its Transform = the splash direction
        public int burst = 12;
        public bool onWorld = true;
        public bool onMoving = true;
        public bool onPlayers = true;
        public bool onStream = true;          // HitKeep streams arrive every interval: a sample may want only single shots
        public AudioSource sound;             // optional, played at the hit
        public float surfaceOffset = 0.02f;   // metres off the surface, so the burst is not born inside it

        private const float NormalProbe = 0.3f;

        private void Start()
        {
            if (targetRig != null) targetRig._Register(this);
        }

        public override void _OnHit(GunnerRig rig)
        {
            // A late joiner gets old hits restored: a splash for those would play long after the fact.
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
            // Bounce off the surface when it can be found again here; a body or a miss of the probe splashes back along the shot.
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
