using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Mecaota.Gunner
{
    // Controller: settings, the synced hit records, shooting, the cast, single shots, aim, bone link, and the per-frame order.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GunnerRig : UdonSharpBehaviour
    {
        // ---------------- Constants ----------------

        public const int KindNone = 0;
        public const int KindWorld = 1;
        public const int KindMoving = 2;
        public const int KindPlayer = 3;
        public const int FireSingle = 0;
        public const int FireStream = 1;
        public const int AimNone = 0;
        public const int AimDirection = 1;
        public const int AimTarget = 2;
        public const int AimAngles = 3;
        public const int MaxEmitters = 16;

        /// <summary>
        /// Length of the synced record ring, the same on every build (PC and Android must agree on it or the ring is read
        /// differently). maxHits only limits how many of the newest records a client uses.
        /// </summary>
        public const int SyncSlots = 16;

        /// <summary>
        /// Largest target number a record can hold (16 bits). A player whose playerId is above this (only in very long-running
        /// instances) is not recorded as a player hit; the shot passes on to whatever is behind.
        /// </summary>
        public const int MaxTargetIndex = 0xFFFF;

        // hMeta layout (31 bits, sign bit unused so the value never goes negative).
        private const int EmitterShift = 0;
        private const int EmitterMask = 0xF;
        private const int KindShift = 4;
        private const int KindMask = 0x7;
        private const int BoneShift = 7;
        private const int BoneMask = 0x7F;
        private const int BoneNoneCode = 0x7F;
        private const int IndexShift = 14;
        private const int IndexMask = 0xFFFF;
        private const int InstantBit = 1 << 30;

        // hWriter layout: who wrote the record (low 12 bits of the playerId) and when (server time in 16 ms ticks, 19 bits,
        // wraps after about 2.3 hours). Tells two records with the same seq apart, and a late joiner's recent records from old ones.
        private const int TickShift = 4;
        private const int TickMask = 0x7FFFF;
        private const int TickMs = 16;
        private const int WriterIdShift = 19;
        private const int WriterIdMask = 0xFFF;
        private const int JoinMarginMs = 1000;  // records this much older than the join still count as seen live

        private const int PlayerLayers = (1 << 9) | (1 << 10);
        private const int CastBuffer = 32;
        private const float SendInterval = 0.2f;   // throttled sends (stream hits, aim) at most 5 per second
        private const float AimSyncAngle = 2f;
        private const float AimSyncMove = 0.05f;
        private const float BodyRadiusMin = 0.08f;  // metres; keeps tiny avatars hittable
        private const float BodyRadiusMax = 0.5f;   // metres; keeps huge avatars from swallowing every shot nearby

        // ---------------- Setup (written by the editor) ----------------

        public Transform[] emitters;              // +Z = rest direction
        public ParticleSystem[] shotParticles;    // same index as emitters; World simulation space
        public Transform[] mounts;                // optional per emitter: the emitter takes this pose every frame (+Z = rest)
        public Transform[] linkBones;             // optional per emitter: what the aim turns; empty = the emitter's parent in the rig
        public Transform[] movers;                // moving objects: a hit on one (or a child) follows it
        public GunnerStream stream;               // optional: stream fire
        public GunnerHitReceiver projectile;      // the one receiver asked for the payload (_OnOwnerHit / _OnOwnerCommit)
        public GunnerHitReceiver[] receivers;     // everyone notified of hits; _Register adds more at runtime

        public int maxHits = 16;                  // records this client uses (1..SyncSlots); lower it on Android, the sync stays 16
        public float shotSpeed = 25f;
        public float maxRange = 30f;
        public float castRadius = 0.05f;
        public LayerMask hitMask = ~(1 << 2);     // player layers are ignored here; hitPlayers decides
        public bool hitPlayers = true;
        // Skips the body of the player on this device, i.e. whoever runs the cast. Casts run on the rig owner, so in practice
        // the shooter is never hit by their own shot (an emitter on their own bones, a shot fired backwards). A trap owned by a
        // bystander also leaves that bystander unhit.
        public bool excludeOwner = true;
        public float playerRadius = 0.12f;        // body radius as a share of the target's eye height
        public int fireMode = FireSingle;         // what TriggerDown / TriggerUp do
        public float aimMaxAngle = 60f;           // degrees from the rest direction

        public float streamRate = 12f;            // particles per second per emitter
        public float streamWidth = 0.02f;         // joined line width (metres)
        public AnimationCurve streamTaper;        // width along one trail (empty = flat)
        public int streamJoin = GunnerStream.JoinLine;
        public int streamHitMode = GunnerStream.HitOnce;
        public float streamHitInterval = 0.25f;   // HitKeep: seconds between records per emitter
        public bool streamCollide = true;         // collision at start (owner-local; _SetCollision changes it)

        public bool boneFollow;                   // initial value; BoneFollowOn / Off sync a change
        public float boneStrength = 1f;           // share of the aim the bone takes
        public float boneYawMax = 30f;
        public float bonePitchUpMax = 30f;
        public float bonePitchDownMax = 15f;
        public float boneSmooth = 10f;            // per second: share of the remaining angle closed
        public float boneMinSpeed = 30f;          // degrees per second at least

        // ---------------- Synced (Manual) ----------------

        [UdonSynced] private int hSeq;                         // records written so far; slot of seq q = (q - 1) % SyncSlots
        [UdonSynced] private int[] hMeta = new int[0];
        [UdonSynced] private int[] hPayload = new int[0];
        [UdonSynced] private Vector3[] hLocal = new Vector3[0]; // world point, or the point in the target's frame
        [UdonSynced] private int[] hWriter = new int[0];       // writer and write time (hWriter layout above)
        [UdonSynced] private int[] mSeq = new int[0];          // per emitter: misses fired
        [UdonSynced] private Vector3[] mDir = new Vector3[0];  // per emitter: direction of the last miss
        [UdonSynced] private int stopSeq;
        [UdonSynced] private bool streamOn;
        [UdonSynced] private int streamPayload;
        [UdonSynced] private byte aimSyncMode;
        [UdonSynced] private Vector3 aimSync;
        [UdonSynced] private byte boneMode;                    // 0 = boneFollow as set, 1 = off, 2 = on

        // ---------------- Read by receivers (the current hit) ----------------

        [System.NonSerialized] public int hitSlot = -1;
        [System.NonSerialized] public int hitSeq;
        [System.NonSerialized] public int hitEmitter = -1;
        [System.NonSerialized] public int hitKind;
        [System.NonSerialized] public int hitIndex = -1;       // mover index or playerId
        [System.NonSerialized] public int hitBone = -1;        // HumanBodyBones as int, -1 = player root
        [System.NonSerialized] public Vector3 hitLocal;
        [System.NonSerialized] public Vector3 hitPoint;
        [System.NonSerialized] public float hitDistance;
        [System.NonSerialized] public int hitPayload;
        [System.NonSerialized] public bool hitReplay;
        [System.NonSerialized] public bool hitStream;
        // False when the target cannot be found now (player left, mover gone): hitPoint is then the last world point seen,
        // or Vector3.zero when this client never saw one.
        [System.NonSerialized] public bool hitResolved;

        // ---------------- 受け手が読む(_OnFire の中身) ----------------

        [System.NonSerialized] public int fireEmitter = -1;    // 連射の開始は -1(全部の射出口)
        [System.NonSerialized] public bool fireStream;         // 連射の開始なら真
        [System.NonSerialized] public bool fireReplay;         // 入室時に既に続いていた連射なら真。今撃たれたのではないので音は鳴らさない

        // Per slot, filled before _OnGunnerLateStep (only when a receiver wants it). Never reallocated after Init.
        [System.NonSerialized] public Vector3[] hitStart;
        [System.NonSerialized] public Vector3[] hitWorld;
        [System.NonSerialized] public bool[] hitLive;

        // Per emitter: the single shot in the air (for look scripts).
        [System.NonSerialized] public bool[] flightOn;
        [System.NonSerialized] public float[] flightFlown;
        [System.NonSerialized] public float[] flightLength;
        [System.NonSerialized] public int[] flightSlot;          // -1 = a miss

        // _SolveFor result.
        [System.NonSerialized] public Vector3 solveDir;
        [System.NonSerialized] public float solveDistance;

        // ---------------- Local state ----------------

        private bool initialized;
        private bool baselined;
        private int slots;
        private int emitterCount;
        private int used;               // maxHits clamped: the newest records this client holds
        private int headSeq;            // newest seq known here: the last received hSeq, or this client's own last write
        private int sentSeq;            // hSeq as last actually serialized by this client (records up to it are out)
        private int pendingSeq;         // hSeq captured in OnPreSerialization
        private int joinMs;             // server time when this client's rig started (late-join cut-off)
        private int seenStop;
        private int[] seenMiss;
        // Per slot, this client's own copy of the record it holds: the synced arrays may already carry the next record when
        // the old one is dropped, and receivers must hear the old one's values.
        private int[] slotSeq;          // 0 = empty
        private int[] kMeta;
        private int[] kPayload;
        private Vector3[] kLocal;
        private Vector3[] kWorld;       // last world point seen for the record
        private int[] kWriter;
        private float[] arriveAt;       // < 0 = nothing pending
        private bool[] arrived;
        private bool[] slotReplay;
        private float[] flightStart;

        private int aimMode;
        private Vector3 aimVector;
        private bool streamMine;
        private bool streamWas;
        private bool streamBusy;
        private bool collideOn;
        private bool dirty;             // owner: records changed, not sent yet
        private bool aimPending;        // owner: the aim changed, not sent yet
        private float nextSendAt;

        private GunnerHitReceiver[] lateReceivers = new GunnerHitReceiver[0];

        private Transform[] restRef;    // per emitter: the frame its rest rotation is relative to
        private Quaternion[] restRel;
        private Quaternion[] restLocal; // taken once in Init, never from an aimed pose
        private Vector3[] restPos;
        private int[] linkOf;           // per emitter: link index, -1 = none
        private Transform[] links;
        private int[] linkLead;
        private Quaternion[] linkBase;
        private Quaternion[] linkWritten;
        private bool[] linkHeld;
        private float[] followYaw;
        private float[] followPitch;
        private float[] turnYaw;
        private float[] turnPitch;
        private bool aimedOnce;         // emitters were turned and need one return to rest

        private int[] probeBones = { 0, 7, 8, 54, 9, 10, 13, 15, 17, 14, 16, 18, 1, 3, 5, 2, 4, 6 };
        private Vector3 wish;           // result of Wish / Resolve (U# has no out parameters on user methods)

        // Players, read once per frame for every cast of that frame.
        private VRCPlayerApi[] players = new VRCPlayerApi[16];
        private Vector3[] pFeet = new Vector3[16];
        private Vector3[] pHead = new Vector3[16];
        private float[] pRadius = new float[16];
        private int pCount;
        private int pFrame = -1;

        private RaycastHit[] castHits = new RaycastHit[CastBuffer];

        // Cast result (owner). The bone and its local point are worked out only for the hit that is kept (FinishCast).
        private int castKind;
        private int castIndex;
        private Vector3 castPoint;
        private Vector3 castLocal;
        private float castDistance;
        private VRCPlayerApi castPlayer;
        // The cast kept by the stream (its best touch of the frame).
        private int keepKind;
        private int keepIndex;
        private Vector3 keepPoint;
        private Vector3 keepLocal;
        private float keepDistance;
        private VRCPlayerApi keepPlayer;
        private int recBone;            // FinishCast result

        // ================= Lifecycle =================

        private void Start()
        {
            Init();
            // Everyone else waits for the first state; the owner resends it to each joiner (OnPlayerJoined).
            if (Networking.IsOwner(gameObject)) baselined = true;
        }

        private void Init()
        {
            if (initialized) return;
            initialized = true;
            slots = SyncSlots;
            used = Mathf.Clamp(maxHits, 1, SyncSlots);
            joinMs = Networking.GetServerTimeInMilliseconds();
            emitterCount = emitters != null ? Mathf.Min(emitters.Length, MaxEmitters) : 0;
            if (receivers == null) receivers = new GunnerHitReceiver[0];
            collideOn = streamCollide;
            EnsureSyncArrays();

            seenMiss = new int[emitterCount];
            for (int i = 0; i < emitterCount; i++) seenMiss[i] = i < mSeq.Length ? mSeq[i] : 0;
            slotSeq = new int[slots];
            kMeta = new int[slots];
            kPayload = new int[slots];
            kLocal = new Vector3[slots];
            kWorld = new Vector3[slots];
            kWriter = new int[slots];
            arriveAt = new float[slots];
            arrived = new bool[slots];
            slotReplay = new bool[slots];
            for (int s = 0; s < slots; s++) arriveAt[s] = -1f;
            hitStart = new Vector3[slots];
            hitWorld = new Vector3[slots];
            hitLive = new bool[slots];
            flightOn = new bool[emitterCount];
            flightFlown = new float[emitterCount];
            flightLength = new float[emitterCount];
            flightSlot = new int[emitterCount];
            flightStart = new float[emitterCount];
            for (int i = 0; i < emitterCount; i++) flightSlot[i] = -1;

            restLocal = new Quaternion[emitterCount];
            restPos = new Vector3[emitterCount];
            for (int i = 0; i < emitterCount; i++)
            {
                if (emitters[i] == null) continue;
                restLocal[i] = emitters[i].localRotation;
                restPos[i] = emitters[i].localPosition;
            }
            InitAimFrames();
            RecountLate();
            if (stream != null) stream._Init(this);
        }

        // Received arrays are kept when they already fit, so a state that arrived before Init is not wiped.
        private void EnsureSyncArrays()
        {
            int n = SyncSlots;
            if (hMeta == null || hMeta.Length != n || hPayload == null || hPayload.Length != n || hLocal == null || hLocal.Length != n
                || hWriter == null || hWriter.Length != n)
            {
                hMeta = new int[n];
                hPayload = new int[n];
                hLocal = new Vector3[n];
                hWriter = new int[n];
            }
            int e = emitterCount;
            if (mSeq == null || mSeq.Length != e || mDir == null || mDir.Length != e)
            {
                mSeq = new int[e];
                mDir = new Vector3[e];
            }
        }

        // Rest frames from the rest pose taken in Init (callers release the linked bones first, so the bones are at rest too).
        private void InitAimFrames()
        {
            int e = emitterCount;
            restRef = new Transform[e];
            restRel = new Quaternion[e];
            linkOf = new int[e];
            Transform[] found = new Transform[e];
            int[] lead = new int[e];
            int count = 0;
            Transform self = transform;
            for (int i = 0; i < e; i++)
            {
                linkOf[i] = -1;
                restRel[i] = Quaternion.identity;
                Transform m = emitters[i];
                if (m == null) continue;
                Transform mount = Mount(i);
                if (mount != null) { restRef[i] = mount; continue; } // a mounted emitter is never bone-linked
                Transform parent = m.parent;
                Quaternion parentRot = parent != null ? parent.rotation : Quaternion.identity;
                Transform bone = linkBones != null && i < linkBones.Length ? linkBones[i] : null;
                if (bone == null && parent != null && parent != self && parent.IsChildOf(self)) bone = parent;
                if (bone == null || !m.IsChildOf(bone))
                {
                    restRef[i] = parent;
                    restRel[i] = restLocal[i];
                    continue;
                }
                // Relative to the linked bone, so a bone above the direct parent still works (the chain between is assumed still).
                restRef[i] = bone;
                restRel[i] = Quaternion.Inverse(bone.rotation) * parentRot * restLocal[i];
                int at = -1;
                for (int b = 0; b < count; b++) if (found[b] == bone) { at = b; break; }
                if (at < 0)
                {
                    at = count;
                    found[count] = bone;
                    lead[count] = i;
                    count++;
                }
                linkOf[i] = at;
            }
            links = new Transform[count];
            linkLead = new int[count];
            linkBase = new Quaternion[count];
            linkWritten = new Quaternion[count];
            linkHeld = new bool[count];
            followYaw = new float[count];
            followPitch = new float[count];
            turnYaw = new float[count];
            turnPitch = new float[count];
            for (int b = 0; b < count; b++)
            {
                links[b] = found[b];
                linkLead[b] = lead[b];
                linkBase[b] = found[b].localRotation;
                linkWritten[b] = linkBase[b];
            }
        }

        private void OnDisable()
        {
            // No PostLateUpdate will ease them back, so the linked bones return at once.
            if (initialized) ReleaseLinks();
        }

        private void ReleaseLinks()
        {
            if (links == null) return;
            for (int b = 0; b < links.Length; b++)
            {
                followYaw[b] = 0f;
                followPitch[b] = 0f;
                turnYaw[b] = 0f;
                turnPitch[b] = 0f;
                if (!linkHeld[b]) continue;
                linkHeld[b] = false;
                if (links[b] != null) links[b].localRotation = linkBase[b];
            }
        }

        // ================= Receivers =================

        public void _Register(GunnerHitReceiver receiver)
        {
            if (receiver == null) return;
            if (receivers == null) receivers = new GunnerHitReceiver[0];
            for (int i = 0; i < receivers.Length; i++) if (receivers[i] == receiver) return;
            GunnerHitReceiver[] grown = new GunnerHitReceiver[receivers.Length + 1];
            for (int i = 0; i < receivers.Length; i++) grown[i] = receivers[i];
            grown[receivers.Length] = receiver;
            receivers = grown;
            RecountLate();
        }

        public void _Unregister(GunnerHitReceiver receiver)
        {
            if (receiver == null || receivers == null) return;
            int at = -1;
            for (int i = 0; i < receivers.Length; i++) if (receivers[i] == receiver) { at = i; break; }
            if (at < 0) return;
            GunnerHitReceiver[] shrunk = new GunnerHitReceiver[receivers.Length - 1];
            int j = 0;
            for (int i = 0; i < receivers.Length; i++)
            {
                if (i == at) continue;
                shrunk[j] = receivers[i];
                j++;
            }
            receivers = shrunk;
            RecountLate();
        }

        // wantsLateStep is read here, not every frame. A receiver that changes it later calls this again.
        public void _RefreshReceivers() { RecountLate(); }

        private void RecountLate()
        {
            GunnerHitReceiver[] list = receivers;
            int count = 0;
            for (int i = 0; i < list.Length; i++) if (list[i] != null && list[i].wantsLateStep) count++;
            GunnerHitReceiver[] late = new GunnerHitReceiver[count];
            int j = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == null || !list[i].wantsLateStep) continue;
                late[j] = list[i];
                j++;
            }
            lateReceivers = late;
        }

        // Each loop walks the array as it was when the loop began: a receiver may (un)register from inside a callback.
        private void NotifyHit()
        {
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnHit(this);
        }

        private void NotifyGone()
        {
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnHitGone(this);
        }

        // 粒子の有無に関係なく、各端末で発射の瞬間に1回ずつ呼ぶ(粒子の無い rig でも音を出せるように)。
        private void NotifyFire(int e, bool isStream, bool replay)
        {
            fireEmitter = e;
            fireStream = isStream;
            fireReplay = replay;
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnFire(this);
        }

        // ================= Public operations =================

        public void Fire() { _FireWith(-1, 0); }

        public void _FireFrom(int emitter) { _FireWith(emitter, 0); }

        // emitter < 0 = every emitter. The payload is opaque here; the projectile receiver may rewrite it per record.
        public void _FireWith(int emitter, int payload)
        {
            Init();
            if (emitter >= 0 && !ValidEmitter(emitter)) return;
            if (!TakeOwnership()) return;
            bool any = false;
            if (emitter >= 0) any = Shoot(emitter, payload);
            else for (int i = 0; i < emitterCount; i++) if (Shoot(i, payload)) any = true;
            if (any) _Commit();
        }

        public void StartStream() { _StartStreamWith(0); }

        public void _StartStreamWith(int payload)
        {
            Init();
            if (stream == null || !TakeOwnership()) return;
            streamMine = true;
            streamPayload = payload;
            if (!streamOn)
            {
                streamOn = true;
                streamWas = true;
                stream._Begin(Time.time);
                NotifyFire(-1, true, false);
            }
            aimSyncMode = (byte)aimMode;
            aimSync = aimVector;
            aimPending = false;
            SendNow();
        }

        // Stops only a stream this client started: a late release must not stop someone else's newer stream.
        public void StopStream()
        {
            Init();
            if (!streamOn || !streamMine)
            {
                streamMine = false;
                return;
            }
            if (!Networking.IsOwner(gameObject))
            {
                streamMine = false;
                return;
            }
            streamOn = false;
            streamWas = false;
            streamMine = false;
            ClearSyncedAimIfUnused();
            SendNow();
        }

        // Stops whatever stream is running, whoever started it (takes the rig).
        public void _ForceStopStream()
        {
            Init();
            if (!streamOn || !TakeOwnership()) return;
            streamOn = false;
            streamWas = false;
            streamMine = false;
            ClearSyncedAimIfUnused();
            SendNow();
        }

        // Owner: once neither the own stream nor the bone link carries the aim, everyone else's emitters go back to rest.
        private void ClearSyncedAimIfUnused()
        {
            if (AimSynced()) return;
            aimSyncMode = (byte)AimNone;
            aimPending = false;
        }

        private bool AimSynced() { return (streamOn && streamMine) || (BoneOn() && links.Length > 0); }

        // Stops what is in the air: shots not yet arrived are retired; arrived hits stay.
        public void StopFire()
        {
            Init();
            if (!TakeOwnership()) return;
            for (int s = 0; s < slots; s++) if (slotSeq[s] != 0 && !arrived[s]) RetireSlot(s);
            stopSeq++;
            seenStop = stopSeq;
            StopAllFlights();
            _Commit();
        }

        public void ClearHits()
        {
            Init();
            if (!TakeOwnership()) return;
            for (int s = 0; s < slots; s++) RetireSlot(s);
            stopSeq++;
            seenStop = stopSeq;
            StopAllFlights();
            _Commit();
        }

        // Owner only: forgets one record (callers then _Commit once, after any number of retires).
        public void _RetireHit(int slot)
        {
            Init();
            if (slot < 0 || slot >= slots || !Networking.IsOwner(gameObject)) return;
            RetireSlot(slot);
        }

        // Owner only: lets the projectile tidy up, then sends the records now.
        public void _Commit()
        {
            if (!Networking.IsOwner(gameObject)) return;
            dirty = false;
            if (projectile != null) projectile._OnOwnerCommit(this);
            SendNow();
        }

        // For a holder (e.g. a grabbed controller) that should own the rig before it shoots. True = owner now.
        public bool _TakeOwnership()
        {
            Init();
            return TakeOwnership();
        }

        // Single: fires. Stream: starts; TriggerUp stops it.
        public void TriggerDown()
        {
            if (fireMode == FireStream) StartStream();
            else Fire();
        }

        public void TriggerUp()
        {
            if (fireMode == FireStream) StopStream();
        }

        // Owner-local: stream particles may record hits. Off -> on starts counting from where the particles are now.
        public void _SetCollision(bool on)
        {
            Init();
            if (on && !collideOn && stream != null) stream._ResetCollision(Time.time);
            collideOn = on;
        }

        public bool _IsStreaming() { return streamOn; }

        public bool _IsCollisionOn() { return collideOn; }

        public bool _IsStreamInFlight() { return initialized && stream != null && stream._InFlight(Time.time); }

        public bool _IsBoneFollowing() { return BoneOn(); }

        public int _LiveHitCount()
        {
            if (!initialized) return 0;
            int count = 0;
            for (int s = 0; s < slots; s++) if (slotSeq[s] != 0 && arrived[s]) count++;
            return count;
        }

        // Record accessors (this client's copy) for receivers that walk the slots themselves.
        public bool _SlotUsed(int slot) { return initialized && slot >= 0 && slot < slots && slotSeq[slot] != 0; }

        public int _SlotKind(int slot) { return _SlotUsed(slot) ? MetaKind(kMeta[slot]) : KindNone; }

        public int _SlotIndex(int slot) { return _SlotUsed(slot) ? MetaIndex(kMeta[slot]) : -1; }

        public int _SlotPayload(int slot) { return _SlotUsed(slot) ? kPayload[slot] : 0; }

        public int _SlotCount() { return slots; }

        // Which emitter can reach the point (in range, inside its cone, nothing in the way). -1 = none. Sets solveDir / solveDistance.
        public int _SolveFor(Vector3 point)
        {
            Init();
            int best = -1;
            float bestAngle = 1e9f;
            for (int i = 0; i < emitterCount; i++)
            {
                if (!ValidEmitter(i)) continue;
                Vector3 origin = emitters[i].position;
                Vector3 to = point - origin;
                float dist = to.magnitude;
                if (dist < 0.0001f || dist > maxRange) continue;
                Vector3 dir = to / dist;
                float angle = Vector3.Angle(RestWorld(i) * Vector3.forward, dir);
                if (angle > aimMaxAngle || angle >= bestAngle) continue;
                if (Blocked(origin, dir, dist)) continue;
                best = i;
                bestAngle = angle;
                solveDir = dir;
                solveDistance = dist;
            }
            return best;
        }

        // ---------------- Aim ----------------
        // Aiming never takes the rig. The owner's aim is synced while it streams or while the bone link is on; anyone else's
        // aim turns the emitters on their own screen only.

        public void _SetAimDirection(Vector3 worldDir)
        {
            Init();
            if (worldDir.sqrMagnitude < 0.000001f) return;
            aimMode = AimDirection;
            aimVector = worldDir.normalized;
        }

        public void _SetAimTarget(Vector3 worldPoint)
        {
            Init();
            aimMode = AimTarget;
            aimVector = worldPoint;
        }

        // Degrees in emitter 0's rest frame: yaw right +, pitch up +.
        public void _SetAimAngles(float yaw, float pitch)
        {
            Init();
            aimMode = AimAngles;
            aimVector = new Vector3(yaw, pitch, 0f);
        }

        public void ResetAim()
        {
            Init();
            aimMode = AimNone;
        }

        public void BoneFollowOn() { _SetBoneFollow(true); }

        public void BoneFollowOff() { _SetBoneFollow(false); }

        public void _SetBoneFollow(bool on)
        {
            Init();
            if (BoneOn() == on || !TakeOwnership()) return;
            boneMode = (byte)(on ? 2 : 1);
            if (!(streamOn && streamMine))
            {
                aimSyncMode = (byte)aimMode;
                aimSync = aimVector;
            }
            ClearSyncedAimIfUnused();
            aimPending = false;
            SendNow();
        }

        // Moves emitter e onto another Transform at runtime (e.g. a point that follows someone's bone); null puts it back.
        public void _SetMount(int emitter, Transform mount)
        {
            Init();
            if (!ValidEmitter(emitter)) return;
            if (mounts == null || mounts.Length < emitterCount)
            {
                Transform[] grown = new Transform[emitterCount];
                if (mounts != null) for (int i = 0; i < mounts.Length && i < emitterCount; i++) grown[i] = mounts[i];
                mounts = grown;
            }
            Transform before = mounts[emitter];
            mounts[emitter] = mount;
            if (mount == null && before != null)
            {
                emitters[emitter].localPosition = restPos[emitter];
                emitters[emitter].localRotation = restLocal[emitter];
            }
            ReleaseLinks();
            InitAimFrames();
        }

        // ================= Shooting (owner) =================

        private bool TakeOwnership()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            // Before the first state arrives this client's records are empty: writing them would wipe everyone's.
            if (!baselined && !Networking.IsOwner(gameObject)) return false;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return false;
            baselined = true;
            EnsureSyncArrays();
            return true;
        }

        private bool Shoot(int e, int payload)
        {
            if (!ValidEmitter(e)) return false;
            // Called from input outside PostLateUpdate: the mount and aim are brought to now first.
            ApplyMount(e);
            AimEmitter(e, aimMode, aimVector);
            Vector3 origin = emitters[e].position;
            Vector3 dir = emitters[e].forward;
            if (!Cast(origin, dir, maxRange))
            {
                mSeq[e] = mSeq[e] + 1;
                mDir[e] = dir;
                seenMiss[e] = mSeq[e];
                Fly(e, -1, dir, maxRange);
                NotifyFire(e, false, false);
                return true;
            }
            KeepCast();
            int s = WriteRecord(e, payload, false);
            Launch(s);
            return true;
        }

        // GunnerStream, owner: keeps the last cast as the frame's best touch so far.
        public void _KeepCast() { KeepCast(); }

        private void KeepCast()
        {
            keepKind = castKind;
            keepIndex = castIndex;
            keepPoint = castPoint;
            keepLocal = castLocal;
            keepDistance = castDistance;
            keepPlayer = castPlayer;
        }

        // GunnerStream, owner: records the kept touch, shown at once. Sent with the next throttled send.
        public void _StreamHit(int emitter)
        {
            if (!ValidEmitter(emitter) || !Networking.IsOwner(gameObject)) return;
            // The ring would overwrite a record nobody else has received yet: that touch is not recorded (the send goes out at
            // the next chance, see PostLateUpdate), so no client ever sees a hit the others never get.
            if (WouldEvictUnsent()) return;
            int s = WriteRecord(emitter, streamPayload, true);
            arriveAt[s] = Time.time;
            dirty = true;
        }

        // Writes the kept cast into the next slot. The old record there is gone first, everywhere, by the seq change.
        private int WriteRecord(int e, int payload, bool instant)
        {
            int s = hSeq % slots;
            DropSlot(s);
            hMeta[s] = 0;
            FinishKept();
            int seq = hSeq + 1;
            hitSlot = s;
            hitSeq = seq;
            hitEmitter = e;
            hitKind = keepKind;
            hitIndex = keepKind == KindMoving || keepKind == KindPlayer ? keepIndex : -1;
            hitBone = recBone;
            hitLocal = keepLocal;
            hitPoint = keepPoint;
            hitDistance = keepDistance;
            hitPayload = payload;
            hitReplay = false;
            hitStream = instant;
            hitResolved = true;
            if (projectile != null) projectile._OnOwnerHit(this);
            hSeq = seq;
            headSeq = seq;
            hMeta[s] = Pack(e, keepKind, recBone, keepIndex, instant);
            hPayload[s] = hitPayload;
            hLocal[s] = keepLocal;
            hWriter[s] = WriterMark();
            // Alone in the instance (or ClientSim) nothing is serialized, so OnPostSerialization never advances sentSeq: with
            // nobody to miss it, every record counts as sent.
            if (VRCPlayerApi.GetPlayerCount() <= 1) sentSeq = seq;
            HoldSlot(s, seq);
            kWorld[s] = keepPoint;
            ForgetUnused();
            return s;
        }

        private bool WouldEvictUnsent() { return hSeq + 1 - slots > sentSeq; }

        private int WriterMark()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            int id = Utilities.IsValid(local) ? local.playerId : 0;
            int tick = (Networking.GetServerTimeInMilliseconds() >> TickShift) & TickMask;
            return ((id & WriterIdMask) << WriterIdShift) | tick;
        }

        // Milliseconds since a record was written, from its mark (exact for about 2.3 hours, then it wraps).
        private int WriterAgeMs(int mark)
        {
            int now = (Networking.GetServerTimeInMilliseconds() >> TickShift) & TickMask;
            return ((now - (mark & TickMask)) & TickMask) * TickMs;
        }

        // Local only: records older than the newest `used` ones are let go here (still synced for clients that use more).
        private void ForgetUnused()
        {
            if (used >= slots) return;
            for (int s = 0; s < slots; s++) if (slotSeq[s] != 0 && slotSeq[s] <= hSeq - used) DropSlot(s);
        }

        // The kept player hit gets its nearest bone and the point in that bone's frame (only now, for the one record).
        private void FinishKept()
        {
            recBone = -1;
            if (keepKind != KindPlayer) return;
            if (!Utilities.IsValid(keepPlayer))
            {
                // The player left between the cast and now: keep the point in the world.
                keepKind = KindWorld;
                keepLocal = keepPoint;
                return;
            }
            recBone = NearestBone(keepPlayer, keepPoint);
            if (recBone < 0) keepLocal = Quaternion.Inverse(keepPlayer.GetRotation()) * (keepPoint - keepPlayer.GetPosition());
            else
            {
                HumanBodyBones bone = (HumanBodyBones)recBone;
                keepLocal = Quaternion.Inverse(keepPlayer.GetBoneRotation(bone)) * (keepPoint - keepPlayer.GetBonePosition(bone));
            }
        }

        // This client now holds the synced record of slot s as seq: copy it and wait for its arrival.
        private void HoldSlot(int s, int seq)
        {
            slotSeq[s] = seq;
            kMeta[s] = hMeta[s];
            kPayload[s] = hPayload[s];
            kLocal[s] = hLocal[s];
            kWriter[s] = hWriter[s];
            // Only a world record's point is known before it is resolved; hitResolved tells receivers when it is not.
            kWorld[s] = MetaKind(hMeta[s]) == KindWorld ? hLocal[s] : Vector3.zero;
            arrived[s] = false;
            arriveAt[s] = -1f;
            slotReplay[s] = false;
        }

        private void RetireSlot(int s)
        {
            DropSlot(s);
            hMeta[s] = 0;
            hPayload[s] = 0;
            hLocal[s] = Vector3.zero;
            hWriter[s] = 0;
        }

        // Local: the slot no longer holds what this client knew. Receivers hear it only if they had heard the hit.
        private void DropSlot(int s)
        {
            if (slotSeq[s] == 0) return;
            bool wasArrived = arrived[s];
            if (wasArrived) LoadHit(s);
            slotSeq[s] = 0;
            arriveAt[s] = -1f;
            arrived[s] = false;
            for (int i = 0; i < emitterCount; i++) if (flightOn[i] && flightSlot[i] == s) StopFlight(i);
            if (wasArrived) NotifyGone();
        }

        // ================= Records =================

        private int Pack(int e, int kind, int bone, int index, bool instant)
        {
            int b = bone < 0 ? BoneNoneCode : (bone & BoneMask);
            int i = kind == KindMoving || kind == KindPlayer ? index & IndexMask : 0;
            int m = ((e & EmitterMask) << EmitterShift) | ((kind & KindMask) << KindShift) | (b << BoneShift) | (i << IndexShift);
            if (instant) m |= InstantBit;
            return m;
        }

        private int MetaEmitter(int m) { return (m >> EmitterShift) & EmitterMask; }

        private int MetaKind(int m) { return (m >> KindShift) & KindMask; }

        private int MetaBone(int m)
        {
            int b = (m >> BoneShift) & BoneMask;
            return b == BoneNoneCode ? -1 : b;
        }

        private int MetaIndex(int m)
        {
            int kind = MetaKind(m);
            if (kind != KindMoving && kind != KindPlayer) return -1;
            return (m >> IndexShift) & IndexMask;
        }

        private bool MetaInstant(int m) { return (m & InstantBit) != 0; }

        private void LoadHit(int s)
        {
            int m = kMeta[s];
            hitSlot = s;
            hitSeq = slotSeq[s];
            hitEmitter = MetaEmitter(m);
            hitKind = MetaKind(m);
            hitIndex = MetaIndex(m);
            hitBone = MetaBone(m);
            hitLocal = kLocal[s];
            hitPayload = kPayload[s];
            hitStream = MetaInstant(m);
            hitReplay = slotReplay[s];
            hitResolved = Resolve(m, kLocal[s]);
            if (hitResolved) kWorld[s] = wish;
            hitPoint = kWorld[s];
            hitDistance = ValidEmitter(hitEmitter) ? Vector3.Distance(emitters[hitEmitter].position, hitPoint) : 0f;
        }

        // World point of a record into wish. False = cannot be resolved now (player gone, mover missing).
        private bool Resolve(int m, Vector3 local)
        {
            int kind = MetaKind(m);
            if (kind == KindWorld) { wish = local; return true; }
            if (kind == KindMoving)
            {
                int k = MetaIndex(m);
                if (movers == null || k < 0 || k >= movers.Length || movers[k] == null) return false;
                wish = movers[k].TransformPoint(local);
                return true;
            }
            if (kind == KindPlayer)
            {
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(MetaIndex(m));
                if (!Utilities.IsValid(player)) return false;
                int b = MetaBone(m);
                if (b >= 0)
                {
                    // Read the bone pose directly: a Transform driven by a constraint would trail by a frame.
                    HumanBodyBones bone = (HumanBodyBones)b;
                    Vector3 pos = player.GetBonePosition(bone);
                    if (pos != Vector3.zero) { wish = pos + player.GetBoneRotation(bone) * local; return true; }
                }
                wish = player.GetPosition() + player.GetRotation() * local;
                return true;
            }
            return false;
        }

        // ================= Cast =================

        // Nearest touch along origin + dir * (0..range): world colliders not in this rig, then players. Fills the cast result.
        public bool _CastPath(Vector3 origin, Vector3 dir, float range) { return Cast(origin, dir, range); }

        // _CastPath's distance along the path (valid right after it returned true).
        public float _CastDistance() { return castDistance; }

        private bool Cast(Vector3 origin, Vector3 dir, float range)
        {
            castKind = KindNone;
            castIndex = -1;
            castLocal = Vector3.zero;
            castPoint = origin;
            castDistance = 0f;
            castPlayer = null;
            if (range <= 0f) return false;
            int count = Physics.SphereCastNonAlloc(origin, castRadius, dir, castHits, range, hitMask.value & ~PlayerLayers, QueryTriggerInteraction.Ignore);
            int best = -1;
            float reach = range;
            Transform self = transform;
            for (int i = 0; i < count && i < castHits.Length; i++)
            {
                Transform t = castHits[i].transform;
                if (t == null || t.IsChildOf(self)) continue;
                if (best < 0 || castHits[i].distance < reach)
                {
                    best = i;
                    reach = castHits[i].distance;
                }
            }
            if (hitPlayers && PlayerTouch(origin, dir, reach)) return true;
            if (best < 0) return false;
            // A cast that starts inside a collider reports distance 0 and a zero point.
            castPoint = castHits[best].distance > 0f ? castHits[best].point : origin;
            castDistance = castHits[best].distance;
            int mover = MoverOf(castHits[best].transform);
            if (mover >= 0)
            {
                castKind = KindMoving;
                castIndex = mover;
                castLocal = movers[mover].InverseTransformPoint(castPoint);
                return true;
            }
            castKind = KindWorld;
            castLocal = castPoint;
            return true;
        }

        private int MoverOf(Transform t)
        {
            if (movers == null || t == null) return -1;
            for (int k = 0; k < movers.Length; k++) if (movers[k] != null && t.IsChildOf(movers[k])) return k;
            return -1;
        }

        // Feet, head and radius of every player, once per frame (the stream casts many paths in one frame).
        private void ReadPlayers()
        {
            int frame = Time.frameCount;
            if (frame == pFrame) return;
            pFrame = frame;
            int count = VRCPlayerApi.GetPlayerCount();
            if (players.Length < count)
            {
                int size = count + 8;
                players = new VRCPlayerApi[size];
                pFeet = new Vector3[size];
                pHead = new Vector3[size];
                pRadius = new float[size];
            }
            VRCPlayerApi.GetPlayers(players);
            pCount = Mathf.Min(count, players.Length);
            for (int p = 0; p < pCount; p++)
            {
                VRCPlayerApi player = players[p];
                pRadius[p] = -1f;
                if (!Utilities.IsValid(player)) continue;
                if (excludeOwner && player.isLocal) continue;
                if (player.playerId > MaxTargetIndex) continue;
                float eye = Mathf.Max(player.GetAvatarEyeHeightAsMeters(), 0.2f);
                Vector3 feet = player.GetPosition();
                Vector3 head = player.GetBonePosition(HumanBodyBones.Head);
                if (head == Vector3.zero) head = feet + Vector3.up * eye;
                pFeet[p] = feet;
                pHead[p] = head;
                pRadius[p] = Mathf.Clamp(eye * playerRadius, BodyRadiusMin, BodyRadiusMax) + castRadius;
            }
        }

        // Player colliders do not follow the avatar, so each body is a capsule from the feet to the head instead.
        private bool PlayerTouch(Vector3 origin, Vector3 dir, float reach)
        {
            ReadPlayers();
            int best = -1;
            float bestAlong = reach;
            Vector3 bestPoint = Vector3.zero;
            for (int p = 0; p < pCount; p++)
            {
                float radius = pRadius[p];
                if (radius < 0f) continue;
                float along = SegmentApproach(origin, dir, reach, pFeet[p], pHead[p]);
                if (along >= bestAlong) continue;
                Vector3 onShot = origin + dir * along;
                Vector3 onBody = ClosestOnSegment(pFeet[p], pHead[p], onShot);
                if ((onShot - onBody).sqrMagnitude > radius * radius) continue;
                best = p;
                bestAlong = along;
                bestPoint = onShot;
            }
            if (best < 0 || !Utilities.IsValid(players[best])) return false;
            castKind = KindPlayer;
            castPlayer = players[best];
            castIndex = castPlayer.playerId;
            castPoint = bestPoint;
            castDistance = bestAlong;
            return true;
        }

        // Distance along the shot (0..reach) where it comes closest to segment a-b.
        private float SegmentApproach(Vector3 origin, Vector3 dir, float reach, Vector3 a, Vector3 b)
        {
            Vector3 seg = b - a;
            float ss = Vector3.Dot(seg, seg);
            if (ss < 0.000001f) return Mathf.Clamp(Vector3.Dot(a - origin, dir), 0f, reach);
            Vector3 w = origin - a;
            float ds = Vector3.Dot(dir, seg);
            float denom = ss - ds * ds;
            float along = denom > 0.000001f ? (ds * Vector3.Dot(seg, w) - ss * Vector3.Dot(dir, w)) / denom : 0f;
            along = Mathf.Clamp(along, 0f, reach);
            // Clamping the body end can move the best point on the shot: settle it once more.
            Vector3 onBody = ClosestOnSegment(a, b, origin + dir * along);
            return Mathf.Clamp(Vector3.Dot(onBody - origin, dir), 0f, reach);
        }

        private Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 seg = b - a;
            float ss = Vector3.Dot(seg, seg);
            if (ss < 0.000001f) return a;
            return a + seg * Mathf.Clamp01(Vector3.Dot(p - a, seg) / ss);
        }

        private int NearestBone(VRCPlayerApi player, Vector3 point)
        {
            int best = -1;
            float bestSq = 1e30f;
            for (int i = 0; i < probeBones.Length; i++)
            {
                Vector3 pos = player.GetBonePosition((HumanBodyBones)probeBones[i]);
                if (pos == Vector3.zero) continue;
                float sq = (pos - point).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = probeBones[i];
                }
            }
            return best;
        }

        private bool Blocked(Vector3 origin, Vector3 dir, float dist)
        {
            int count = Physics.SphereCastNonAlloc(origin, castRadius, dir, castHits, dist, hitMask.value & ~PlayerLayers, QueryTriggerInteraction.Ignore);
            Transform self = transform;
            for (int i = 0; i < count && i < castHits.Length; i++)
            {
                Transform t = castHits[i].transform;
                if (t != null && !t.IsChildOf(self) && castHits[i].distance < dist - castRadius * 2f) return true;
            }
            return false;
        }

        // ================= Single-shot flights (every client) =================

        private void Launch(int s)
        {
            int e = MetaEmitter(kMeta[s]);
            arrived[s] = false;
            slotReplay[s] = false;
            if (ValidEmitter(e)) NotifyFire(e, false, false);
            if (!ValidEmitter(e) || !Resolve(kMeta[s], kLocal[s]))
            {
                arriveAt[s] = Time.time;
                return;
            }
            kWorld[s] = wish;
            Vector3 to = wish - emitters[e].position;
            float dist = to.magnitude;
            arriveAt[s] = Time.time + dist / Speed();
            if (dist > 0.0001f) Fly(e, s, to / dist, dist);
        }

        private void Fly(int e, int slot, Vector3 dir, float dist)
        {
            ParticleSystem ps = Particles(e);
            if (ps == null || !ValidEmitter(e)) return;
            if (dir.sqrMagnitude < 0.000001f) dir = emitters[e].forward;
            // The same system serves the stream; back to its authored single-shot setup (this also clears stream particles).
            if (stream != null) stream._Restore(e);
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.transform.SetPositionAndRotation(emitters[e].position, Quaternion.LookRotation(dir));
            ParticleSystem.MainModule main = ps.main;
            main.startSpeedMultiplier = Speed();
            main.startLifetimeMultiplier = dist / Speed();
            ps.Play(true);
            flightOn[e] = true;
            flightSlot[e] = slot;
            flightStart[e] = Time.time;
            flightLength[e] = dist;
            flightFlown[e] = 0f;
        }

        private void StopFlight(int e)
        {
            if (!flightOn[e]) return;
            flightOn[e] = false;
            flightSlot[e] = -1;
            flightFlown[e] = 0f;
            ParticleSystem ps = Particles(e);
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void StopAllFlights()
        {
            for (int i = 0; i < emitterCount; i++) StopFlight(i);
            if (stream != null) stream._ClearAll();
        }

        // ================= Sync =================

        public override void OnDeserialization()
        {
            Init();
            int n = Mathf.Min(slots, Mathf.Min(Mathf.Min(hMeta.Length, hPayload.Length), Mathf.Min(hLocal.Length, hWriter.Length)));
            int e = Mathf.Min(emitterCount, Mathf.Min(mSeq.Length, mDir.Length));
            float now = Time.time;
            bool first = !baselined;
            baselined = true;
            if (!first && stopSeq != seenStop) StopAllFlights();
            seenStop = stopSeq;
            int sinceJoin = Networking.GetServerTimeInMilliseconds() - joinMs;
            // Every slot is matched against the received state each time, so two clients writing the same seq (or this client
            // losing a race) can never leave a slot holding what was not kept.
            for (int s = 0; s < n; s++)
            {
                int q = SeqOfSlot(s);
                if (q <= 0 || q <= hSeq - used || MetaKind(hMeta[s]) == KindNone)
                {
                    DropSlot(s);
                    continue;
                }
                int heldSeq = slotSeq[s];
                if (heldSeq == q && SameAsHeld(s)) continue;
                DropSlot(s);
                HoldSlot(s, q);
                bool fresh;
                if (first)
                {
                    // A late joiner: what was written after it arrived (give or take) is new to it; the rest is history.
                    fresh = WriterAgeMs(kWriter[s]) <= sinceJoin + JoinMarginMs;
                }
                else if (q > headSeq) fresh = true;          // beyond anything seen here
                else if (q == heldSeq) fresh = true;         // same seq, another writer: the race was lost, this one is real
                else fresh = false;                          // an older record came back (rolled back), or one seen before
                if (!fresh)
                {
                    arriveAt[s] = now;
                    slotReplay[s] = true;
                }
                else if (MetaInstant(kMeta[s]))
                {
                    arriveAt[s] = now;
                    // The owner's stream touched here: this emitter's stream particles end here too (HitOnce).
                    int em = MetaEmitter(kMeta[s]);
                    if (!first && stream != null && streamHitMode == GunnerStream.HitOnce && ValidEmitter(em)) stream._Clear(em);
                }
                else Launch(s);
            }
            headSeq = hSeq;
            for (int i = 0; i < e; i++)
            {
                if (mSeq[i] == seenMiss[i]) continue;
                // A smaller count is a rolled-back write of this client's own: match it without a flight.
                bool newer = !first && mSeq[i] > seenMiss[i];
                seenMiss[i] = mSeq[i];
                if (newer && ValidEmitter(i))
                {
                    Fly(i, -1, mDir[i], maxRange);
                    NotifyFire(i, false, false);
                }
            }
            ApplyStreamState(first);
        }

        private bool SameAsHeld(int s)
        {
            return kMeta[s] == hMeta[s] && kPayload[s] == hPayload[s] && kLocal[s] == hLocal[s] && kWriter[s] == hWriter[s];
        }

        // The newest seq that maps to slot s (0 = none yet).
        private int SeqOfSlot(int s)
        {
            if (hSeq <= 0) return 0;
            int newestSlot = (hSeq - 1) % slots;
            int back = (newestSlot - s + slots) % slots;
            return hSeq - back;
        }

        // first = 入室後の最初の受信。その時に続いていた連射は、今始まったのではないので再生の印を付ける。
        private void ApplyStreamState(bool first)
        {
            if (streamOn && !streamWas && stream != null)
            {
                stream._Begin(Time.time);
                NotifyFire(-1, true, first);
            }
            streamWas = streamOn;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!initialized || !Utilities.IsValid(player) || !Networking.IsOwner(gameObject)) return;
            if (RetirePlayer(player.playerId)) _Commit();
        }

        // Owner: retires the records on a player (id < 0 = on every player who is no longer here). True = something changed.
        private bool RetirePlayer(int id)
        {
            bool changed = false;
            for (int s = 0; s < slots; s++)
            {
                if (MetaKind(hMeta[s]) != KindPlayer) continue;
                int pid = MetaIndex(hMeta[s]);
                if (id >= 0 ? pid != id : Utilities.IsValid(VRCPlayerApi.GetPlayerById(pid))) continue;
                RetireSlot(s);
                changed = true;
            }
            return changed;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            Init();
            if (Utilities.IsValid(player))
            {
                if (!player.isLocal)
                {
                    streamMine = false;
                    dirty = false;
                    aimPending = false;
                }
                else
                {
                    baselined = true;
                    EnsureSyncArrays();
                    // What the previous owner had is already out; only what this client writes from now on is unsent.
                    sentSeq = hSeq;
                    if (hSeq > headSeq) headSeq = hSeq;
                    bool send = false;
                    // A stream nobody here started (its owner left, or this client took the rig to fire): stop it.
                    if (streamOn && !streamMine)
                    {
                        streamOn = false;
                        streamWas = false;
                        send = true;
                    }
                    // The old owner may have left before its own OnPlayerLeft cleanup ran: tidy records on absent players.
                    if (RetirePlayer(-1)) dirty = true;
                    if (dirty) _Commit();
                    else if (send) SendNow();
                }
            }
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnGunnerOwner(this);
        }

        private void SendNow()
        {
            nextSendAt = Time.time + SendInterval;
            RequestSerialization();
        }

        public override void OnPreSerialization() { pendingSeq = hSeq; }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success) sentSeq = pendingSeq;
        }

        // A joiner shoots only after its first state, so the owner sends one to every joiner (even an empty one).
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!initialized || !Utilities.IsValid(player) || player.isLocal || !Networking.IsOwner(gameObject)) return;
            if (dirty) _Commit();
            else SendNow();
        }

        // ================= Per frame =================

        private void Update()
        {
            // Before this frame's particle simulation: on a stall the joined trails must lengthen first, or points are lost.
            if (streamBusy && stream != null) stream._PreSimulate(Time.time, Time.deltaTime);
        }

        public override void PostLateUpdate()
        {
            if (!initialized) return;
            float now = Time.time;
            bool owner = Networking.IsOwner(gameObject);

            // 1. Mounts, bone link, aim: the emitters take this frame's pose. The owner's aim is the synced one for everyone else.
            for (int i = 0; i < emitterCount; i++) ApplyMount(i);
            int mode = aimMode;
            Vector3 vec = aimVector;
            if (mode == AimNone && !owner)
            {
                mode = aimSyncMode;
                vec = aimSync;
            }
            StepLinks(Time.deltaTime, mode, vec);
            ApplyAimAll(mode, vec);

            // 2. Stream, then the throttled send (stream records and aim share it).
            if (stream != null) streamBusy = stream._Step(now, streamOn, owner, owner && collideOn);
            if (owner && AimSynced() && AimChanged())
            {
                aimSyncMode = (byte)aimMode;
                aimSync = aimVector;
                aimPending = true;
            }
            else if (owner && !AimSynced() && aimSyncMode != AimNone)
            {
                // A stale aim (the old owner's, or one left by a stop that did not send it) would keep remote emitters turned.
                aimSyncMode = (byte)AimNone;
                aimPending = true;
            }
            // Records about to be overwritten before anyone got them go out at once (not waiting for the interval).
            bool urgent = dirty && WouldEvictUnsent();
            if (owner && (dirty || aimPending) && (now >= nextSendAt || urgent) && !Networking.IsClogged)
            {
                aimPending = false;
                if (dirty) _Commit();
                else SendNow();
            }

            // 3. Flights and arrivals.
            float speed = Speed();
            for (int i = 0; i < emitterCount; i++)
            {
                if (!flightOn[i]) continue;
                flightFlown[i] = (now - flightStart[i]) * speed;
                if (flightFlown[i] >= flightLength[i])
                {
                    flightOn[i] = false;
                    flightSlot[i] = -1;
                }
            }
            for (int s = 0; s < slots; s++)
            {
                if (slotSeq[s] == 0 || arrived[s] || arriveAt[s] < 0f || now < arriveAt[s]) continue;
                arrived[s] = true;
                arriveAt[s] = -1f;
                LoadHit(s);
                NotifyHit();
                slotReplay[s] = false;
            }

            // 4 and 5. End points, then the late-step receivers (skipped when none wants them).
            GunnerHitReceiver[] late = lateReceivers;
            if (late.Length == 0) return;
            for (int s = 0; s < slots; s++)
            {
                int em = MetaEmitter(kMeta[s]);
                bool live = slotSeq[s] != 0 && arrived[s] && ValidEmitter(em) && Resolve(kMeta[s], kLocal[s]);
                hitLive[s] = live;
                if (!live) continue;
                hitStart[s] = emitters[em].position;
                hitWorld[s] = wish;
                kWorld[s] = wish;
            }
            for (int i = 0; i < late.Length; i++) if (late[i] != null) late[i]._OnGunnerLateStep(this);
        }

        // Compared with the last value put in the synced fields (sent or waiting).
        private bool AimChanged()
        {
            if (aimMode != aimSyncMode) return true;
            if (aimMode == AimDirection) return Vector3.Angle(aimVector, aimSync) > AimSyncAngle;
            if (aimMode == AimTarget) return (aimVector - aimSync).sqrMagnitude > AimSyncMove * AimSyncMove;
            if (aimMode == AimAngles) return (aimVector - aimSync).sqrMagnitude > AimSyncAngle * AimSyncAngle;
            return false;
        }

        // ================= Aim and bone link =================

        private void ApplyMount(int i)
        {
            Transform mount = Mount(i);
            if (mount != null && emitters[i] != null) emitters[i].SetPositionAndRotation(mount.position, mount.rotation);
        }

        // The direction (mode, vec) asks of emitter i, inside its cone, into wish. False = no aim.
        private bool Wish(int mode, Vector3 vec, int i)
        {
            Vector3 w;
            if (mode == AimDirection) w = vec;
            else if (mode == AimTarget) w = vec - emitters[i].position;
            else if (mode == AimAngles)
            {
                if (!ValidEmitter(0)) return false;
                w = RestWorld(0) * (Quaternion.Euler(-vec.y, vec.x, 0f) * Vector3.forward);
            }
            else return false;
            if (w.sqrMagnitude < 0.000001f) return false;
            w = w.normalized;
            Vector3 rest = RestWorld(i) * Vector3.forward;
            if (Vector3.Angle(rest, w) > aimMaxAngle) w = Vector3.RotateTowards(rest, w, aimMaxAngle * Mathf.Deg2Rad, 0f);
            wish = w;
            return true;
        }

        // Rest rotation in world, with a held linked bone taken as it would be without the aim turn.
        private Quaternion RestWorld(int i)
        {
            Transform r = restRef[i];
            if (r == null) return restRel[i];
            int b = linkOf[i];
            if (b < 0 || !linkHeld[b]) return r.rotation * restRel[i];
            Transform up = r.parent;
            Quaternion upRot = up != null ? up.rotation : Quaternion.identity;
            return upRot * linkBase[b] * restRel[i];
        }

        // Every client. The follow angle eases toward the aim; the bone takes a capped share of it. The inputs are the aim and the
        // rest frame only (never the turned bone), so nothing drifts.
        private void StepLinks(float dt, int mode, Vector3 vec)
        {
            bool on = BoneOn();
            for (int b = 0; b < links.Length; b++)
            {
                Transform bone = links[b];
                if (bone == null) continue;
                int lead = linkLead[b];
                Quaternion local = bone.localRotation;
                if (!linkHeld[b])
                {
                    if (!on || mode == AimNone) continue;
                    linkBase[b] = local;
                    linkHeld[b] = true;
                    followYaw[b] = 0f;
                    followPitch[b] = 0f;
                }
                else if (local != linkWritten[b]) linkBase[b] = local; // an Animator wrote it: that is the new base
                float ty = 0f;
                float tp = 0f;
                bool aimed = on && mode != AimNone && ValidEmitter(lead) && Wish(mode, vec, lead);
                if (aimed)
                {
                    Vector3 v = Quaternion.Inverse(RestWorld(lead)) * wish;
                    ty = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    tp = Mathf.Atan2(v.y, Mathf.Sqrt(v.x * v.x + v.z * v.z)) * Mathf.Rad2Deg;
                }
                float dy = ty - followYaw[b];
                float dp = tp - followPitch[b];
                float left = Mathf.Sqrt(dy * dy + dp * dp);
                float step = Mathf.Max(boneMinSpeed, left * boneSmooth) * dt;
                if (left <= step)
                {
                    followYaw[b] = ty;
                    followPitch[b] = tp;
                }
                else
                {
                    followYaw[b] = followYaw[b] + dy * step / left;
                    followPitch[b] = followPitch[b] + dp * step / left;
                }
                if (!aimed && followYaw[b] == 0f && followPitch[b] == 0f)
                {
                    bone.localRotation = linkBase[b];
                    linkHeld[b] = false;
                    turnYaw[b] = 0f;
                    turnPitch[b] = 0f;
                    continue;
                }
                float k = Mathf.Clamp01(boneStrength);
                turnYaw[b] = Mathf.Clamp(followYaw[b] * k, -boneYawMax, boneYawMax);
                turnPitch[b] = Mathf.Clamp(followPitch[b] * k, -bonePitchDownMax, bonePitchUpMax);
                // The turn is expressed in the lead emitter's rest frame, then moved into the bone's local space.
                Quaternion rel = restRel[lead];
                bone.localRotation = linkBase[b] * rel * Quaternion.Euler(-turnPitch[b], turnYaw[b], 0f) * Quaternion.Inverse(rel);
                linkWritten[b] = bone.localRotation;
            }
        }

        private void ApplyAimAll(int mode, Vector3 vec)
        {
            if (mode == AimNone && !aimedOnce) return;
            bool any = false;
            for (int i = 0; i < emitterCount; i++) if (AimEmitter(i, mode, vec)) any = true;
            aimedOnce = any;
        }

        // Linked emitters follow the eased angle (the shot leaves with the bone, not ahead of it); others point at once.
        // True = the emitter is turned away from rest.
        private bool AimEmitter(int i, int mode, Vector3 vec)
        {
            Transform m = emitters[i];
            if (m == null) return false;
            int b = linkOf[i];
            Vector3 up = RestWorld(i) * Vector3.up;
            if (b >= 0 && linkHeld[b])
            {
                int lead = linkLead[b];
                Vector3 dir = RestWorld(lead) * (Quaternion.Euler(-followPitch[b], followYaw[b], 0f) * Vector3.forward);
                m.rotation = Quaternion.LookRotation(dir, up);
                return true;
            }
            if (mode != AimNone && Wish(mode, vec, i))
            {
                m.rotation = Quaternion.LookRotation(wish, up);
                return true;
            }
            if (Mount(i) == null) m.localRotation = restLocal[i];
            return false;
        }

        private bool BoneOn() { return boneMode == 0 ? boneFollow : boneMode == 2; }

        // ================= Small helpers =================

        private bool ValidEmitter(int e) { return e >= 0 && e < emitterCount && emitters[e] != null; }

        private ParticleSystem Particles(int e) { return shotParticles != null && e >= 0 && e < shotParticles.Length ? shotParticles[e] : null; }

        private Transform Mount(int e) { return mounts != null && e >= 0 && e < mounts.Length ? mounts[e] : null; }

        private float Speed() { return Mathf.Max(shotSpeed, 0.01f); }

        public int _EmitterCount() { return emitterCount; }
    }
}
