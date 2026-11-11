using UdonSharp;
using UnityEngine;

namespace Mecaota.Gunner
{
    // Stream fire for one GunnerRig: emits stream particles, keeps their paths, finds where they touch (owner), joins the trails.
    // Driven only by the rig (Update -> _PreSimulate, PostLateUpdate -> _Step); it has no events of its own.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class GunnerStream : UdonSharpBehaviour
    {
        public const int JoinParticles = 0;
        public const int JoinLine = 1;
        // HitOnce: the first touch records one hit, clears that emitter's particles in the air, and that emitter records no more
        // until the stream restarts or collision is switched on again; emission goes on while the trigger is held.
        public const int HitOnce = 0;
        public const int HitKeep = 1;

        private const int CatchUpMax = 3;        // emits per frame after a hitch (no burst)
        private const float FrameMax = 0.1f;     // frame time counted for the trail overlap at most
        private const float FrameEase = 0.05f;   // per frame: how fast the followed frame time comes back down
        private const float OverlapMin = 0.02f;  // seconds a joined trail reaches past the previous particle at least
        private const float OverlapFrames = 2f;  // ... and at least this many frames (births and trail points land on frames)
        private const float StallShare = 1.5f;   // a birth gap wider than this many intervals is a stall

        // Per emitter, for look scripts: metres the farthest stream particle has flown, metres one joined trail spans, and
        // whether any stream particle is in the air.
        [System.NonSerialized] public float[] reach;
        [System.NonSerialized] public float[] span;
        [System.NonSerialized] public bool[] active;

        private GunnerRig rig;
        private bool ready;
        private int count;
        private int cap;
        private Transform[] emitters;
        private ParticleSystem[] systems;
        private bool[] flightOn;               // the rig's array: a single shot owns the system meanwhile
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

        // The stream (re)starts now: emits from now, the gap from the previous run is never bridged, and every emitter may hit
        // again (a rig that never toggles collision would otherwise stay latched after its first HitOnce touch).
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

        // Collision off -> on: each emitter may hit again, counted from where its particles are now.
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

        // Back to the authored single-shot setup (the rig calls this before a single shot on the same system).
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

        // Clears emitter e's stream particles (a HitOnce touch, StopFire, ClearHits).
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

        // Before the particle simulation, on a stalled frame only: joined trails lengthen now. Later would be too late, the
        // simulation would already have dropped the points from before the stall, and dropped points never come back.
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

        // Every client, from the rig's PostLateUpdate (after the aim): emit, age the paths, keep the trails joined, and on the
        // owner with collision on, record where a path first touches. True = still busy (streaming or particles in the air).
        public bool _Step(float now, bool streaming, bool owner, bool collide)
        {
            if (!ready) return false;
            float dt = Mathf.Clamp(Time.deltaTime, 0f, FrameMax);
            frameDt = Mathf.Max(dt, Mathf.Lerp(frameDt, dt, FrameEase));
            bool busy = streaming;
            for (int e = 0; e < count; e++)
            {
                if (emitters[e] == null) continue;
                if (streaming && flightOn[e]) nextEmitAt[e] = now; // the single shot owns the system; resume without catch-up
                else if (streaming)
                {
                    int k = 0;
                    while (now >= nextEmitAt[e] && k < CatchUpMax) { Emit(e, now); nextEmitAt[e] += interval; k++; }
                    if (now >= nextEmitAt[e]) nextEmitAt[e] = now + interval;
                }
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

        // Drops paths past the range; fills reach / active. Returns the widest birth gap between consecutive particles in the air.
        private float Age(int e, float now)
        {
            int at = e * cap;
            float farthest = 0f;
            float prevBorn = 0f;
            float maxGap = 0f;
            bool any = false;
            for (int r = 0; r < cap; r++)
            {
                int q = at + (head[e] + r) % cap; // oldest to newest
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

        // Owner: casts each live path along the stretch flown since the last check; the earliest touch in time wins.
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
                // The rig's cast result is overwritten by the next cast: the rig keeps the best one now.
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

        // One particle from the emitter's current pose (the rig aimed it this frame). Always born at the emitter, so the newest
        // trail keeps the line attached to it.
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
            // Particles come from Emit only; Play must not fire the authored burst.
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            // HitOnce: the owner's cast decides the touch, and authored collision would kill particles on the shooter itself.
            // HitKeep: the authored collision stays so a stream (water) can end on walls by itself.
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = hitMode == HitKeep && baseCollision[e];
            ParticleSystem.MainModule main = ps.main;
            if (main.maxParticles < cap + 1) main.maxParticles = cap + 1;
            if (join != JoinLine) return;
            // One even width per trail: a root-to-tip taper over each short trail would read as a chain of wedges.
            ParticleSystem.TrailModule trails = ps.trails;
            if (taper != null && taper.length >= 2) trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width, taper);
            else trails.widthOverTrail = new ParticleSystem.MinMaxCurve(width);
        }

        // Seconds one joined trail must cover: up to the previous particle plus a frame-based overlap. Over a stall the older
        // particle's trail holds one long span that is cut point by point, so it must last about twice the gap.
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
