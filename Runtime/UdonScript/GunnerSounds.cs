using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Mecaota.Gunner
{
    // 受け手の見本: 発射音と着弾音。クリップが空ならその音は鳴らさない。同期は持たず、各端末が届いた出来事から鳴らす。
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class GunnerSounds : GunnerHitReceiver
    {
        public GunnerRig[] rigs;                  // 開始時にこの全部へ登録する。1つで複数の rig を受け持てる
        public AudioClip fireClip;
        public AudioClip impactClip;
        public float fireVolume = 1f;
        public float impactVolume = 1f;
        public float pitchJitter = 0.05f;         // 同じ音が続けて並んだ時の単調さを和らげる。0 で切
        // 音源はエディタのメニューが子に作る。Udon は実行時に VRC_SpatialAudioSource を足せないため
        public AudioSource[] fireVoices;
        public AudioSource[] impactVoices;
        public float fireMinInterval = 0.05f;     // 同じフレームの一斉射撃を1回にまとめる
        public float impactMinInterval = 0.08f;   // 当たり続ける連射で鳴りすぎないように
        public float streamFireInterval = 0f;     // 0 = 連射の発射音は撃ち始めの1回だけ
        public bool impactOnStream = true;
        public bool onWorld = true;
        public bool onMoving = true;
        public bool onPlayers = true;
        public float cullDistance = 0f;           // 0 = 音源の maxDistance(メニューは Far と同じ値にする)。遠い音で近い音の枠を奪わないように

        // 間引きの時計は rig ごと(rigs と同じ番号。最後の1つは rigs に無い rig の分)。別々の rig の同時の発射を1回にまとめないため
        private float[] lastFire;
        private float[] lastImpact;
        private float[] fireStarted;
        private float[] impactStarted;
        private bool[] repeatOn;                  // rigs と同じ番号。連射の発射音を繰り返している rig
        private bool repeatRunning;
        private Vector3 foundPoint;               // EmitterPoint の結果(U# のメソッドは out 引数を持てない)

        private void Start()
        {
            if (fireVoices == null) fireVoices = new AudioSource[0];
            if (impactVoices == null) impactVoices = new AudioSource[0];
            if (rigs == null) rigs = new GunnerRig[0];
            fireStarted = new float[fireVoices.Length];
            impactStarted = new float[impactVoices.Length];
            repeatOn = new bool[rigs.Length];
            lastFire = new float[rigs.Length + 1];
            lastImpact = new float[rigs.Length + 1];
            for (int i = 0; i <= rigs.Length; i++)
            {
                lastFire[i] = -1000f;
                lastImpact[i] = -1000f;
            }
            for (int i = 0; i < rigs.Length; i++) if (rigs[i] != null) rigs[i]._Register(this);
        }

        private void OnDisable()
        {
            StopAll(fireVoices);
            StopAll(impactVoices);
            if (repeatOn != null) for (int i = 0; i < repeatOn.Length; i++) repeatOn[i] = false;
        }

        public override void _OnFire(GunnerRig rig)
        {
            if (rig == null || fireStarted == null) return;
            if (fireClip == null) return;
            if (rig.fireStream && streamFireInterval > 0f) StartRepeat(rig);
            // 入室時に続いていた連射は今撃たれたのではないので、繰り返しだけ始めて最初の1回は鳴らさない。
            if (rig.fireReplay) return;
            int clock = ClockOf(rig);
            if (Time.time - lastFire[clock] < fireMinInterval || !EmitterPoint(rig, rig.fireEmitter)) return;
            if (Play(fireVoices, fireStarted, fireClip, fireVolume, foundPoint)) lastFire[clock] = Time.time;
        }

        public override void _OnHit(GunnerRig rig)
        {
            // 遅れて入った人に戻される古い記録や、相手が見つからない記録では鳴らさない(見た目のしぶきと同じ)。
            if (rig == null || impactStarted == null || rig.hitReplay || !rig.hitResolved) return;
            if (rig.hitStream && !impactOnStream) return;
            int kind = rig.hitKind;
            if (kind == GunnerRig.KindWorld && !onWorld) return;
            if (kind == GunnerRig.KindMoving && !onMoving) return;
            if (kind == GunnerRig.KindPlayer && !onPlayers) return;
            int clock = ClockOf(rig);
            if (Time.time - lastImpact[clock] < impactMinInterval) return;
            if (Play(impactVoices, impactStarted, impactClip, impactVolume, rig.hitPoint)) lastImpact[clock] = Time.time;
        }

        private int ClockOf(GunnerRig rig)
        {
            for (int i = 0; i < rigs.Length; i++) if (rigs[i] == rig) return i;
            return rigs.Length;
        }

        // ---------------- 連射中の繰り返し ----------------

        private void StartRepeat(GunnerRig rig)
        {
            int at = -1;
            for (int i = 0; i < rigs.Length && i < repeatOn.Length; i++) if (rigs[i] == rig) { at = i; break; }
            if (at < 0) return;
            repeatOn[at] = true;
            if (repeatRunning) return;
            repeatRunning = true;
            SendCustomEventDelayedSeconds(nameof(_RepeatFire), streamFireInterval);
        }

        public void _RepeatFire()
        {
            bool any = false;
            if (enabled && gameObject.activeInHierarchy && streamFireInterval > 0f)
            {
                for (int i = 0; i < rigs.Length; i++)
                {
                    if (!repeatOn[i]) continue;
                    GunnerRig rig = rigs[i];
                    if (rig == null || !rig._IsStreaming())
                    {
                        repeatOn[i] = false;
                        continue;
                    }
                    any = true;
                    if (!EmitterPoint(rig, -1) || Time.time - lastFire[i] < fireMinInterval) continue;
                    if (Play(fireVoices, fireStarted, fireClip, fireVolume, foundPoint)) lastFire[i] = Time.time;
                }
            }
            repeatRunning = any;
            if (any) SendCustomEventDelayedSeconds(nameof(_RepeatFire), streamFireInterval);
        }

        // ---------------- 鳴らす ----------------

        // e < 0 は先頭の有効な射出口。
        private bool EmitterPoint(GunnerRig rig, int e)
        {
            Transform[] list = rig.emitters;
            if (list == null) return false;
            int count = Mathf.Min(list.Length, rig._EmitterCount());
            if (e >= 0)
            {
                if (e >= count || list[e] == null) return false;
                foundPoint = list[e].position;
                return true;
            }
            for (int i = 0; i < count; i++)
            {
                if (list[i] == null) continue;
                foundPoint = list[i].position;
                return true;
            }
            return false;
        }

        // 鳴っていない音源を先に使い、全部鳴っていればいちばん古い音を止めて使う。
        // PlayOneShot は使わない。1つの音源に重ねると、音ごとに位置を変えられないため。
        private bool Play(AudioSource[] voices, float[] started, AudioClip clip, float volume, Vector3 point)
        {
            if (clip == null || voices == null || voices.Length == 0) return false;
            int pick = -1;
            int oldest = -1;
            for (int i = 0; i < voices.Length && i < started.Length; i++)
            {
                AudioSource v = voices[i];
                if (v == null) continue;
                if (!v.isPlaying) { pick = i; break; }
                if (oldest < 0 || started[i] < started[oldest]) oldest = i;
            }
            if (pick < 0) pick = oldest;
            if (pick < 0) return false;
            AudioSource voice = voices[pick];
            if (OutOfReach(voice, point)) return false;
            voice.Stop();
            voice.transform.position = point;
            voice.clip = clip;
            voice.volume = volume;
            voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            voice.Play();
            started[pick] = Time.time;
            return true;
        }

        private bool OutOfReach(AudioSource voice, Vector3 point)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            float reach = cullDistance > 0f ? cullDistance : voice.maxDistance;
            if (reach <= 0f) return false;
            Vector3 head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            return (point - head).sqrMagnitude > reach * reach;
        }

        private void StopAll(AudioSource[] voices)
        {
            if (voices == null) return;
            for (int i = 0; i < voices.Length; i++) if (voices[i] != null) voices[i].Stop();
        }
    }
}
