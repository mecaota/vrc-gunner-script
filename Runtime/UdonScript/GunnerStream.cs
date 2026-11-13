using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // 1つの GunnerRig の連射: 連射の粒子を出し、その経路を持ち、当たる所を見つけ(持ち主)、トレイルをつなぐ。
    // rig からだけ動かされる(Update → _PreSimulate、PostLateUpdate → _Step)。自分のイベントは持たない。
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerStream : UdonSharpBehaviour
    {
        public const int JoinParticles = 0;
        public const int JoinLine = 1;
        // HitOnce: 最初の接触で着弾を1つ記録し、その射出口の飛んでいる粒子を消す。その射出口は、連射をやり直すか
        // 当たり判定をもう一度 on にするまで記録しない。トリガーを押している間は出し続ける。
        public const int HitOnce = 0;
        public const int HitKeep = 1;

        private const int CatchUpMax = 3;        // 引っかかりの後、1フレームに出す数の上限(まとめて出さない)
        private const float FrameMax = 0.1f;     // トレイルの重なりに数えるフレーム時間の上限
        private const float FrameEase = 0.05f;   // フレームごと: 追っているフレーム時間が下がって戻る速さ
        private const float OverlapMin = 0.02f;  // つないだトレイルが1つ前の粒子より先へ伸びる最低の秒数
        private const float OverlapFrames = 2f;  // ... と最低このフレーム数(生まれる時とトレイルの点はフレームに乗る)
        private const float StallShare = 1.5f;   // 生まれる間隔がこの何倍より開いたら止まったとみなす

        // 射出口ごと、見た目のスクリプト用: 一番遠い連射の粒子が飛んだメートル、つないだトレイル1本の長さのメートル、
        // 連射の粒子が1つでも飛んでいるか。
        [System.NonSerialized] public float[] reach;
        [System.NonSerialized] public float[] span;
        [System.NonSerialized] public bool[] active;

        private GunnerRig rig;
        private bool ready;
        private int count;
        private int cap;
        private Transform[] emitters;
        private ParticleSystem[] systems;
        private bool[] flightOn;               // rig の配列: その間は単発が粒子を使う
        private float speed;
        private float range;
        private float life;
        private float interval;
        private float width;
        private float hitInterval;
        private AnimationCurve taper;
        private int join;
        private int hitMode;

        private float[] nextEmitAt;
        private float[] nextHitAt;
        private float[] checkFrom;
        private float[] trailShare;
        private float[] baseTrailLife;
        private bool[] look;
        private bool[] baseCollision;
        private bool[] latched;
        private ParticleSystem.MinMaxCurve[] baseTrailWidth;
        private int[] head;
        private Vector3[] rOrigin;
        private Vector3[] rDir;
        private float[] rTime;
        private bool[] rAlive;
        private float resumeAt = -100f;
        private float frameDt = 1f / 60f;

        public void _Init(GunnerRig owner)
        {
            if (ready || owner == null) return;
            ready = true;
            rig = owner;
            emitters = rig.emitters;
            systems = rig.shotParticles;
            flightOn = rig.flightOn;
            count = rig._EmitterCount();
            speed = Mathf.Max(rig.shotSpeed, 0.01f);
            range = Mathf.Max(rig.maxRange, 0.01f);
            life = range / speed;
            interval = 1f / Mathf.Max(rig.streamRate, 0.1f);
            width = Mathf.Max(rig.streamWidth, 0.0005f);
            taper = rig.streamTaper;
            join = rig.streamJoin;
            hitMode = rig.streamHitMode;
            hitInterval = Mathf.Max(rig.streamHitInterval, 0.02f);
            cap = Mathf.Clamp(Mathf.CeilToInt(life / interval) + 2, 4, 128);

            reach = new float[count];
            span = new float[count];
            active = new bool[count];
            nextEmitAt = new float[count];
            nextHitAt = new float[count];
            checkFrom = new float[count];
            trailShare = new float[count];
            baseTrailLife = new float[count];
            look = new bool[count];
            baseCollision = new bool[count];
            latched = new bool[count];
            baseTrailWidth = new ParticleSystem.MinMaxCurve[count];
            head = new int[count];
            rOrigin = new Vector3[count * cap];
            rDir = new Vector3[count * cap];
            rTime = new float[count * cap];
            rAlive = new bool[count * cap];
            for (int e = 0; e < count; e++)
            {
                trailShare[e] = -1f;
                ParticleSystem ps = Shot(e);
                if (ps == null) continue;
                ParticleSystem.CollisionModule collision = ps.collision;
                baseCollision[e] = collision.enabled;
                ParticleSystem.TrailModule trails = ps.trails;
                baseTrailLife[e] = trails.lifetimeMultiplier;
                baseTrailWidth[e] = trails.widthOverTrail;
            }
        }

        // 連射を今(また)始める: 今から出し、前の回との間はつながない。どの射出口もまた当たってよい
        // (当たり判定を切り替えない rig は、最初の HitOnce の接触の後ずっと止まったままになってしまうため)。
        public void _Begin(float now)
        {
            if (!ready) return;
            resumeAt = now;
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
            trailShare[e] = -1f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = baseCollision[e];
            ParticleSystem.TrailModule trails = ps.trails;
            trails.lifetimeMultiplier = baseTrailLife[e];
            trails.widthOverTrail = baseTrailWidth[e];
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
            for (int k = 0; k < rAlive.Length; k++) if (rAlive[k] && (now - rTime[k]) * speed <= range) return true;
            return false;
        }

        // 粒子のシミュレーションの前、止まったフレームでだけ: つないだトレイルを今伸ばす。後に回すと、
        // シミュレーションが止まり(引っかかり)より前の点を先に落としてしまい、落ちた点は戻らない。
        public void _PreSimulate(float now, float dt)
        {
            if (!ready || join != JoinLine || dt <= StallShare * interval) return;
            for (int e = 0; e < count; e++)
            {
                if (!look[e] || flightOn[e] || trailShare[e] < 0f) continue;
                float gap = dt;
                int newest = e * cap + (head[e] + cap - 1) % cap;
                if (rAlive[newest] && rTime[newest] >= resumeAt && now - rTime[newest] > gap) gap = now - rTime[newest];
                float share = Share(TrailTime(gap));
                if (share > trailShare[e]) SetShare(e, share);
            }
        }

        // 全員、rig の PostLateUpdate から(狙いの後): 出す、経路を古くする、トレイルをつないだままにする。持ち主で
        // 当たり判定が on なら、経路が最初に当たった所を記録する。true = まだ動いている(連射中か粒子が飛んでいる)。
        public bool _Step(float now, bool streaming, bool owner, bool collide) { return _StepMasked(now, streaming, owner, collide, 0); }

        // _Step と同じ。mask = 粒子を出す射出口の組(ビット e = 射出口 e。0 = 全部)。組の外の射出口は新しく出さないが、
        // もう飛んでいる粒子は今までどおり進み、当たる。
        public bool _StepMasked(float now, bool streaming, bool owner, bool collide, int mask)
        {
            if (!ready) return false;
            float dt = Mathf.Clamp(Time.deltaTime, 0f, FrameMax);
            frameDt = Mathf.Max(dt, Mathf.Lerp(frameDt, dt, FrameEase));
            bool busy = streaming;
            for (int e = 0; e < count; e++)
            {
                if (emitters[e] == null) continue;
                bool emitHere = streaming && (mask == 0 || (mask & (1 << e)) != 0);
                if (emitHere && flightOn[e]) nextEmitAt[e] = now; // 単発が粒子を使っている。追いつかずに再開する
                else if (emitHere)
                {
                    int k = 0;
                    while (now >= nextEmitAt[e] && k < CatchUpMax) { Emit(e, now); nextEmitAt[e] += interval; k++; }
                    if (now >= nextEmitAt[e]) nextEmitAt[e] = now + interval;
                }
                else if (streaming) nextEmitAt[e] = now; // 組の外: 組に戻った時に追いつきで続けて出さない
                float maxGap = Age(e, now);
                if (active[e]) busy = true;
                if (active[e] && look[e] && join == JoinLine && !flightOn[e])
                {
                    float trailTime = TrailTime(maxGap);
                    float share = Share(trailTime);
                    if (Mathf.Abs(share - trailShare[e]) > 0.0005f) SetShare(e, share);
                    span[e] = Mathf.Min(reach[e], trailTime * speed);
                }
                else span[e] = 0f;
                if (owner && collide && active[e]) Collide(e, now);
                checkFrom[e] = now;
            }
            return busy;
        }

        // 射程を越えた経路を捨てる。reach / active を埋める。飛んでいる隣り合う粒子の、生まれた間隔の最大を返す。
        private float Age(int e, float now)
        {
            int at = e * cap;
            float farthest = 0f;
            float prevBorn = 0f;
            float maxGap = 0f;
            bool any = false;
            for (int r = 0; r < cap; r++)
            {
                int q = at + (head[e] + r) % cap; // 古いものから新しいものへ
                if (!rAlive[q]) continue;
                float flown = (now - rTime[q]) * speed;
                if (flown > range) { rAlive[q] = false; continue; }
                if (flown > farthest) farthest = flown;
                bool acrossRestart = prevBorn < resumeAt && rTime[q] >= resumeAt;
                if (any && !acrossRestart && rTime[q] - prevBorn > maxGap) maxGap = rTime[q] - prevBorn;
                prevBorn = rTime[q];
                any = true;
            }
            reach[e] = farthest;
            active[e] = any;
            return maxGap;
        }

        // 持ち主: 生きている経路ごとに、前に確かめた後に飛んだ区間を判定する。時間で一番早い接触を採る。
        private void Collide(int e, float now)
        {
            if (hitMode == HitOnce && latched[e]) return;
            if (hitMode == HitKeep && now < nextHitAt[e]) return;
            int at = e * cap;
            int bestQ = -1;
            float bestWhen = 1e30f;
            for (int r = 0; r < cap; r++)
            {
                int q = at + r;
                if (!rAlive[q]) continue;
                float a = (Mathf.Max(checkFrom[e], rTime[q]) - rTime[q]) * speed;
                float b = Mathf.Min((now - rTime[q]) * speed, range);
                if (b - a <= 0.0001f) continue;
                if (!rig._CastPath(rOrigin[q] + rDir[q] * a, rDir[q], b - a)) continue;
                float when = rTime[q] + (a + rig._CastDistance()) / speed;
                if (when >= bestWhen) continue;
                bestWhen = when;
                bestQ = q;
                // rig の判定の結果は次の判定で上書きされる: 一番よいものを今 rig に残させる。
                rig._KeepCast();
            }
            if (bestQ < 0) return;
            rig._StreamHit(e);
            if (hitMode == HitOnce)
            {
                latched[e] = true;
                _Clear(e);
            }
            else
            {
                rAlive[bestQ] = false;
                nextHitAt[e] = now + hitInterval;
            }
        }

        // 射出口の今の姿勢(このフレームで rig が狙わせた)から粒子を1つ。いつも射出口で生まれるので、一番新しい
        // トレイルが線を射出口につないでおく。
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
            main.startSpeedMultiplier = speed;
            main.startLifetimeMultiplier = life;
            if (!ps.isPlaying) ps.Play(true);
            ps.Emit(1);
            int k = e * cap + head[e];
            head[e] = (head[e] + 1) % cap;
            rOrigin[k] = origin; rDir[k] = dir; rTime[k] = now; rAlive[k] = true;
        }

        private void UseStreamLook(int e, ParticleSystem ps)
        {
            look[e] = true;
            trailShare[e] = -1f;
            // 粒子は Emit からだけ出す。Play で作ったときのバーストを出してはいけない。
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            // HitOnce: 接触は持ち主の判定で決める。作ったときの当たり判定だと、撃つ人の体で粒子が消えてしまう。
            // HitKeep: 作ったときの当たり判定を残し、連射(水)が壁で自然に終わるようにする。
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = hitMode == HitKeep && baseCollision[e];
            ParticleSystem.MainModule main = ps.main;
            if (main.maxParticles < cap + 1) main.maxParticles = cap + 1;
            if (join != JoinLine) return;
            // トレイルごとに幅は一定: 短いトレイルごとに根元から先へ細くすると、くさびをつないだ鎖に見える。
            ParticleSystem.TrailModule trails = ps.trails;
            if (taper != null && taper.length >= 2) trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width, taper);
            else trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width);
        }

        // つないだトレイル1本が覆うべき秒数: 1つ前の粒子まで + フレームに基づく重なり。止まった時は古い方の
        // 粒子のトレイルが長い1区間を持ち、それが点ごとに削られるので、間隔の約2倍もたせる必要がある。
        private float TrailTime(float gap)
        {
            float reachBack = gap > StallShare * interval ? 2f * gap : Mathf.Max(interval, gap);
            return reachBack + Mathf.Max(OverlapMin, OverlapFrames * frameDt);
        }

        private float Share(float trailTime) { return Mathf.Clamp(trailTime / life, 0.001f, 1f); }

        private void SetShare(int e, float share)
        {
            ParticleSystem ps = Shot(e);
            if (ps == null) return;
            ParticleSystem.TrailModule trails = ps.trails;
            trails.lifetimeMultiplier = share;
            trailShare[e] = share;
        }

        private void Forget(int e)
        {
            int at = e * cap;
            for (int i = 0; i < cap; i++) rAlive[at + i] = false;
            active[e] = false;
            reach[e] = 0f;
            span[e] = 0f;
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
