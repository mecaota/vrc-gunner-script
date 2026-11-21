using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // 1つの GunnerRig の連射: 連射の粒子を出し、その経路を持ち、当たる所を見つける(持ち主)。
    // rig の PostLateUpdate → _StepMasked からだけ動かされる。自分のイベントは持たない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerStream : UdonSharpBehaviour
    {
        // HitOnce: 最初の接触で着弾を1つ記録し、その射出口の飛んでいる粒子を消す
        // その射出口は、連射をやり直すか当たり判定をもう一度 on にするまで記録しない。トリガーを押している間は出し続ける。
        public const int HitOnce = 0;
        public const int HitKeep = 1;

        private const int CatchUpMax = 3;        // 引っかかりの後、1フレームに出す数の上限(まとめて出さない)

        // 射出口ごと、見た目のスクリプト用: 一番遠い連射の粒子が飛んだメートル、連射の粒子が1つでも飛んでいるか。
        [System.NonSerialized] public float[] reach;
        [System.NonSerialized] public bool[] active;

        private GunnerRig rig;
        private bool ready;
        private int count;
        private int cap;
        private Transform[] emitters;
        private ParticleSystem[] systems;
        private bool[] flightOn;               // rig の配列: その間は単発が粒子を使う
        // 射出口ごと(rig の値を _Init で写す)
        private float[] speed;
        private float[] range;
        private float[] life;
        private float[] interval;
        private float[] hitInterval;
        private int[] hitMode;

        private float[] nextEmitAt;
        private float[] nextHitAt;
        private float[] checkFrom;
        private bool[] look;
        private bool[] baseCollision;
        private bool[] latched;
        private int[] head;
        private Vector3[] rOrigin;
        private Vector3[] rDir;
        private float[] rTime;
        private bool[] rAlive;

        public void _Init(GunnerRig owner)
        {
            if (ready || owner == null) return;
            ready = true;
            rig = owner;
            emitters = rig.emitters;
            systems = rig.shotParticles;
            flightOn = rig.flightOn;
            count = rig._EmitterCount();
            speed = new float[count];
            range = new float[count];
            life = new float[count];
            interval = new float[count];
            hitInterval = new float[count];
            hitMode = new int[count];
            cap = 4;
            for (int e = 0; e < count; e++)
            {
                speed[e] = rig._Speed(e);
                range[e] = Mathf.Max(rig._Range(e), 0.01f);
                life[e] = range[e] / speed[e];
                interval[e] = 1f / Mathf.Max(rig._StreamRate(e), 0.1f);
                hitMode[e] = rig._StreamHitMode(e);
                hitInterval[e] = Mathf.Max(rig._StreamHitInterval(e), 0.02f);
                cap = Mathf.Max(cap, Mathf.CeilToInt(life[e] / interval[e]) + 2);
            }
            cap = Mathf.Min(cap, 128);

            reach = new float[count];
            active = new bool[count];
            nextEmitAt = new float[count];
            nextHitAt = new float[count];
            checkFrom = new float[count];
            look = new bool[count];
            baseCollision = new bool[count];
            latched = new bool[count];
            head = new int[count];
            rOrigin = new Vector3[count * cap];
            rDir = new Vector3[count * cap];
            rTime = new float[count * cap];
            rAlive = new bool[count * cap];
            for (int e = 0; e < count; e++)
            {
                ParticleSystem ps = Shot(e);
                if (ps == null) continue;
                ParticleSystem.CollisionModule collision = ps.collision;
                baseCollision[e] = collision.enabled;
            }
        }

        // 連射を今(また)始める: 今から出す。どの射出口もまた当たってよい
        // (当たり判定を切り替えない rig は、最初の HitOnce の接触の後ずっと止まったままになってしまうため)。
        public void _Begin(float now)
        {
            if (!ready) return;
            for (int e = 0; e < count; e++)
            {
                nextEmitAt[e] = now;
                latched[e] = false;
                checkFrom[e] = now;
                nextHitAt[e] = now;
            }
        }

        // 当たり判定 off → on: 各射出口がまた当たってよい。粒子の今の位置から数える。
        public void _ResetCollision(float now)
        {
            if (!ready) return;
            for (int e = 0; e < count; e++)
            {
                latched[e] = false;
                checkFrom[e] = now;
                nextHitAt[e] = now;
            }
        }

        // 作ったときの単発の設定へ戻す(同じ粒子で単発を撃つ前に rig が呼ぶ)。
        public void _Restore(int e)
        {
            if (!ready || e < 0 || e >= count) return;
            ParticleSystem ps = Shot(e);
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Forget(e);
            if (!look[e]) return;
            look[e] = false;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = baseCollision[e];
        }

        // 射出口 e の連射の粒子を消す(HitOnce の接触、StopFire、ClearHits)。
        public void _Clear(int e)
        {
            if (!ready || e < 0 || e >= count || !Any(e)) return;
            ParticleSystem ps = Shot(e);
            if (ps != null && !flightOn[e]) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Forget(e);
        }

        public void _ClearAll()
        {
            for (int e = 0; e < count; e++) _Clear(e);
        }

        public bool _InFlight(float now)
        {
            if (!ready) return false;
            for (int k = 0; k < rAlive.Length; k++) if (rAlive[k] && (now - rTime[k]) * speed[k / cap] <= range[k / cap]) return true;
            return false;
        }

        // 全員、rig の PostLateUpdate から(狙いの後): 出す、経路を古くする。持ち主で
        // 当たり判定が on なら、経路が最初に当たった所を記録する。
        // mask = 粒子を出す射出口の組(ビット e = 射出口 e)。組の外の射出口は新しく出さないが、
        // もう飛んでいる粒子は今までどおり進み、当たる。collide = 当たりを記録する射出口の組(0 = どれも記録しない)。
        public void _StepMasked(float now, bool streaming, bool owner, int collide, int mask)
        {
            if (!ready) return;
            for (int e = 0; e < count; e++)
            {
                if (emitters[e] == null) continue;
                bool emitHere = streaming && (mask & (1 << e)) != 0;
                if (emitHere && flightOn[e]) nextEmitAt[e] = now; // 単発が粒子を使っている。追いつかずに再開する
                else if (emitHere)
                {
                    int k = 0;
                    while (now >= nextEmitAt[e] && k < CatchUpMax) { Emit(e, now); nextEmitAt[e] += interval[e]; k++; }
                    if (now >= nextEmitAt[e]) nextEmitAt[e] = now + interval[e];
                }
                else if (streaming) nextEmitAt[e] = now; // 組の外: 組に戻った時に追いつきで続けて出さない
                Age(e, now);
                if (owner && (collide & (1 << e)) != 0 && active[e]) Collide(e, now);
                checkFrom[e] = now;
            }
        }

        // 射程を越えた経路を捨てる。reach / active を埋める。
        private void Age(int e, float now)
        {
            int at = e * cap;
            float farthest = 0f;
            bool any = false;
            for (int r = 0; r < cap; r++)
            {
                int q = at + r;
                if (!rAlive[q]) continue;
                float flown = (now - rTime[q]) * speed[e];
                if (flown > range[e]) { rAlive[q] = false; continue; }
                if (flown > farthest) farthest = flown;
                any = true;
            }
            reach[e] = farthest;
            active[e] = any;
        }

        // 持ち主: 生きている経路ごとに、前に確かめた後に飛んだ区間を判定する。時間で一番早い接触を採る。
        private void Collide(int e, float now)
        {
            if (hitMode[e] == HitOnce && latched[e]) return;
            if (hitMode[e] == HitKeep && now < nextHitAt[e]) return;
            int at = e * cap;
            int bestQ = -1;
            float bestWhen = 1e30f;
            for (int r = 0; r < cap; r++)
            {
                int q = at + r;
                if (!rAlive[q]) continue;
                float a = (Mathf.Max(checkFrom[e], rTime[q]) - rTime[q]) * speed[e];
                float b = Mathf.Min((now - rTime[q]) * speed[e], range[e]);
                if (b - a <= 0.0001f) continue;
                if (!rig._CastPath(e, rOrigin[q] + rDir[q] * a, rDir[q], b - a)) continue;
                float when = rTime[q] + (a + rig._CastDistance()) / speed[e];
                if (when >= bestWhen) continue;
                bestWhen = when;
                bestQ = q;
                // rig の判定の結果は次の判定で上書きされる: 一番よいものを今 rig に残させる。
                rig._KeepCast();
            }
            if (bestQ < 0) return;
            rig._StreamHit(e);
            if (hitMode[e] == HitOnce)
            {
                latched[e] = true;
                _Clear(e);
            }
            else
            {
                rAlive[bestQ] = false;
                nextHitAt[e] = now + hitInterval[e];
            }
        }

        // 射出口の今の姿勢(このフレームで rig が狙わせた)から粒子を1つ。
        private void Emit(int e, float now)
        {
            ParticleSystem ps = Shot(e);
            if (ps == null) return;
            if (!look[e]) UseStreamLook(e, ps);
            Transform m = emitters[e];
            Vector3 origin = m.position;
            Vector3 dir = m.forward;
            ps.transform.SetPositionAndRotation(origin, m.rotation);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeedMultiplier = speed[e];
            main.startLifetimeMultiplier = life[e];
            if (!ps.isPlaying) ps.Play(true);
            ps.Emit(1);
            int k = e * cap + head[e];
            head[e] = (head[e] + 1) % cap;
            rOrigin[k] = origin; rDir[k] = dir; rTime[k] = now; rAlive[k] = true;
        }

        private void UseStreamLook(int e, ParticleSystem ps)
        {
            look[e] = true;
            // 粒子は Emit からだけ出す。Play で作ったときのバーストを出してはいけない。
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            // HitOnce: 接触は持ち主の判定で決める。作ったときの当たり判定だと、撃つ人の体で粒子が消えてしまう。
            // HitKeep: 作ったときの当たり判定を残し、連射(水)が壁で自然に終わるようにする。
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = hitMode[e] == HitKeep && baseCollision[e];
            ParticleSystem.MainModule main = ps.main;
            if (main.maxParticles < cap + 1) main.maxParticles = cap + 1;
        }

        private void Forget(int e)
        {
            int at = e * cap;
            for (int i = 0; i < cap; i++) rAlive[at + i] = false;
            active[e] = false;
            reach[e] = 0f;
        }

        private bool Any(int e)
        {
            int at = e * cap;
            for (int i = 0; i < cap; i++) if (rAlive[at + i]) return true;
            return false;
        }

        private ParticleSystem Shot(int e) { return systems != null && e >= 0 && e < systems.Length ? systems[e] : null; }
    }
}
