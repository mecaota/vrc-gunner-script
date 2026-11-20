using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Mecaota.Gunner
{
    // 本体: 設定、同期する着弾の記録、射撃、当たり判定、単発、狙い、ボーンの連動、毎フレームの順番。
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class GunnerRig : UdonSharpBehaviour
    {
        // ---------------- 定数 ----------------

        public const int KindNone = 0;
        public const int KindWorld = 1;
        public const int KindMoving = 2;
        public const int KindPlayer = 3;
        public const int FireSingle = 0;
        public const int FireStream = 1;
        public const int AimNone = 0;
        public const int AimTarget = 2;      // 1 と 3 は空き番(旧 AimDirection、AimAngles)。同期する値なので番号は詰めない
        public const int MaxEmitters = 16;
        public const float DefaultAimLimit = 60f;

        /// <summary>
        /// 同期する記録の輪の長さ。どのビルドでも同じ(PC と Android で合っていないと、輪の読み方が
        /// 変わってしまう)。maxHits は、端末が使う新しい記録の数を絞るだけ。
        /// </summary>
        public const int SyncSlots = 16;

        /// <summary>
        /// 記録に入れられる的の番号の最大(16ビット)。playerId がこれを超えるプレイヤー(とても長く続いた
        /// インスタンスだけ)はプレイヤーへの着弾として記録せず、弾は後ろにあるものへ抜ける。
        /// </summary>
        public const int MaxTargetIndex = 0xFFFF;

        // hMeta の並び(31ビット。符号ビットは使わないので値が負にならない)。
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

        // hWriter の並び: 記録を書いた人(playerId の下位12ビット)と時刻(サーバー時刻を16 ms 刻み、19ビット。
        // 約2.3時間で一周する)。同じ seq の2つの記録や、遅れて入った人にとっての新しい記録と古い記録を見分ける。
        private const int TickShift = 4;
        private const int TickMask = 0x7FFFF;
        private const int TickMs = 16;
        private const int WriterIdShift = 19;
        private const int WriterIdMask = 0xFFF;
        private const int JoinMarginMs = 1000;  // 入った時刻よりこれだけ古い記録までは、その場で見たものとして扱う

        private const int PlayerLayers = (1 << 9) | (1 << 10);
        private const int CastBuffer = 32;
        private const float PreFireTimeout = 3f;    // 撃つ前のアニメの合図(_AnimFire)を待つ秒数。過ぎたら撃たずにやめる
        private const float SendInterval = 0.2f;   // 間引いて送るもの(連射の着弾、狙い)は1秒に5回まで
        private const float AimSyncTan = 0.0087f; // tan 0.5 度。狙う点が rig からの距離のこの割合を超えて動いたら送る
        private const float BodyRadiusMin = 0.08f;  // メートル。小さいアバターにも当たるように
        private const float BodyRadiusMax = 0.5f;   // メートル。大きいアバターが近くの弾を全部吸い込まないように
        private const float ImpactProbe = 0.3f;     // メートル
        private const float ImpactLift = 0.02f;     // メートル。面の中で弾けないように
        // 暴れ止め: 休みの向きから上下にこの角度(度)を超える狙いでは、左右の角度を前の値のまま保つ。
        // 狙える角度がこれ以下の射出口では起きないので、既定の rig(60 度)は今までと同じ。
        private const float FlipPitch = 80f;
        // boneMaxSpeeds を使う時、追従の速さが 0 から上限に届くまでの時間(秒)。加速の上限 = 上限 / BoneRampTime。
        private const float BoneRampTime = 0.25f;
        // boneMaxSpeeds を使う時、目標にこの角度(度)より近く、速さも小さければ、目標に合わせて止める。
        private const float SettleAngle = 0.01f;
        // boneMaxSpeeds を使う時、1フレームの計算に使う時間の上限(秒)。処理落ちで行き過ぎないように。
        private const float BoneStepMax = 0.1f;

        // ---------------- セットアップ(エディタが書く) ----------------

        public GunnerEmitter[] muzzles;           // 並びが射出口の番号(同期する記録と GunnerPre<番号> が使う)
        public Transform[] movers;                // 動くオブジェクト: これ(または子)への着弾はそれに付いていく
        public GunnerStream stream;               // 任意: 連射
        public GunnerHitReceiver[] receivers;    // 着弾を知らされる全員。実行中は _Register で増える

        public int maxHits = 16;                  // この端末が使う記録の数(1..SyncSlots)。Android では下げる。同期は16のまま
        // 当たるレイヤーは射出口のパーティクルの Collision > Collides With(Collision を切っていても読む)。プレイヤーには常に当たる(持ち主も)
        public float playerRadius = 0.12f;        // 体の半径。的の目の高さに対する割合
        public bool boneFollow;                   // 初期値。BoneFollowOn / Off で変えると同期する
        public Animator fireAnimator;             // 任意: 撃つ前のアニメ。空 = すぐ撃つ。入れたら、全部の射出口に bool GunnerPre0, GunnerPre1, ... が要る

        // ---------------- 射出口ごとの値(_LoadMuzzles が muzzles から写す) ----------------

        // 毎フレーム読む値を、ほかの Behaviour の欄の読み出しにしないため、配列に写して使う。
        [System.NonSerialized] public Transform[] emitters;         // GunnerStream・GunnerAutoAim も読む
        [System.NonSerialized] public ParticleSystem[] shotParticles;
        private Transform[][] linkBones;
        private int[] fireModes;
        private float[] aimMaxAngles;
        // 連動ごとに、その連動の最初の射出口(linkLead)の値だけを使う。
        private float[] boneStrengths;
        private float[] boneYawMaxes;
        private float[] bonePitchUpMaxes;
        private float[] bonePitchDownMaxes;
        private float[] boneSmooths;
        private float[] boneMinSpeeds;
        // 0 より大きい時は、速さをこの上限で切り、速さの変わり方も BoneRampTime 秒で 0 から上限に届く割合までにする。
        private float[] boneMaxSpeeds;
        // 同期は持たず、各端末が発射と着弾の知らせ(NotifyFire / NotifyHit)から鳴らす。
        private AudioSource[] fireSources;
        private AudioSource[] impactSources;
        private AudioClip[] fireClips;
        private AudioClip[] impactClips;
        private float[] fireMinIntervals;
        private float[] impactMinIntervals;
        private ParticleSystem[] impactParticles;
        private int[] impactBursts;
        private float[] shotSpeeds;
        private float[] maxRanges;
        private float[] castRadii;
        private float[] streamRates;
        private int[] streamHitModes;
        private float[] streamHitIntervals;
        private int[] streamCollides;

        // ---------------- 同期(Manual) ----------------

        [UdonSynced] private int hSeq;                         // これまでに書いた記録の数。seq q の枠 = (q - 1) % SyncSlots
        [UdonSynced] private int[] hMeta = new int[0];
        [UdonSynced] private int[] hPayload = new int[0];
        [UdonSynced] private Vector3[] hLocal = new Vector3[0]; // ワールドの点、または的の座標系での点
        [UdonSynced] private int[] hWriter = new int[0];       // 書いた人と書いた時刻(上の hWriter の並び)
        [UdonSynced] private int[] mSeq = new int[0];          // 射出口ごと: 外れた弾の数
        [UdonSynced] private Vector3[] mDir = new Vector3[0];  // 射出口ごと: 最後に外れた弾の向き
        [UdonSynced] private int mPayload;                     // 最後に外れた弾の payload(全射出口で1つ)
        [UdonSynced] private int stopSeq;
        [UdonSynced] private bool streamOn;
        [UdonSynced] private int streamPayload;
        [UdonSynced] private byte aimSyncMode;
        [UdonSynced] private Vector3 aimSync;
        [UdonSynced] private ushort aimEmitSync;               // 狙う射出口の組(ビット i = 射出口 i)。0 = 全部。狙いと一緒に送る
        [UdonSynced] private byte boneMode;                    // 0 = boneFollow の設定どおり、1 = off、2 = on

        // ---------------- 受け手が読む(今の着弾) ----------------

        [System.NonSerialized] public int hitSlot = -1;
        [System.NonSerialized] public int hitSeq;
        [System.NonSerialized] public int hitEmitter = -1;
        [System.NonSerialized] public int hitKind;
        [System.NonSerialized] public int hitIndex = -1;       // 動くオブジェクトの番号、または playerId
        [System.NonSerialized] public int hitBone = -1;        // HumanBodyBones の int。-1 = プレイヤーの根元
        [System.NonSerialized] public Vector3 hitLocal;
        [System.NonSerialized] public Vector3 hitPoint;
        [System.NonSerialized] public float hitDistance;
        [System.NonSerialized] public int hitPayload;
        [System.NonSerialized] public bool hitReplay;
        [System.NonSerialized] public bool hitStream;
        // 今は的が見つからない時(プレイヤーが抜けた、動くオブジェクトが無い)は false。その時の hitPoint は最後に見たワールドの点、
        // この端末が一度も見ていなければ Vector3.zero。
        [System.NonSerialized] public bool hitResolved;

        // ---------------- 受け手が読む(_OnFire の中身) ----------------

        [System.NonSerialized] public int fireEmitter = -1;    // 連射の開始は -1(全部の射出口)
        [System.NonSerialized] public bool fireStream;         // 連射の開始なら真
        [System.NonSerialized] public bool fireReplay;         // 入室時に既に続いていた連射なら真。今撃たれたのではないので音は鳴らさない
        // その弾の payload。当たった弾は記録の値、外れた弾は撃った時の値、連射は連射の値。
        // 外れた弾の値は rig に1つしか同期しないので、送る前に別の値で撃ち直すと、ほかの端末では後の値になる。
        [System.NonSerialized] public int firePayload;

        // 枠ごと。_OnGunnerLateStep の前に埋める(受け手が欲しがる時だけ)。Init の後は作り直さない。
        [System.NonSerialized] public Vector3[] hitStart;
        [System.NonSerialized] public Vector3[] hitWorld;
        [System.NonSerialized] public bool[] hitLive;

        // 射出口ごと: 飛んでいる単発の弾(GunnerStream が読む)。
        [System.NonSerialized] public bool[] flightOn;

        // ---------------- 端末ごとの状態 ----------------

        private bool initialized;
        private bool baselined;
        private int slots;
        private int emitterCount;
        [System.NonSerialized] public int streamMask;  // 撃ち方が連射の射出口(ビット i = 射出口 i)
        private int preEmitter = -1;    // 持ち主: 撃つ前のアニメの合図を待っている射出口。-1 = 待っていない
        private int prePayload;
        private float preUntil;
        private int used;               // maxHits を範囲に収めたもの: この端末が持つ新しい記録の数
        private int headSeq;            // ここで知っている最新の seq: 最後に受け取った hSeq、またはこの端末が最後に書いたもの
        private int sentSeq;            // この端末が実際に最後にシリアライズした hSeq(そこまでの記録は送り済み)
        private int pendingSeq;         // OnPreSerialization で取った hSeq
        private int joinMs;             // この端末の rig が始まったサーバー時刻(遅れて入った人の区切り)
        private int seenStop;
        private int[] seenMiss;
        // 枠ごとに、この端末が持つ記録の自分用の写し: 古い記録を手放す時には同期の配列に次の記録が入っていることがあり、
        // 受け手には古い記録の値を聞かせる必要がある。
        private int[] slotSeq;          // 0 = 空
        private int[] kMeta;
        private int[] kPayload;
        private Vector3[] kLocal;
        private Vector3[] kWorld;       // その記録で最後に見たワールドの点
        private int[] kWriter;
        private float[] arriveAt;       // 0 未満 = 待ちなし
        private bool[] arrived;
        private bool[] slotReplay;
        private float[] flightStart;
        private float[] flightLength;
        private int[] flightSlot;       // -1 = 外れ

        private int aimMode;
        private Vector3 aimVector;
        private int aimEmitters;        // この端末の狙う射出口の組(_SetAimEmitters で決める)。0 = 全部
        private float[] aimHoldYaw;     // 射出口ごと: 暴れ止めで保つ左右の角度(狙える角度が FlipPitch を超える射出口だけ使う)
        private bool streamMine;
        private bool streamWas;
        private bool collideOn = true;
        private int collideMask;
        private bool dirty;             // 持ち主: 記録が変わったがまだ送っていない
        private bool aimPending;        // 持ち主: 狙いが変わったがまだ送っていない
        private float nextSendAt;

        private GunnerHitReceiver[] lateReceivers = new GunnerHitReceiver[0];

        private Transform[] restRef;    // 射出口ごと: 休みの回転の基準になる座標系
        private Quaternion[] restRel;
        private Quaternion[] restLocal; // Init で1回だけ取る。狙った後の姿勢からは取らない
        private int[] linkOf;           // 射出口ごと: 連動の番号。-1 = なし
        private Transform[] links;      // 連動ごと: 先頭のボーン
        private int[] linkLead;
        private bool[] linkHeld;
        // 連動ごとの鎖(追加のボーンを上から順に、最後が先頭のボーン)。chain* は全部の連動の鎖を続けて並べた配列。
        private int[] linkChainStart;
        private int[] linkChainCount;
        private Transform[] chainBone;
        private Quaternion[] chainBase;     // ボーンの休みのローカル回転(Animator が書いたら取り直す)
        private Quaternion[] chainWritten;
        private Quaternion[] chainRel;      // ボーンから見た、先頭の射出口の休みの座標系(起動時に1回)。先頭のボーンは restRel と同じ
        // 連動ごとの、鎖のいちばん上のボーンから先頭のボーンまでの道(間の動かないボーンも含む)。休みの座標系を作るのに使う。
        private int[] linkPathStart;
        private int[] linkPathCount;
        private Transform[] pathBone;
        private int[] pathChain;            // chain* の番号。-1 = 鎖に入っていない間のボーン
        private const int MaxAncestors = 64;
        private float[] followYaw;
        private float[] followPitch;
        // 連動ごと: boneMaxSpeeds を使う時の追従の速度(2つの値の1秒あたりの変化)。
        private float[] followVelYaw;
        private float[] followVelPitch;
        private float[] turnYaw;
        private float[] turnPitch;
        private bool aimedOnce;         // 射出口が回されていて、休みへ1回戻す必要がある

        private int[] probeBones = { 0, 7, 8, 54, 9, 10, 13, 15, 17, 14, 16, 18, 1, 3, 5, 2, 4, 6 };
        private Vector3 wish;           // Wish / Resolve の結果(U# のユーザーのメソッドは out 引数を使えない)

        // プレイヤー。そのフレームのすべての判定のために、フレームごとに1回読む。
        private VRCPlayerApi[] players = new VRCPlayerApi[16];
        private Vector3[] pFeet = new Vector3[16];
        private Vector3[] pHead = new Vector3[16];
        private float[] pRadius = new float[16];
        private int pCount;
        private int pFrame = -1;

        private RaycastHit[] castHits = new RaycastHit[CastBuffer];

        // 当たり判定の結果(持ち主)。ボーンとその中での点は、残す着弾についてだけ求める(FinishCast)。
        private int castKind;
        private int castIndex;
        private Vector3 castPoint;
        private Vector3 castLocal;
        private float castDistance;
        private VRCPlayerApi castPlayer;
        // 連射が残した判定(そのフレームで一番よい接触)。
        private int keepKind;
        private int keepIndex;
        private Vector3 keepPoint;
        private Vector3 keepLocal;
        private float keepDistance;
        private VRCPlayerApi keepPlayer;
        private int recBone;            // FinishCast の結果

        // ================= 起動と終了 =================

        private void Start()
        {
            Init();
            // 持ち主以外は最初の状態を待つ。持ち主は入ってきた人ごとに送り直す(OnPlayerJoined)。
            if (Networking.IsOwner(gameObject)) baselined = true;
        }

        private void Init()
        {
            if (initialized) return;
            initialized = true;
            slots = SyncSlots;
            used = Mathf.Clamp(maxHits, 1, SyncSlots);
            joinMs = Networking.GetServerTimeInMilliseconds();
            emitterCount = muzzles != null ? Mathf.Min(muzzles.Length, MaxEmitters) : 0;
            _LoadMuzzles();
            if (receivers == null) receivers = new GunnerHitReceiver[0];
            for (int i = 0; i < emitterCount; i++)
            {
                if (Number(streamCollides, i, 1) != 0) collideMask |= 1 << i;
                if (Number(fireModes, i, FireSingle) == FireStream) streamMask |= 1 << i;
            }
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
            flightLength = new float[emitterCount];
            flightSlot = new int[emitterCount];
            flightStart = new float[emitterCount];
            for (int i = 0; i < emitterCount; i++) flightSlot[i] = -1;
            aimHoldYaw = new float[emitterCount];

            restLocal = new Quaternion[emitterCount];
            for (int i = 0; i < emitterCount; i++) if (emitters[i] != null) restLocal[i] = emitters[i].localRotation;
            InitAimFrames();
            RecountLate();
            if (stream != null) stream._Init(this);
        }

        /// <summary>muzzles の値を射出口ごとの配列へ写す。エディタは Play 中の変更を効かせるために呼ぶ(射出口の数、撃ち方、連動、連射が読む値は起動時のまま)。</summary>
        public void _LoadMuzzles()
        {
            int n = emitterCount;
            emitters = new Transform[n];
            shotParticles = new ParticleSystem[n];
            linkBones = new Transform[n][];
            fireModes = new int[n];
            aimMaxAngles = new float[n];
            boneStrengths = new float[n];
            boneYawMaxes = new float[n];
            bonePitchUpMaxes = new float[n];
            bonePitchDownMaxes = new float[n];
            boneSmooths = new float[n];
            boneMinSpeeds = new float[n];
            boneMaxSpeeds = new float[n];
            fireSources = new AudioSource[n];
            impactSources = new AudioSource[n];
            fireClips = new AudioClip[n];
            impactClips = new AudioClip[n];
            fireMinIntervals = new float[n];
            impactMinIntervals = new float[n];
            impactParticles = new ParticleSystem[n];
            impactBursts = new int[n];
            shotSpeeds = new float[n];
            maxRanges = new float[n];
            castRadii = new float[n];
            streamRates = new float[n];
            streamHitModes = new int[n];
            streamHitIntervals = new float[n];
            streamCollides = new int[n];
            for (int i = 0; i < n; i++)
            {
                GunnerEmitter m = muzzles[i];
                linkBones[i] = new Transform[0];
                if (m == null) continue;
                emitters[i] = m.pivot;
                shotParticles[i] = m.shotParticle;
                if (m.linkBones != null) linkBones[i] = m.linkBones;
                fireModes[i] = m.fireMode;
                aimMaxAngles[i] = m.aimMaxAngle;
                boneStrengths[i] = m.boneStrength;
                boneYawMaxes[i] = m.boneYawMax;
                bonePitchUpMaxes[i] = m.bonePitchUpMax;
                bonePitchDownMaxes[i] = m.bonePitchDownMax;
                boneSmooths[i] = m.boneSmooth;
                boneMinSpeeds[i] = m.boneMinSpeed;
                boneMaxSpeeds[i] = m.boneMaxSpeed;
                fireSources[i] = m.fireSource;
                impactSources[i] = m.impactSource;
                fireClips[i] = m.fireClip;
                impactClips[i] = m.impactClip;
                fireMinIntervals[i] = m.fireMinInterval;
                impactMinIntervals[i] = m.impactMinInterval;
                impactParticles[i] = m.impactParticle;
                impactBursts[i] = m.impactBurst;
                shotSpeeds[i] = m.shotSpeed;
                maxRanges[i] = m.maxRange;
                castRadii[i] = m.castRadius;
                streamRates[i] = m.streamRate;
                streamHitModes[i] = m.streamHitMode;
                streamHitIntervals[i] = m.streamHitInterval;
                streamCollides[i] = m.streamCollide;
            }
        }

        // 受け取った配列は、大きさが合っていれば残す。Init より前に届いた状態を消さないため。
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

        // Init で取った休みの姿勢から休みの座標系を作る(呼び出し側が先に連動したボーンを放すので、ボーンも休みにある)。
        private void InitAimFrames()
        {
            int e = emitterCount;
            restRef = new Transform[e];
            restRel = new Quaternion[e];
            linkOf = new int[e];
            Transform[] found = new Transform[e];
            int[] lead = new int[e];
            int count = 0;
            for (int i = 0; i < e; i++)
            {
                linkOf[i] = -1;
                restRel[i] = Quaternion.identity;
                Transform m = emitters[i];
                if (m == null) continue;
                Transform[] list = linkBones[i];
                Transform bone = list.Length > 0 ? list[list.Length - 1] : null;
                if (bone == null)
                {
                    restRef[i] = m.parent;
                    restRel[i] = restLocal[i];
                    continue;
                }
                // 先端のボーンからの相対(起動時の姿勢)。射出口がボーンの子孫でなくても使える。
                restRef[i] = bone;
                restRel[i] = Quaternion.Inverse(bone.rotation) * m.rotation;
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
            linkHeld = new bool[count];
            followYaw = new float[count];
            followPitch = new float[count];
            followVelYaw = new float[count];
            followVelPitch = new float[count];
            turnYaw = new float[count];
            turnPitch = new float[count];
            for (int b = 0; b < count; b++)
            {
                links[b] = found[b];
                linkLead[b] = lead[b];
            }
            BuildChains(count);
        }

        // 連動ごとの鎖と道を作る。追加のボーンは先頭のボーンの祖先だけを使い、祖先でないボーン・別の連動が使っているボーン・
        // 別の連動の先頭のボーンは無視する(点検が赤で知らせる)。追加のボーンが無ければ、鎖も道も先頭のボーン1本だけ。
        private void BuildChains(int count)
        {
            int extraCount = 0;
            for (int e = 0; e < emitterCount; e++) extraCount += linkBones[e].Length;
            linkChainStart = new int[count];
            linkChainCount = new int[count];
            linkPathStart = new int[count];
            linkPathCount = new int[count];
            int[] top = new int[count];
            Transform[] claimed = new Transform[count + extraCount];
            int[] claimedBy = new int[count + extraCount];
            int claimedCount = 0;
            for (int b = 0; b < count; b++)
            {
                claimed[claimedCount] = links[b];
                claimedBy[claimedCount] = -1; // 先頭のボーンはどの鎖の追加にもならない
                claimedCount++;
            }
            Transform[] anc = new Transform[MaxAncestors];
            int chainTotal = 0;
            int pathTotal = 0;
            // 1回目: 祖先を下からたどり、この連動の追加のボーンを取る。いちばん上に取れたボーンまでが道になる。
            for (int b = 0; b < count; b++)
            {
                int depth = Ancestors(links[b], anc);
                int t = -1;
                for (int a = 0; a < depth; a++)
                {
                    Transform up = anc[a];
                    if (!IsExtraOf(up, b)) continue;
                    if (ClaimIndex(up, claimed, claimedCount) >= 0) continue;
                    claimed[claimedCount] = up;
                    claimedBy[claimedCount] = b;
                    claimedCount++;
                    t = a;
                    chainTotal++;
                }
                top[b] = t;
                chainTotal++;
                pathTotal += t + 2;
            }
            chainBone = new Transform[chainTotal];
            chainBase = new Quaternion[chainTotal];
            chainWritten = new Quaternion[chainTotal];
            chainRel = new Quaternion[chainTotal];
            pathBone = new Transform[pathTotal];
            pathChain = new int[pathTotal];
            int ci = 0;
            int pi = 0;
            // 2回目: 上から順に並べる。
            for (int b = 0; b < count; b++)
            {
                Ancestors(links[b], anc);
                int lead = linkLead[b];
                Quaternion restFrame = emitters[lead].rotation;
                linkChainStart[b] = ci;
                linkPathStart[b] = pi;
                for (int a = top[b]; a >= 0; a--)
                {
                    Transform up = anc[a];
                    int at = ClaimIndex(up, claimed, claimedCount);
                    pathBone[pi] = up;
                    if (at >= 0 && claimedBy[at] == b)
                    {
                        chainBone[ci] = up;
                        chainBase[ci] = up.localRotation;
                        chainWritten[ci] = chainBase[ci];
                        chainRel[ci] = Quaternion.Inverse(up.rotation) * restFrame;
                        pathChain[pi] = ci;
                        ci++;
                    }
                    else pathChain[pi] = -1;
                    pi++;
                }
                Transform bone = links[b];
                chainBone[ci] = bone;
                chainBase[ci] = bone.localRotation;
                chainWritten[ci] = chainBase[ci];
                chainRel[ci] = restRel[lead];
                pathBone[pi] = bone;
                pathChain[pi] = ci;
                ci++;
                pi++;
                linkChainCount[b] = ci - linkChainStart[b];
                linkPathCount[b] = pi - linkPathStart[b];
            }
        }

        // t の親から上へ、祖先を into に入れる(下から順)。数を返す。
        private int Ancestors(Transform t, Transform[] into)
        {
            int n = 0;
            Transform p = t != null ? t.parent : null;
            while (p != null && n < into.Length)
            {
                into[n] = p;
                n++;
                p = p.parent;
            }
            return n;
        }

        // t が、連動 b に属する射出口の、先端より上のボーンとして指定されているか。
        private bool IsExtraOf(Transform t, int b)
        {
            for (int e = 0; e < emitterCount; e++)
            {
                if (linkOf[e] != b) continue;
                Transform[] list = linkBones[e];
                for (int k = 0; k < list.Length - 1; k++) if (list[k] == t) return true;
            }
            return false;
        }

        private int ClaimIndex(Transform t, Transform[] claimed, int count)
        {
            for (int i = 0; i < count; i++) if (claimed[i] == t) return i;
            return -1;
        }

        private void OnDisable()
        {
            // 戻してくれる PostLateUpdate が無いので、連動したボーンはすぐに戻す。
            if (initialized) ReleaseLinks();
            StopSources(fireSources);
            StopSources(impactSources);
        }

        private void ReleaseLinks()
        {
            if (links == null) return;
            for (int b = 0; b < links.Length; b++)
            {
                followYaw[b] = 0f;
                followPitch[b] = 0f;
                followVelYaw[b] = 0f;
                followVelPitch[b] = 0f;
                turnYaw[b] = 0f;
                turnPitch[b] = 0f;
                if (!linkHeld[b]) continue;
                linkHeld[b] = false;
                RestoreChain(b);
            }
        }

        // 鎖の全部のボーンを休みのローカル回転へ戻す。
        private void RestoreChain(int b)
        {
            int start = linkChainStart[b];
            int end = start + linkChainCount[b];
            for (int j = start; j < end; j++) if (chainBone[j] != null) chainBone[j].localRotation = chainBase[j];
        }

        // ================= 受け手 =================

        /// <summary>実行中に受け手を receivers に足す(同じ受け手は二重に足さない)。</summary>
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

        // wantsLateStep は毎フレームではなくここで読む。後で変える受け手は、これをもう一度呼ぶ。
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

        // どのループも始めた時の配列をたどる: 受け手がコールバックの中で登録することがあるため。
        private void NotifyHit()
        {
            PlayImpact();
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnHit(this);
        }

        private void NotifyGone()
        {
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnHitGone(this);
        }

        // 粒子の有無に関係なく、各端末で発射の瞬間に1回ずつ呼ぶ(粒子の無い rig でも音を出せるように)。
        private void NotifyFire(int e, bool isStream, bool replay, int payload)
        {
            fireEmitter = e;
            fireStream = isStream;
            fireReplay = replay;
            firePayload = payload;
            PlayFireSound(e, replay);
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnFire(this);
        }

        // ================= 音 =================

        // 発射音の音源は動かさない。e < 0(連射の開始)は、持ち主の狙う組の連射の射出口のうち発射音の音源がある先頭の射出口。
        // 組は同期した aimEmitSync で見る(ほかの端末の aimEmitters はその端末自身の組)。
        // 入室時に既に続いていた連射(replay)は今撃たれたのではないので鳴らさない。
        private void PlayFireSound(int e, bool replay)
        {
            if (replay) return;
            int at = e;
            if (at < 0)
            {
                for (int i = 0; i < emitterCount; i++)
                {
                    if (!ValidEmitter(i) || (StreamSet(aimEmitSync) & (1 << i)) == 0 || Source(fireSources, i) == null) continue;
                    at = i;
                    break;
                }
            }
            if (!ValidEmitter(at)) return;
            PlaySound(Source(fireSources, at), Clip(fireClips, at), Value(fireMinIntervals, at, 0f));
        }

        private void PlayImpact()
        {
            if (hitReplay || !hitResolved) return;
            EmitImpactParticles();
            AudioSource source = Source(impactSources, hitEmitter);
            if (PlaySound(source, Clip(impactClips, hitEmitter), Value(impactMinIntervals, hitEmitter, 0f))) source.transform.position = hitPoint;
        }

        // 着弾の記録には面の向きが無いので、Raycast で取り直す。
        private void EmitImpactParticles()
        {
            ParticleSystem ps = Particles(impactParticles, hitEmitter);
            if (ps == null) return;
            Vector3 point = hitPoint;
            Vector3 incoming = Vector3.forward;
            if (ValidEmitter(hitEmitter))
            {
                Vector3 to = point - emitters[hitEmitter].position;
                if (to.sqrMagnitude > 0.000001f) incoming = to.normalized;
            }
            Vector3 normal = -incoming;
            RaycastHit hit;
            if (hitKind != KindPlayer
                && Physics.Raycast(point - incoming * ImpactProbe, incoming, out hit, ImpactProbe * 2f, HitMask(hitEmitter), QueryTriggerInteraction.Ignore))
            {
                normal = hit.normal;
                point = hit.point;
            }
            ps.transform.SetPositionAndRotation(point + normal * ImpactLift, Quaternion.LookRotation(normal));
            ps.Emit(Mathf.Max(1, Number(impactBursts, hitEmitter, 0)));
        }

        // 鳴っている途中でも、最短間隔を過ぎていれば頭から鳴らし直す。PlayOneShot は音ごとに位置を変えられないので使わない。
        private bool PlaySound(AudioSource source, AudioClip clip, float minInterval)
        {
            if (source == null || (source.isPlaying && source.time < minInterval)) return false;
            source.Stop();
            if (clip != null) source.clip = clip;
            source.Play();
            return true;
        }

        private void StopSources(AudioSource[] sources)
        {
            if (sources == null) return;
            for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].Stop();
        }

        // ================= 公開の操作 =================

        /// <summary>単発の一斉射(狙う射出口の組のうち、撃ち方が単発の射出口。payload は 0)。</summary>
        public void Fire() { _FireWith(-1, 0); }

        // emitter < 0 = 一斉射(狙う射出口の組のうち、撃ち方が単発の射出口)。emitter >= 0 は撃ち方や組に関係なく、その射出口で
        // 単発を撃つ(組の外の射出口は休みの向きで撃つ)。payload はここでは中身を見ない。弾の受け手が記録ごとに書き換えてよい。
        public void _FireWith(int emitter, int payload)
        {
            Init();
            if (emitter < 0)
            {
                _FireVolley(payload, ~streamMask, null);
                return;
            }
            if (!ValidEmitter(emitter)) return;
            if (fireAnimator != null)
            {
                // 撃つ前のアニメを流し、その合図(_AnimFire)で撃つ。待つのは1度に1つ。
                if (preEmitter >= 0 || !TakeOwnership()) return;
                preEmitter = emitter;
                prePayload = payload;
                preUntil = Time.time + PreFireTimeout;
                fireAnimator.SetBool("GunnerPre" + emitter, true);
                return;
            }
            if (!TakeOwnership()) return;
            if (Shoot(emitter, payload, null)) _Commit();
        }

        // 撃つ前のアニメの Animation Event(SendCustomEvent)から呼ぶ。アニメを流した全端末に届くが、撃つのは待っている持ち主の端末だけ。
        public void _AnimFire()
        {
            if (preEmitter < 0 || !Networking.IsOwner(gameObject)) return;
            int e = preEmitter;
            int p = prePayload;
            ClearPreFire();
            if (Shoot(e, p, null)) _Commit();
        }

        /// <summary>撃つ前のアニメの合図を待っている間は真。</summary>
        public bool _IsPreFiring() { return preEmitter >= 0; }

        private void ClearPreFire()
        {
            fireAnimator.SetBool("GunnerPre" + preEmitter, false);
            preEmitter = -1;
        }

        // 一斉射を引数で決めて撃つ。rig の欄は何も書き換えない(撃った後に戻すものは無い)。
        // mask: 使う射出口の組(ビット i = 射出口 i。-1 = 全部)。狙う射出口の組(_SetAimEmitters)と両方に入っている射出口だけが撃つ。
        // offsets: 射出口ごとの狙いのずらし(度。x = 左右で右が +、y = 上下で上が +)。狙った後の向きにこの角度を足して撃つ。
        //   null・短い・(0, 0) の射出口はずらさない。可動域では切らない。持ち主の端末で撃つ時だけ使う(着弾の点と外れた向きは同期で伝わる)。
        public void _FireVolley(int payload, int mask, Vector2[] offsets)
        {
            Init();
            if (!TakeOwnership()) return;
            bool any = false;
            for (int i = 0; i < emitterCount; i++)
            {
                if ((mask & (1 << i)) == 0 || !InAim(i, aimEmitters)) continue;
                if (Shoot(i, payload, offsets)) any = true;
            }
            if (any) _Commit();
        }

        /// <summary>連射を始める(payload は 0)。stream が空なら何もしない。</summary>
        public void StartStream() { _StartStreamWith(0); }

        /// <summary>payload 付きで連射を始める(rig を取る)。狙う組に連射の射出口が無ければ何もしない。もう連射中なら payload と狙いだけを送り直す。</summary>
        public void _StartStreamWith(int payload)
        {
            Init();
            if (stream == null || StreamSet(aimEmitters) == 0 || !TakeOwnership()) return;
            streamMine = true;
            streamPayload = payload;
            aimSyncMode = (byte)aimMode;
            aimSync = aimVector;
            aimEmitSync = (ushort)aimEmitters;
            if (!streamOn)
            {
                streamOn = true;
                streamWas = true;
                stream._Begin(Time.time);
                NotifyFire(-1, true, false, streamPayload);
            }
            aimPending = false;
            SendNow();
        }

        // この端末が始めた連射だけを止める: 遅れて届いた放す操作で、ほかの人の新しい連射を止めてはいけない。
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

        // 持ち主: 自分の連射もボーンの連動も狙いを受け持たなくなったら、ほかの人の画面の射出口は休みに戻る。
        private void ClearSyncedAimIfUnused()
        {
            if (AimSynced()) return;
            aimSyncMode = (byte)AimNone;
            aimPending = false;
        }

        private bool AimSynced() { return (streamOn && streamMine) || (BoneOn() && links.Length > 0); }

        // 飛んでいるものを止める: まだ届いていない弾は引退させ、届いた着弾は残す。
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

        /// <summary>届いた着弾も飛んでいる弾も、記録を全部消す(rig を取る)。</summary>
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

        // 持ち主だけ: 記録を今すぐ送る。
        public void _Commit()
        {
            if (!Networking.IsOwner(gameObject)) return;
            dirty = false;
            SendNow();
        }

        // 撃つ前に rig の持ち主になっておくべき部品(例: つかんだコントローラー)のため。true = 今は持ち主。
        public bool _TakeOwnership()
        {
            Init();
            return TakeOwnership();
        }

        /// <summary>連射の射出口は連射を始め(TriggerUp で止まる)、単発の射出口は1回撃つ。</summary>
        public void TriggerDown()
        {
            StartStream();
            Fire();
        }

        /// <summary>この端末が始めた連射を止める。</summary>
        public void TriggerUp() { StopStream(); }

        // 持ち主の端末だけ: 連射の粒子が着弾を記録してよいか。off → on では、粒子の今の位置から数え始める。
        public void _SetCollision(bool on)
        {
            Init();
            if (on && !collideOn && stream != null) stream._ResetCollision(Time.time);
            collideOn = on;
        }

        /// <summary>連射中なら true(誰が始めたかは問わない。同期した値)。</summary>
        public bool _IsStreaming() { return streamOn; }

        /// <summary>連射の粒子がまだ射程の中を飛んでいれば true。</summary>
        public bool _IsStreamInFlight() { return initialized && stream != null && stream._InFlight(Time.time); }

        /// <summary>ボーンの連動が今 on なら true。</summary>
        public bool _IsBoneFollowing() { return BoneOn(); }

        // ---------------- 狙い ----------------
        // 狙っても rig は取らない。持ち主の狙いは、連射中かボーンの連動が on の間は同期する。ほかの人の
        // 狙いは、その人の画面の射出口だけを回す。

        /// <summary>ワールドの点を狙う。射出口ごとにその点へ向く。</summary>
        public void _SetAimTarget(Vector3 worldPoint)
        {
            Init();
            aimMode = AimTarget;
            aimVector = worldPoint;
        }

        /// <summary>狙いを外す。射出口は休みの向きに戻る。</summary>
        public void ResetAim()
        {
            Init();
            aimMode = AimNone;
        }

        // 狙う射出口の組(ビット i = 射出口 i)。-1・0・有効な射出口が1つも入っていない組 = 全部(既定)。
        // 組の外の射出口は狙いを受けず休みの向きに戻り(連動したボーンはなめらかに戻る)、連射と一斉射(_FireWith(-1, …)・
        // _FireVolley)も組の中の射出口だけから出る。射出口を指定した _FireWith(番号, …) は組の外でも休みの向きで撃つ。
        // 狙いと同じ扱い: rig は取らず、持ち主の組は狙いと一緒に同期する(組が変わったら狙いが変わったものとして送る)。
        // 組は1つの狙いを受ける射出口を決めるだけで、射出口ごとに別の点を狙うことはできない。組を替えた後の待ち
        // (行ったり来たりを止める時間)は rig が持たないので、組を使う側が持つ。
        public void _SetAimEmitters(int mask)
        {
            Init();
            int all = emitterCount >= 16 ? 0xFFFF : (1 << emitterCount) - 1;
            int m = mask & all;
            if (m == all) m = 0;
            aimEmitters = m;
        }

        /// <summary>ボーンの連動を on にする(_SetBoneFollow(true))。</summary>
        public void BoneFollowOn() { _SetBoneFollow(true); }

        /// <summary>ボーンの連動を off にする(_SetBoneFollow(false))。</summary>
        public void BoneFollowOff() { _SetBoneFollow(false); }

        /// <summary>ボーンの連動を切り替える。rig を取り、全員に同期する。</summary>
        public void _SetBoneFollow(bool on)
        {
            Init();
            if (BoneOn() == on || !TakeOwnership()) return;
            boneMode = (byte)(on ? 2 : 1);
            if (!(streamOn && streamMine))
            {
                aimSyncMode = (byte)aimMode;
                aimSync = aimVector;
                aimEmitSync = (ushort)aimEmitters;
            }
            ClearSyncedAimIfUnused();
            aimPending = false;
            SendNow();
        }

        // ================= 射撃(持ち主) =================

        private bool TakeOwnership()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return false;
            // 最初の状態が届くまで、この端末の記録は空: それを書くと全員の記録を消してしまう。
            if (!baselined && !Networking.IsOwner(gameObject)) return false;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
            if (!Networking.IsOwner(gameObject)) return false;
            baselined = true;
            EnsureSyncArrays();
            return true;
        }

        private bool Shoot(int e, int payload, Vector2[] offsets)
        {
            if (!ValidEmitter(e)) return false;
            // PostLateUpdate の外の入力から呼ばれる: 先に狙いを今の状態にする。狙う組の外の射出口は休みの向き。
            AimEmitter(e, InAim(e, aimEmitters) ? aimMode : AimNone, aimVector);
            Vector3 origin = emitters[e].position;
            Vector3 dir = emitters[e].forward;
            if (offsets != null && e < offsets.Length)
            {
                Vector2 off = offsets[e];
                if (off.x != 0f || off.y != 0f) dir = emitters[e].rotation * (Quaternion.Euler(-off.y, off.x, 0f) * Vector3.forward);
            }
            if (!Cast(e, origin, dir, _Range(e)))
            {
                mSeq[e] = mSeq[e] + 1;
                mDir[e] = dir;
                mPayload = payload;
                seenMiss[e] = mSeq[e];
                // 受け手が粒子の発射より前に firePayload を読めるように、通知を先にする(粒子を戻すのはさらに前)。
                PrepareFly(e);
                NotifyFire(e, false, false, payload);
                Fly(e, -1, dir, _Range(e));
                return true;
            }
            KeepCast();
            int s = WriteRecord(e, payload, false);
            Launch(s);
            return true;
        }

        // GunnerStream、持ち主: 最後の判定を、そのフレームでここまでで一番よい接触として残す。
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

        // GunnerStream、持ち主: 残した接触を記録し、すぐに見せる。次の間引いた送信で送る。
        public void _StreamHit(int emitter)
        {
            if (!ValidEmitter(emitter) || !Networking.IsOwner(gameObject)) return;
            // まだ誰も受け取っていない記録を輪が上書きしてしまう時は、その接触を記録しない(送信は次の機会に出る。
            // PostLateUpdate を参照)。これで、ほかの人に届かない着弾をどの端末も見ることがない。
            if (WouldEvictUnsent()) return;
            int s = WriteRecord(emitter, streamPayload, true);
            arriveAt[s] = Time.time;
            dirty = true;
        }

        // 残した判定を次の枠に書く。そこにあった古い記録は、seq が変わることで先にどこでも消える。
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
            hSeq = seq;
            headSeq = seq;
            hMeta[s] = Pack(e, keepKind, recBone, keepIndex, instant);
            hPayload[s] = hitPayload;
            hLocal[s] = keepLocal;
            hWriter[s] = WriterMark();
            // インスタンスに1人だけ(または ClientSim)だと何もシリアライズされず、OnPostSerialization で sentSeq が進まない。
            // 取りこぼす相手もいないので、すべての記録を送り済みとみなす。
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

        // 記録を書いてからのミリ秒。記録の印から求める(約2.3時間までは正確で、その後は一周する)。
        private int WriterAgeMs(int mark)
        {
            int now = (Networking.GetServerTimeInMilliseconds() >> TickShift) & TickMask;
            return ((now - (mark & TickMask)) & TickMask) * TickMs;
        }

        // 端末ごと: 新しい `used` 件より古い記録はここで手放す(もっと使う端末のために同期はしたまま)。
        private void ForgetUnused()
        {
            if (used >= slots) return;
            for (int s = 0; s < slots; s++) if (slotSeq[s] != 0 && slotSeq[s] <= hSeq - used) DropSlot(s);
        }

        // 残したプレイヤーへの着弾に、一番近いボーンとそのボーンの座標系での点を付ける(この1つの記録について、今だけ)。
        private void FinishKept()
        {
            recBone = -1;
            if (keepKind != KindPlayer) return;
            if (!Utilities.IsValid(keepPlayer))
            {
                // 判定から今までにプレイヤーが抜けた: 点はワールドのまま残す。
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

        // この端末は枠 s の同期の記録を seq として持つようになった: 写して、届くのを待つ。
        private void HoldSlot(int s, int seq)
        {
            slotSeq[s] = seq;
            kMeta[s] = hMeta[s];
            kPayload[s] = hPayload[s];
            kLocal[s] = hLocal[s];
            kWriter[s] = hWriter[s];
            // 求める前に点が分かるのはワールドの記録だけ。分からない時は hitResolved で受け手に知らせる。
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

        // 端末ごと: この枠はもう、この端末が知っていたものを持っていない。着弾を聞いていた受け手にだけ知らせる。
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

        // ================= 記録 =================

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

        // 記録のワールドの点を wish へ。false = 今は求められない(プレイヤーがいない、動くオブジェクトが無い)。
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
                    // ボーンの姿勢を直接読む: コンストレイントで動く Transform は1フレーム遅れるため。
                    HumanBodyBones bone = (HumanBodyBones)b;
                    Vector3 pos = player.GetBonePosition(bone);
                    if (pos != Vector3.zero) { wish = pos + player.GetBoneRotation(bone) * local; return true; }
                }
                wish = player.GetPosition() + player.GetRotation() * local;
                return true;
            }
            return false;
        }

        // ================= 当たり判定 =================

        // origin + dir * (0..range) に沿って一番近い接触: この rig に属さないワールドのコライダー、次にプレイヤー。判定の結果を埋める。
        public bool _CastPath(int e, Vector3 origin, Vector3 dir, float range) { return Cast(e, origin, dir, range); }

        // _CastPath の経路に沿った距離(true を返した直後だけ有効)。
        public float _CastDistance() { return castDistance; }

        private bool Cast(int e, Vector3 origin, Vector3 dir, float range)
        {
            castKind = KindNone;
            castIndex = -1;
            castLocal = Vector3.zero;
            castPoint = origin;
            castDistance = 0f;
            castPlayer = null;
            if (range <= 0f) return false;
            float radius = Value(castRadii, e, 0.05f);
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, castHits, range, HitMask(e), QueryTriggerInteraction.Ignore);
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
            if (PlayerTouch(origin, dir, reach, radius)) return true;
            if (best < 0) return false;
            // コライダーの中から始めた判定は、距離0とゼロの点を返す。
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

        // 全プレイヤーの足・頭・半径を、フレームごとに1回(連射は1フレームに多くの経路を判定する)。
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
                if (player.playerId > MaxTargetIndex) continue;
                float eye = Mathf.Max(player.GetAvatarEyeHeightAsMeters(), 0.2f);
                Vector3 feet = player.GetPosition();
                Vector3 head = player.GetBonePosition(HumanBodyBones.Head);
                if (head == Vector3.zero) head = feet + Vector3.up * eye;
                pFeet[p] = feet;
                pHead[p] = head;
                pRadius[p] = Mathf.Clamp(eye * playerRadius, BodyRadiusMin, BodyRadiusMax);
            }
        }

        // プレイヤーのコライダーはアバターに付いていかないので、代わりに足から頭までのカプセルを体とする。
        private bool PlayerTouch(Vector3 origin, Vector3 dir, float reach, float shotRadius)
        {
            ReadPlayers();
            int best = -1;
            float bestAlong = reach;
            Vector3 bestPoint = Vector3.zero;
            for (int p = 0; p < pCount; p++)
            {
                if (pRadius[p] < 0f) continue;
                float radius = pRadius[p] + shotRadius;
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

        // 弾に沿った距離(0..reach)のうち、線分 a-b に一番近づく所。
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
            // 体の端を範囲に収めると弾の上の一番よい点がずれることがあるので、もう1回だけ合わせ直す。
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

        // ================= 飛んでいる単発(全員) =================

        private void Launch(int s)
        {
            int e = MetaEmitter(kMeta[s]);
            arrived[s] = false;
            slotReplay[s] = false;
            bool valid = ValidEmitter(e);
            bool resolved = valid && Resolve(kMeta[s], kLocal[s]);
            Vector3 point = wish;
            // 粒子を単発の設定へ戻すのは通知より前(受け手が _OnFire で整えた見た目を消さないため)。
            bool flies = resolved && (point - emitters[e].position).magnitude > 0.0001f;
            if (flies) PrepareFly(e);
            if (valid) NotifyFire(e, false, false, kPayload[s]);
            if (!resolved)
            {
                arriveAt[s] = Time.time;
                return;
            }
            kWorld[s] = point;
            Vector3 to = point - emitters[e].position;
            float dist = to.magnitude;
            arriveAt[s] = Time.time + dist / _Speed(e);
            if (flies && dist > 0.0001f) Fly(e, s, to / dist, dist);
        }

        // 同じ粒子を連射でも使う: 作ったときの単発の設定へ戻す(連射の粒子も消える)。Fly の前、_OnFire の通知より前に呼ぶ。
        private void PrepareFly(int e)
        {
            ParticleSystem ps = Particles(shotParticles, e);
            if (ps == null || !ValidEmitter(e)) return;
            if (stream != null) stream._Restore(e);
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 呼ぶ前に PrepareFly を済ませておく。
        private void Fly(int e, int slot, Vector3 dir, float dist)
        {
            ParticleSystem ps = Particles(shotParticles, e);
            if (ps == null || !ValidEmitter(e)) return;
            if (dir.sqrMagnitude < 0.000001f) dir = emitters[e].forward;
            ps.transform.SetPositionAndRotation(emitters[e].position, Quaternion.LookRotation(dir));
            ParticleSystem.MainModule main = ps.main;
            main.startSpeedMultiplier = _Speed(e);
            main.startLifetimeMultiplier = dist / _Speed(e);
            ps.Play(true);
            flightOn[e] = true;
            flightSlot[e] = slot;
            flightStart[e] = Time.time;
            flightLength[e] = dist;
        }

        private void StopFlight(int e)
        {
            if (!flightOn[e]) return;
            flightOn[e] = false;
            flightSlot[e] = -1;
            ParticleSystem ps = Particles(shotParticles, e);
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void StopAllFlights()
        {
            for (int i = 0; i < emitterCount; i++) StopFlight(i);
            if (stream != null) stream._ClearAll();
        }

        // ================= 同期 =================

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
            // 毎回すべての枠を受け取った状態と突き合わせる。2つの端末が同じ seq を書いても(またはこの端末が
            // 競り負けても)、残らなかったものを枠が持ち続けることはない。
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
                    // 遅れて入った人: 入ってから(だいたい)後に書かれたものは新しく、残りは過去のもの。
                    fresh = WriterAgeMs(kWriter[s]) <= sinceJoin + JoinMarginMs;
                }
                else if (q > headSeq) fresh = true;          // ここで見たどれよりも先
                else if (q == heldSeq) fresh = true;         // 同じ seq で書いた人が違う: 競り負けたので、こちらが本物
                else fresh = false;                          // 古い記録が戻ってきた(巻き戻し)、または前に見たもの
                if (!fresh)
                {
                    arriveAt[s] = now;
                    slotReplay[s] = true;
                }
                else if (MetaInstant(kMeta[s]))
                {
                    arriveAt[s] = now;
                    // 持ち主の連射がここに当たった: この射出口の連射の粒子もここで終わる(HitOnce の時)。
                    int em = MetaEmitter(kMeta[s]);
                    if (!first && stream != null && _StreamHitMode(em) == GunnerStream.HitOnce && ValidEmitter(em)) stream._Clear(em);
                }
                else Launch(s);
            }
            headSeq = hSeq;
            for (int i = 0; i < e; i++)
            {
                if (mSeq[i] == seenMiss[i]) continue;
                // 数が小さいのは、この端末自身の書き込みが巻き戻されたもの: 飛ばさずに合わせる。
                bool newer = !first && mSeq[i] > seenMiss[i];
                seenMiss[i] = mSeq[i];
                if (newer && ValidEmitter(i))
                {
                    PrepareFly(i);
                    NotifyFire(i, false, false, mPayload);
                    Fly(i, -1, mDir[i], _Range(i));
                }
            }
            ApplyStreamState(first);
        }

        private bool SameAsHeld(int s)
        {
            return kMeta[s] == hMeta[s] && kPayload[s] == hPayload[s] && kLocal[s] == hLocal[s] && kWriter[s] == hWriter[s];
        }

        // 枠 s に対応する最新の seq(0 = まだ無い)。
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
                NotifyFire(-1, true, first, streamPayload);
            }
            streamWas = streamOn;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!initialized || !Utilities.IsValid(player) || !Networking.IsOwner(gameObject)) return;
            if (RetirePlayer(player.playerId)) _Commit();
        }

        // 持ち主: プレイヤーへの記録を引退させる(id < 0 = もういない全プレイヤーへの記録)。true = 何か変わった。
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
                    // 前の持ち主が持っていたものは送り済み。これからこの端末が書くものだけが未送信。
                    sentSeq = hSeq;
                    if (hSeq > headSeq) headSeq = hSeq;
                    bool send = false;
                    // ここにいる誰も始めていない連射(持ち主が抜けた、またはこの端末が撃つために rig を取った): 止める。
                    if (streamOn && !streamMine)
                    {
                        streamOn = false;
                        streamWas = false;
                        send = true;
                    }
                    // 古い持ち主は自分の OnPlayerLeft の後片付けの前に抜けたかもしれない: いないプレイヤーへの記録を片付ける。
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

        // 入ってきた人は最初の状態の後でしか撃たないので、持ち主は入ってきた全員に送る(空でも)。
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!initialized || !Utilities.IsValid(player) || player.isLocal || !Networking.IsOwner(gameObject)) return;
            if (dirty) _Commit();
            else SendNow();
        }

        // ================= 毎フレーム =================

        public override void PostLateUpdate()
        {
            if (!initialized) return;
            float now = Time.time;
            bool owner = Networking.IsOwner(gameObject);
            // 撃つ前のアニメの合図が来ないまま期限が過ぎた、または持ち主でなくなった: 撃たずにやめる。
            if (preEmitter >= 0 && (!owner || now >= preUntil)) ClearPreFire();

            // 1. ボーンの連動、狙い: 射出口がこのフレームの姿勢を取る。持ち主の狙いは、ほかの全員には同期したもの。
            int mode = aimMode;
            Vector3 vec = aimVector;
            int emit = aimEmitters;
            if (mode == AimNone && !owner)
            {
                mode = aimSyncMode;
                vec = aimSync;
                emit = aimEmitSync;
            }
            StepLinks(Time.deltaTime, mode, vec, emit);
            ApplyAimAll(mode, vec, emit);

            // 2. 連射、次に間引いた送信(連射の記録と狙いで共有する)。連射は持ち主の狙う組の連射の射出口だけから出る。
            if (stream != null) stream._StepMasked(now, streamOn, owner, owner && collideOn ? collideMask : 0, StreamSet(owner ? aimEmitters : aimEmitSync));
            if (owner && AimSynced() && AimChanged())
            {
                aimSyncMode = (byte)aimMode;
                aimSync = aimVector;
                aimEmitSync = (ushort)aimEmitters;
                aimPending = true;
            }
            else if (owner && !AimSynced() && aimSyncMode != AimNone)
            {
                // 古い狙い(前の持ち主のもの、または送らずに止めた時に残ったもの)は、ほかの人の射出口を回したままにしてしまう。
                aimSyncMode = (byte)AimNone;
                aimPending = true;
            }
            // 誰も受け取る前に上書きされそうな記録は、間隔を待たずにすぐ送る。
            bool urgent = dirty && WouldEvictUnsent();
            if (owner && (dirty || aimPending) && (now >= nextSendAt || urgent) && !Networking.IsClogged)
            {
                aimPending = false;
                if (dirty) _Commit();
                else SendNow();
            }

            // 3. 飛んでいる弾と着弾。
            for (int i = 0; i < emitterCount; i++)
            {
                if (!flightOn[i]) continue;
                if ((now - flightStart[i]) * _Speed(i) >= flightLength[i])
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

            // 4 と 5。端点、次に毎フレームの受け手(欲しがるものが無ければ飛ばす)。
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

        // 同期のフィールドに最後に入れた値(送った、または送る待ち)と比べる。
        private bool AimChanged()
        {
            if (aimMode != aimSyncMode) return true;
            if (aimMode != AimNone && aimEmitters != aimEmitSync) return true;
            if (aimMode == AimTarget) return (aimVector - aimSync).sqrMagnitude > AimSyncTan * AimSyncTan * (aimSync - transform.position).sqrMagnitude;
            return false;
        }

        // ================= 狙いとボーンの連動 =================

        // (mode, vec) が射出口 i に求める向きを、円錐の中に収めて wish へ。false = 狙いなし。
        private bool Wish(int mode, Vector3 vec, int i)
        {
            if (mode != AimTarget) return false;
            Vector3 w = vec - emitters[i].position;
            if (w.sqrMagnitude < 0.000001f) return false;
            w = w.normalized;
            Vector3 rest = RestWorld(i) * Vector3.forward;
            float limit = AimLimit(i);
            if (Vector3.Angle(rest, w) > limit) w = Vector3.RotateTowards(rest, w, limit * Mathf.Deg2Rad, 0f);
            wish = w;
            return true;
        }

        // ワールドでの休みの回転。連動して持っているボーンは、狙いで回していない時の姿勢として扱う。
        private Quaternion RestWorld(int i)
        {
            Transform r = restRef[i];
            if (r == null) return restRel[i];
            int b = linkOf[i];
            if (b < 0 || !linkHeld[b]) return r.rotation * restRel[i];
            // 鎖のいちばん上のボーンの親から、道のボーンのローカル回転を順に掛ける(鎖のボーンは休みの値、間のボーンは今の値)。
            // 鎖のボーンを曲げると先頭のボーンの親も回るので、親の今の回転は使わない。ボーンが1本なら今までと同じ式になる。
            int start = linkPathStart[b];
            int end = start + linkPathCount[b];
            Transform topBone = pathBone[start];
            if (topBone == null) return r.rotation * restRel[i];
            Transform up = topBone.parent;
            Quaternion rot = up != null ? up.rotation : Quaternion.identity;
            for (int k = start; k < end; k++)
            {
                int j = pathChain[k];
                if (j >= 0) rot = rot * chainBase[j];
                else if (pathBone[k] != null) rot = rot * pathBone[k].localRotation;
            }
            return rot * restRel[i];
        }

        // 全員。追従の角度は狙いへ少しずつ寄り、ボーンはそのうち上限付きの割合を受け持つ。入力は狙いと
        // 休みの座標系だけ(回したボーンは使わない)なので、ずれがたまらない。
        // emit: 狙う射出口の組。先頭の射出口が組の外の連動は狙わず、休みへなめらかに戻る。
        private void StepLinks(float dt, int mode, Vector3 vec, int emit)
        {
            bool on = BoneOn();
            for (int b = 0; b < links.Length; b++)
            {
                Transform bone = links[b];
                if (bone == null) continue;
                int lead = linkLead[b];
                int cs = linkChainStart[b];
                int cn = linkChainCount[b];
                int ce = cs + cn;
                if (!linkHeld[b])
                {
                    if (!on || mode == AimNone) continue;
                    for (int j = cs; j < ce; j++) if (chainBone[j] != null) chainBase[j] = chainBone[j].localRotation;
                    linkHeld[b] = true;
                    followYaw[b] = 0f;
                    followPitch[b] = 0f;
                    followVelYaw[b] = 0f;
                    followVelPitch[b] = 0f;
                }
                else
                {
                    // Animator が書いたボーンは、それを新しい基準にする(ボーンごと)。
                    for (int j = cs; j < ce; j++)
                    {
                        Transform cb = chainBone[j];
                        if (cb == null) continue;
                        Quaternion local = cb.localRotation;
                        if (local != chainWritten[j]) chainBase[j] = local;
                    }
                }
                float ty = 0f;
                float tp = 0f;
                bool aimed = on && mode != AimNone && InAim(lead, emit) && ValidEmitter(lead) && Wish(mode, vec, lead);
                bool wide = Wide(lead);
                if (aimed && wide)
                {
                    // 狙える角度が 90 度を超える連動: 休みの前から狙いへの最短の回転を「軸と角度」で持つ。
                    // 追従の2つの値は (角度 × x/ρ, 角度 × y/ρ)(ρ = 狙いの向きの x・y の長さ)。小さい角度では左右・上下と同じ値。
                    // 左右・上下に分けると休みの後ろ側で左右が +180 と −180 の間を跳ぶが、この形で跳ぶのは真後ろ(円錐の外)の1点だけ。
                    Vector3 v = Quaternion.Inverse(RestWorld(lead)) * wish;
                    float rho = Mathf.Sqrt(v.x * v.x + v.y * v.y);
                    if (rho > 0.000001f)
                    {
                        float theta = Mathf.Atan2(rho, v.z) * Mathf.Rad2Deg;
                        ty = theta * v.x / rho;
                        tp = theta * v.y / rho;
                    }
                }
                else if (aimed)
                {
                    Vector3 v = Quaternion.Inverse(RestWorld(lead)) * wish;
                    ty = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    tp = Mathf.Atan2(v.y, Mathf.Sqrt(v.x * v.x + v.z * v.z)) * Mathf.Rad2Deg;
                    // 暴れ止め: 真上・真下に近い狙いでは左右の角度が定まらないので、左右の目標は今の値のまま保つ。
                    // 狙える角度が FlipPitch 以下なら |tp| はそれを超えないので、ここは通らない。
                    if (tp > FlipPitch || tp < -FlipPitch) ty = followYaw[b];
                }
                float dy = ty - followYaw[b];
                float dp = tp - followPitch[b];
                float left = Mathf.Sqrt(dy * dy + dp * dp);
                if (Value(boneMaxSpeeds, lead, 0f) > 0f) StepFollowLimited(b, ty, tp, dy, dp, left, dt);
                else
                {
                    float step = Mathf.Max(Value(boneMinSpeeds, lead, 30f), left * Value(boneSmooths, lead, 10f)) * dt;
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
                }
                if (!aimed && followYaw[b] == 0f && followPitch[b] == 0f)
                {
                    RestoreChain(b);
                    linkHeld[b] = false;
                    turnYaw[b] = 0f;
                    turnPitch[b] = 0f;
                    continue;
                }
                float k = Mathf.Clamp01(Value(boneStrengths, lead, 1f));
                float yawMax = Value(boneYawMaxes, lead, 30f) * cn;
                turnYaw[b] = Mathf.Clamp(followYaw[b] * k, -yawMax, yawMax);
                turnPitch[b] = Mathf.Clamp(followPitch[b] * k, -Value(bonePitchDownMaxes, lead, 15f) * cn, Value(bonePitchUpMaxes, lead, 30f) * cn);
                // 鎖全体の回転を n 等分する(同じ軸で角度だけ 1/n)。n 回重ねると全体の回転になる。
                // 狙える角度が 90 度を超える連動は、2つの値を軸と角度の回転にする(上限は今までどおり左右・上下とみなして切る)。
                Quaternion q = wide ? AxisTurn(turnYaw[b], turnPitch[b]) : Quaternion.Euler(-turnPitch[b], turnYaw[b], 0f);
                if (cn > 1) q = Quaternion.Slerp(Quaternion.identity, q, 1f / cn);
                // 回転を先頭の射出口の休みの座標系で表し、各ボーンのローカル空間へ移す。どのボーンも同じ軸・同じ角度で曲がる。
                for (int j = cs; j < ce; j++)
                {
                    Transform cb = chainBone[j];
                    if (cb == null) continue;
                    Quaternion rel = chainRel[j];
                    cb.localRotation = chainBase[j] * rel * q * Quaternion.Inverse(rel);
                    chainWritten[j] = cb.localRotation;
                }
            }
        }

        // boneMaxSpeeds を使う追従: 目標へ向かう速度を、速さの上限と加速の上限の中で寄せる。止まれる速さ(残りの角度から)を
        // 超えないので、目標が止まればほとんど行き過ぎずに追いつく(急に止めないので、わずかに行き過ぎて戻ることはある)。目標が急に跳んでも(相手が瞬間的に動いた、届いた位置が飛んだ)、
        // 射出口とボーンは上限の速さでなめらかに回る。
        private void StepFollowLimited(int b, float ty, float tp, float dy, float dp, float left, float dt)
        {
            // 処理落ちの長いフレームでも速度が大きく変わらないよう、計算に使う時間を上限で切る。
            float h = dt < BoneStepMax ? dt : BoneStepMax;
            if (h <= 0f) return;
            int lead = linkLead[b];
            float max = Value(boneMaxSpeeds, lead, 0f);
            float accel = max / BoneRampTime;
            float want = Mathf.Min(Mathf.Max(Value(boneMinSpeeds, lead, 30f), left * Value(boneSmooths, lead, 10f)), max);
            // 止まれる速さ(連続の式)と、1フレームで残りを越えない速さ(フレームの刻みの式)の両方で切る。
            want = Mathf.Min(want, Mathf.Sqrt(2f * accel * left));
            want = Mathf.Min(want, left / h);
            float wy = 0f;
            float wp = 0f;
            if (left > 0.000001f)
            {
                wy = dy / left * want;
                wp = dp / left * want;
            }
            // 速度を目標の速度へ、1フレームに accel × h まで寄せる(向きを変える時も加速の上限を守る)。
            float cy = wy - followVelYaw[b];
            float cp = wp - followVelPitch[b];
            float change = Mathf.Sqrt(cy * cy + cp * cp);
            float most = accel * h;
            if (change > most)
            {
                cy = cy * most / change;
                cp = cp * most / change;
            }
            followVelYaw[b] = followVelYaw[b] + cy;
            followVelPitch[b] = followVelPitch[b] + cp;
            followYaw[b] = followYaw[b] + followVelYaw[b] * h;
            followPitch[b] = followPitch[b] + followVelPitch[b] * h;
            float ry = ty - followYaw[b];
            float rp = tp - followPitch[b];
            // 目標を越えた(残りの向きが動く前と逆になった)か、目標のすぐ近くで速さも1フレームで止められるほど小さい時は、
            // 目標にちょうど合わせて止める(休みへ戻る時は値がちょうど 0 になり、ボーンを放せる)。
            bool passed = ry * dy + rp * dp <= 0f;
            float vy = followVelYaw[b];
            float vp = followVelPitch[b];
            bool settled = ry * ry + rp * rp < SettleAngle * SettleAngle && vy * vy + vp * vp <= most * most;
            if (passed || settled)
            {
                followYaw[b] = ty;
                followPitch[b] = tp;
                followVelYaw[b] = 0f;
                followVelPitch[b] = 0f;
            }
        }

        // emit: 狙う射出口の組。組の外の射出口は狙いなしとして扱う(連動したボーンが戻る間はボーンに従う)。
        private void ApplyAimAll(int mode, Vector3 vec, int emit)
        {
            if (mode == AimNone && !aimedOnce) return;
            bool any = false;
            for (int i = 0; i < emitterCount; i++) if (AimEmitter(i, InAim(i, emit) ? mode : AimNone, vec)) any = true;
            aimedOnce = any;
        }

        // 連動した射出口は寄せた角度に従う(弾はボーンと一緒に出て、ボーンより先に行かない)。ほかはすぐに向く。
        // true = 射出口が休みから回されている。
        private bool AimEmitter(int i, int mode, Vector3 vec)
        {
            Transform m = emitters[i];
            if (m == null) return false;
            int b = linkOf[i];
            Quaternion restRot = RestWorld(i);
            Vector3 up = restRot * Vector3.up;
            if (b >= 0 && linkHeld[b] && Wide(linkLead[b]))
            {
                // 軸と角度の連動: 同じ回転を休みの前と上に掛ける(真上を通っても上の軸が回らない。射出口のロールも最小)。
                int lead = linkLead[b];
                Quaternion leadRest = RestWorld(lead);
                Quaternion delta = leadRest * AxisTurn(followYaw[b], followPitch[b]) * Quaternion.Inverse(leadRest);
                m.rotation = Quaternion.LookRotation(delta * (leadRest * Vector3.forward), delta * up);
                return true;
            }
            if (b >= 0 && linkHeld[b])
            {
                int lead = linkLead[b];
                float fp = followPitch[b];
                Quaternion turn = Quaternion.Euler(-fp, followYaw[b], 0f);
                Quaternion leadRest = RestWorld(lead);
                Vector3 dir = leadRest * (turn * Vector3.forward);
                // 暴れ止め: 上下が FlipPitch を超える時は、上の軸をボーンの角度から作る(上の軸と向きが平行になって回らないように)。
                if (fp > FlipPitch || fp < -FlipPitch) up = leadRest * (turn * Vector3.up);
                m.rotation = Quaternion.LookRotation(dir, up);
                return true;
            }
            if (mode != AimNone && Wish(mode, vec, i))
            {
                if (Wide(i))
                {
                    // 狙える角度が 90 度を超える射出口: 休みの前から狙いへの最短の回転で向く(頭越しでロールしない)。
                    Quaternion swing = Quaternion.FromToRotation(restRot * Vector3.forward, wish);
                    m.rotation = Quaternion.LookRotation(wish, swing * up);
                    return true;
                }
                if (AimLimit(i) > FlipPitch) up = SteadyUp(i, restRot, up);
                m.rotation = Quaternion.LookRotation(wish, up);
                return true;
            }
            m.localRotation = restLocal[i];
            return false;
        }

        // 暴れ止め(連動していない射出口): wish が休みの向きから上下に FlipPitch を超える時は、前に保った左右の角度で
        // 上の軸を作る。超えない時は左右の角度を覚えるだけで、上の軸は今までどおり(境目で向きが跳ばない)。
        private Vector3 SteadyUp(int i, Quaternion restRot, Vector3 up)
        {
            Vector3 v = Quaternion.Inverse(restRot) * wish;
            float p = Mathf.Atan2(v.y, Mathf.Sqrt(v.x * v.x + v.z * v.z)) * Mathf.Rad2Deg;
            if (p > FlipPitch || p < -FlipPitch) return restRot * (Quaternion.Euler(-p, aimHoldYaw[i], 0f) * Vector3.up);
            aimHoldYaw[i] = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            return up;
        }

        // 狙える角度が 90 度を超える射出口か。その射出口(と、それを先頭とする連動)は、狙いを「軸と角度」で持つ。
        // 連動する射出口の狙える角度は、およそ 170 度まで(180 度の真後ろでは軸が決まらない)。
        private bool Wide(int i) { return AimLimit(i) > 90f; }

        // (左右, 上下) の2つの値(度)を、休みの座標系での軸と角度の回転にする。角度 = 2つの値の長さ、軸 = (−上下, 左右, 0)。
        // 小さい角度では Euler(−上下, 左右, 0) とほぼ同じ向き。
        private Quaternion AxisTurn(float yaw, float pitch)
        {
            float angle = Mathf.Sqrt(yaw * yaw + pitch * pitch);
            if (angle < 0.000001f) return Quaternion.identity;
            return Quaternion.AngleAxis(angle, new Vector3(-pitch / angle, yaw / angle, 0f));
        }

        // 射出口 i の狙える角度: aimMaxAngles に 0 より大きい値があればそれ、無ければ DefaultAimLimit。
        private float AimLimit(int i)
        {
            if (aimMaxAngles != null && i < aimMaxAngles.Length && aimMaxAngles[i] > 0f) return aimMaxAngles[i];
            return DefaultAimLimit;
        }

        // 射出口 i が狙う組に入っているか(0 = 全部)。
        private bool InAim(int i, int mask) { return mask == 0 || (mask & (1 << i)) != 0; }

        // 狙う組(0 = 全部)のうち、撃ち方が連射の射出口。
        private int StreamSet(int aim) { return (aim == 0 ? -1 : aim) & streamMask; }

        private bool BoneOn() { return boneMode == 0 ? boneFollow : boneMode == 2; }

        // ================= 小さな補助 =================

        private bool ValidEmitter(int e) { return e >= 0 && e < emitterCount && emitters[e] != null; }

        private ParticleSystem Particles(ParticleSystem[] a, int e) { return a != null && e >= 0 && e < a.Length ? a[e] : null; }

        private AudioSource Source(AudioSource[] a, int e) { return a != null && e >= 0 && e < a.Length ? a[e] : null; }

        private AudioClip Clip(AudioClip[] a, int e) { return a != null && e >= 0 && e < a.Length ? a[e] : null; }

        private float Value(float[] a, int e, float empty) { return a != null && e >= 0 && e < a.Length ? a[e] : empty; }

        private int Number(int[] a, int e, int empty) { return a != null && e >= 0 && e < a.Length ? a[e] : empty; }

        // パーティクルを入れていない射出口は弾が見えないので、物には当てない(人には当たる)。
        private int HitMask(int e)
        {
            ParticleSystem ps = Particles(shotParticles, e);
            return ps != null ? ps.collision.collidesWith.value & ~PlayerLayers : 0;
        }

        // 射出口 e の値(GunnerStream も読む)。
        public float _Speed(int e) { return Mathf.Max(Value(shotSpeeds, e, 25f), 0.01f); }

        public float _Range(int e) { return Value(maxRanges, e, 30f); }

        public float _StreamRate(int e) { return Value(streamRates, e, 12f); }

        public int _StreamHitMode(int e) { return Number(streamHitModes, e, GunnerStream.HitOnce); }

        public float _StreamHitInterval(int e) { return Value(streamHitIntervals, e, 0.25f); }

        /// <summary>使う射出口の数(emitters の長さ。最大 MaxEmitters)。</summary>
        public int _EmitterCount() { return emitterCount; }
    }
}
