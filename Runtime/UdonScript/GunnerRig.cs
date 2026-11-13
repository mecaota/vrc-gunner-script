using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Mecaota.Gunner
{
    // 本体: 設定、同期する着弾の記録、射撃、当たり判定、単発、狙い、骨の連動、毎フレームの順番。
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
        public const int AimDirection = 1;
        public const int AimTarget = 2;
        public const int AimAngles = 3;
        public const int MaxEmitters = 16;

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
        private const float SendInterval = 0.2f;   // 間引いて送るもの(連射の着弾、狙い)は1秒に5回まで
        private const float AimSyncAngle = 2f;
        private const float AimSyncMove = 0.05f;
        private const float BodyRadiusMin = 0.08f;  // メートル。小さいアバターにも当たるように
        private const float BodyRadiusMax = 0.5f;   // メートル。大きいアバターが近くの弾を全部吸い込まないように

        // ---------------- セットアップ(エディタが書く) ----------------

        public Transform[] emitters;              // +Z = 休み方向
        public ParticleSystem[] shotParticles;    // emitters と同じ番号。World シミュレーション空間
        public Transform[] mounts;                // 射出口ごとに任意: 射出口が毎フレームこの姿勢を取る(+Z = 休み)
        public Transform[] linkBones;             // 射出口ごとに任意: 狙いで回すもの。空 = rig の中での射出口の親
        // 任意: 連動に加える追加の骨(先頭の骨の祖先に限る)。linkExtraEmitter の同じ番号が、その骨の加わる射出口。
        // 鎖 = 追加の骨(上から順)+先頭の骨。狙いの回転を骨の数で等分し、鎖の全部の骨に同じ回転を書く。空 = 今までどおり先頭の骨だけ。
        public Transform[] linkExtraBones;
        public int[] linkExtraEmitter;
        public Transform[] movers;                // 動くオブジェクト: これ(または子)への着弾はそれに付いていく
        public GunnerStream stream;               // 任意: 連射
        public GunnerHitReceiver projectile;      // payload を聞く唯一の受け手(_OnOwnerHit / _OnOwnerCommit)
        public GunnerHitReceiver[] receivers;     // 着弾を知らされる全員。実行中は _Register で増える

        public int maxHits = 16;                  // この端末が使う記録の数(1..SyncSlots)。Android では下げる。同期は16のまま
        public float shotSpeed = 25f;
        public float maxRange = 30f;
        public float castRadius = 0.05f;
        public LayerMask hitMask = ~(1 << 2);     // プレイヤーのレイヤーはここでは無視する。hitPlayers で決める
        public bool hitPlayers = true;
        // この端末のプレイヤー、つまり当たり判定を走らせる人の体を飛ばす。判定は rig の持ち主で走るので、実際には
        // 撃つ人が自分の弾に当たることはない(自分の骨に付けた射出口、後ろ向きに撃った弾)。見ている人が持ち主の
        // 罠も、その人には当たらない。
        public bool excludeOwner = true;
        public float playerRadius = 0.12f;        // 体の半径。的の目の高さに対する割合
        public int fireMode = FireSingle;         // TriggerDown / TriggerUp で何をするか
        public float aimMaxAngle = 60f;           // 休み方向からの角度

        public float streamRate = 12f;            // 射出口ごとの1秒あたりの粒子数
        public float streamWidth = 0.02f;         // つないだ線の幅(メートル)
        public AnimationCurve streamTaper;        // 1本のトレイルに沿った幅(空 = 一定)
        public int streamJoin = GunnerStream.JoinLine;
        public int streamHitMode = GunnerStream.HitOnce;
        public float streamHitInterval = 0.25f;   // HitKeep: 射出口ごとの記録の間隔(秒)
        public bool streamCollide = true;         // 開始時の当たり判定(持ち主の端末だけ。_SetCollision で変わる)

        public bool boneFollow;                   // 初期値。BoneFollowOn / Off で変えると同期する
        public float boneStrength = 1f;           // 狙いのうち骨が受け持つ割合
        public float boneYawMax = 30f;
        public float bonePitchUpMax = 30f;
        public float bonePitchDownMax = 15f;
        // 鎖全体で曲がる合計の上限(度)。0 = 骨1本あたりの上限 × 骨の数。0 より大きい時は、小さいほうが効く。
        public float chainYawLimit;
        public float chainPitchUpLimit;
        public float chainPitchDownLimit;
        public float boneSmooth = 10f;            // 1秒あたり: 残りの角度を詰める割合
        public float boneMinSpeed = 30f;          // 最低でも1秒あたりこの角度

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
        // その弾の payload。当たった弾は記録の値(発射物が書き換えた後)、外れた弾は撃った時の値、連射は連射の値。
        // 外れた弾の値は rig に1つしか同期しないので、送る前に別の値で撃ち直すと、ほかの端末では後の値になる。
        [System.NonSerialized] public int firePayload;

        // ---------------- 撃つ側が書く(実行中だけ。同期しない) ----------------
        // 書いた値は残り、同じ rig を撃つほかの経路(Fire()、手に持つ球)にも効く。撃った後に fireMask = -1、aimOffsets = null へ戻す。

        // 一斉射(_FireWith(-1, …)・Fire)で使う射出口の組。ビット i = 射出口 i。-1 = 全部(今までどおり)。
        // 射出口を指定した発射と連射には効かない。
        [System.NonSerialized] public int fireMask = -1;
        // 射出口ごとの狙いのずらし(度。x = 左右で右が +、y = 上下で上が +)。狙った向きにこの角度を足して撃つ。
        // null・短い・(0, 0) の射出口はずらさない。持ち主の端末で撃つ時だけ使う(着弾の点と外れた向きは同期で伝わる)。
        // 単発すべて(射出口を指定した発射も)に効き、連射には効かない。
        [System.NonSerialized] public Vector2[] aimOffsets;

        // 枠ごと。_OnGunnerLateStep の前に埋める(受け手が欲しがる時だけ)。Init の後は作り直さない。
        [System.NonSerialized] public Vector3[] hitStart;
        [System.NonSerialized] public Vector3[] hitWorld;
        [System.NonSerialized] public bool[] hitLive;

        // 射出口ごと: 飛んでいる単発の弾(見た目のスクリプト用)。
        [System.NonSerialized] public bool[] flightOn;
        [System.NonSerialized] public float[] flightFlown;
        [System.NonSerialized] public float[] flightLength;
        [System.NonSerialized] public int[] flightSlot;          // -1 = 外れ

        // _SolveFor の結果。
        [System.NonSerialized] public Vector3 solveDir;
        [System.NonSerialized] public float solveDistance;

        // ---------------- 端末ごとの状態 ----------------

        private bool initialized;
        private bool baselined;
        private int slots;
        private int emitterCount;
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

        private int aimMode;
        private Vector3 aimVector;
        private bool streamMine;
        private bool streamWas;
        private bool streamBusy;
        private bool collideOn;
        private bool dirty;             // 持ち主: 記録が変わったがまだ送っていない
        private bool aimPending;        // 持ち主: 狙いが変わったがまだ送っていない
        private float nextSendAt;

        private GunnerHitReceiver[] lateReceivers = new GunnerHitReceiver[0];

        private Transform[] restRef;    // 射出口ごと: 休みの回転の基準になる座標系
        private Quaternion[] restRel;
        private Quaternion[] restLocal; // Init で1回だけ取る。狙った後の姿勢からは取らない
        private Vector3[] restPos;
        private int[] linkOf;           // 射出口ごと: 連動の番号。-1 = なし
        private Transform[] links;      // 連動ごと: 先頭の骨
        private int[] linkLead;
        private bool[] linkHeld;
        // 連動ごとの鎖(追加の骨を上から順に、最後が先頭の骨)。chain* は全部の連動の鎖を続けて並べた配列。
        private int[] linkChainStart;
        private int[] linkChainCount;
        private Transform[] chainBone;
        private Quaternion[] chainBase;     // 骨の休みのローカル回転(Animator が書いたら取り直す)
        private Quaternion[] chainWritten;
        private Quaternion[] chainRel;      // 骨から見た、先頭の射出口の休みの座標系(起動時に1回)。先頭の骨は restRel と同じ
        // 連動ごとの、鎖のいちばん上の骨から先頭の骨までの道(間の動かない骨も含む)。休みの座標系を作るのに使う。
        private int[] linkPathStart;
        private int[] linkPathCount;
        private Transform[] pathBone;
        private int[] pathChain;            // chain* の番号。-1 = 鎖に入っていない間の骨
        private const int MaxAncestors = 64;
        private float[] followYaw;
        private float[] followPitch;
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

        // 当たり判定の結果(持ち主)。骨とその中での点は、残す着弾についてだけ求める(FinishCast)。
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

        // Init で取った休みの姿勢から休みの座標系を作る(呼び出し側が先に連動した骨を放すので、骨も休みにある)。
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
                if (mount != null) { restRef[i] = mount; continue; } // 付け替えた射出口は骨に連動させない
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
                // 連動した骨からの相対。直接の親より上の骨でも動く(間の骨は止まっているとみなす)。
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
            linkHeld = new bool[count];
            followYaw = new float[count];
            followPitch = new float[count];
            turnYaw = new float[count];
            turnPitch = new float[count];
            for (int b = 0; b < count; b++)
            {
                links[b] = found[b];
                linkLead[b] = lead[b];
            }
            BuildChains(count);
        }

        // 連動ごとの鎖と道を作る。追加の骨は先頭の骨の祖先だけを使い、祖先でない骨・別の連動が使っている骨・
        // 別の連動の先頭の骨は無視する(点検が赤で知らせる)。追加の骨が無ければ、鎖も道も先頭の骨1本だけ。
        private void BuildChains(int count)
        {
            int extraCount = linkExtraBones != null ? linkExtraBones.Length : 0;
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
                claimedBy[claimedCount] = -1; // 先頭の骨はどの鎖の追加にもならない
                claimedCount++;
            }
            Transform[] anc = new Transform[MaxAncestors];
            int chainTotal = 0;
            int pathTotal = 0;
            // 1回目: 祖先を下からたどり、この連動の追加の骨を取る。いちばん上に取れた骨までが道になる。
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
                Transform em = emitters[lead];
                Quaternion restFrame = (em.parent != null ? em.parent.rotation : Quaternion.identity) * restLocal[lead];
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

        // t が、連動 b に属する射出口の追加の骨として指定されているか。
        private bool IsExtraOf(Transform t, int b)
        {
            if (linkExtraBones == null || linkExtraEmitter == null) return false;
            int n = Mathf.Min(linkExtraBones.Length, linkExtraEmitter.Length);
            for (int k = 0; k < n; k++)
            {
                if (linkExtraBones[k] != t) continue;
                int e = linkExtraEmitter[k];
                if (e >= 0 && e < emitterCount && linkOf[e] == b) return true;
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
            // 戻してくれる PostLateUpdate が無いので、連動した骨はすぐに戻す。
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
                RestoreChain(b);
            }
        }

        // 鎖の全部の骨を休みのローカル回転へ戻す。
        private void RestoreChain(int b)
        {
            int start = linkChainStart[b];
            int end = start + linkChainCount[b];
            for (int j = start; j < end; j++) if (chainBone[j] != null) chainBone[j].localRotation = chainBase[j];
        }

        // ================= 受け手 =================

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

        // どのループも始めた時の配列をたどる: 受け手がコールバックの中で登録・解除することがあるため。
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
        private void NotifyFire(int e, bool isStream, bool replay, int payload)
        {
            fireEmitter = e;
            fireStream = isStream;
            fireReplay = replay;
            firePayload = payload;
            GunnerHitReceiver[] list = receivers;
            for (int i = 0; i < list.Length; i++) if (list[i] != null) list[i]._OnFire(this);
        }

        // ================= 公開の操作 =================

        public void Fire() { _FireWith(-1, 0); }

        public void _FireFrom(int emitter) { _FireWith(emitter, 0); }

        // emitter < 0 = fireMask の立っている射出口(既定は全部)。payload はここでは中身を見ない。弾の受け手が記録ごとに書き換えてよい。
        public void _FireWith(int emitter, int payload)
        {
            Init();
            if (emitter >= 0 && !ValidEmitter(emitter)) return;
            if (!TakeOwnership()) return;
            bool any = false;
            if (emitter >= 0) any = Shoot(emitter, payload);
            else
            {
                int mask = fireMask;
                for (int i = 0; i < emitterCount; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    if (Shoot(i, payload)) any = true;
                }
            }
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
                NotifyFire(-1, true, false, streamPayload);
            }
            aimSyncMode = (byte)aimMode;
            aimSync = aimVector;
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

        // 誰が始めたかに関係なく、動いている連射を止める(rig を取る)。
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

        // 持ち主: 自分の連射も骨の連動も狙いを受け持たなくなったら、ほかの人の画面の射出口は休みに戻る。
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

        // 持ち主だけ: 記録を1つ忘れる(呼び出し側は、いくつ引退させても最後に1回 _Commit する)。
        public void _RetireHit(int slot)
        {
            Init();
            if (slot < 0 || slot >= slots || !Networking.IsOwner(gameObject)) return;
            RetireSlot(slot);
        }

        // 持ち主だけ: 弾に片付けさせてから、記録を今すぐ送る。
        public void _Commit()
        {
            if (!Networking.IsOwner(gameObject)) return;
            dirty = false;
            if (projectile != null) projectile._OnOwnerCommit(this);
            SendNow();
        }

        // 撃つ前に rig の持ち主になっておくべき部品(例: つかんだコントローラー)のため。true = 今は持ち主。
        public bool _TakeOwnership()
        {
            Init();
            return TakeOwnership();
        }

        // 単発: 撃つ。連射: 始める。TriggerUp で止まる。
        public void TriggerDown()
        {
            if (fireMode == FireStream) StartStream();
            else Fire();
        }

        public void TriggerUp()
        {
            if (fireMode == FireStream) StopStream();
        }

        // 持ち主の端末だけ: 連射の粒子が着弾を記録してよいか。off → on では、粒子の今の位置から数え始める。
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

        // 自分で枠をたどる受け手のための記録の読み出し(この端末の写し)。
        public bool _SlotUsed(int slot) { return initialized && slot >= 0 && slot < slots && slotSeq[slot] != 0; }

        public int _SlotKind(int slot) { return _SlotUsed(slot) ? MetaKind(kMeta[slot]) : KindNone; }

        public int _SlotIndex(int slot) { return _SlotUsed(slot) ? MetaIndex(kMeta[slot]) : -1; }

        public int _SlotPayload(int slot) { return _SlotUsed(slot) ? kPayload[slot] : 0; }

        public int _SlotCount() { return slots; }

        // その点に届く射出口(射程内、円錐の中、間に何も無い)。-1 = なし。solveDir / solveDistance を設定する。
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

        // ---------------- 狙い ----------------
        // 狙っても rig は取らない。持ち主の狙いは、連射中か骨の連動が on の間は同期する。ほかの人の
        // 狙いは、その人の画面の射出口だけを回す。

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

        // 射出口0の休みの座標系での角度: 左右は右が +、上下は上が +。
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

        // 実行中に射出口 e を別の Transform に付け替える(例: 誰かの骨に付いていく点)。null で元に戻す。
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

        private bool Shoot(int e, int payload)
        {
            if (!ValidEmitter(e)) return false;
            // PostLateUpdate の外の入力から呼ばれる: 先に付け替えと狙いを今の状態にする。
            ApplyMount(e);
            AimEmitter(e, aimMode, aimVector);
            Vector3 origin = emitters[e].position;
            Vector3 dir = emitters[e].forward;
            if (aimOffsets != null && e < aimOffsets.Length)
            {
                Vector2 off = aimOffsets[e];
                if (off.x != 0f || off.y != 0f) dir = emitters[e].rotation * (Quaternion.Euler(-off.y, off.x, 0f) * Vector3.forward);
            }
            if (!Cast(origin, dir, maxRange))
            {
                mSeq[e] = mSeq[e] + 1;
                mDir[e] = dir;
                mPayload = payload;
                seenMiss[e] = mSeq[e];
                // 受け手が粒子の発射より前に firePayload を読めるように、通知を先にする(粒子を戻すのはさらに前)。
                PrepareFly(e);
                NotifyFire(e, false, false, payload);
                Fly(e, -1, dir, maxRange);
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
            if (projectile != null) projectile._OnOwnerHit(this);
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

        // 残したプレイヤーへの着弾に、一番近い骨とその骨の座標系での点を付ける(この1つの記録について、今だけ)。
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
                    // 骨の姿勢を直接読む: コンストレイントで動く Transform は1フレーム遅れるため。
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
        public bool _CastPath(Vector3 origin, Vector3 dir, float range) { return Cast(origin, dir, range); }

        // _CastPath の経路に沿った距離(true を返した直後だけ有効)。
        public float _CastDistance() { return castDistance; }

        // _CastPath で一番近かった物の種類(KindWorld / KindMoving / KindPlayer、外れは KindNone)。読むだけで状態は変えない。
        public int _CastKind() { return castKind; }

        // _CastPath で一番近かった物の番号。KindPlayer は playerId、KindMoving は movers の番号、それ以外は -1。
        public int _CastIndex() { return castIndex; }

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

        // プレイヤーのコライダーはアバターに付いていかないので、代わりに足から頭までのカプセルを体とする。
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
            arriveAt[s] = Time.time + dist / Speed();
            if (flies && dist > 0.0001f) Fly(e, s, to / dist, dist);
        }

        // 同じ粒子を連射でも使う: 作ったときの単発の設定へ戻す(連射の粒子も消える)。Fly の前、_OnFire の通知より前に呼ぶ。
        private void PrepareFly(int e)
        {
            ParticleSystem ps = Particles(e);
            if (ps == null || !ValidEmitter(e)) return;
            if (stream != null) stream._Restore(e);
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // 呼ぶ前に PrepareFly を済ませておく。
        private void Fly(int e, int slot, Vector3 dir, float dist)
        {
            ParticleSystem ps = Particles(e);
            if (ps == null || !ValidEmitter(e)) return;
            if (dir.sqrMagnitude < 0.000001f) dir = emitters[e].forward;
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
                    // 持ち主の連射がここに当たった: この射出口の連射の粒子もここで終わる(HitOnce)。
                    int em = MetaEmitter(kMeta[s]);
                    if (!first && stream != null && streamHitMode == GunnerStream.HitOnce && ValidEmitter(em)) stream._Clear(em);
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
                    Fly(i, -1, mDir[i], maxRange);
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

        private void Update()
        {
            // このフレームの粒子のシミュレーションの前: 止まった時は、つないだトレイルを先に伸ばさないと点が失われる。
            if (streamBusy && stream != null) stream._PreSimulate(Time.time, Time.deltaTime);
        }

        public override void PostLateUpdate()
        {
            if (!initialized) return;
            float now = Time.time;
            bool owner = Networking.IsOwner(gameObject);

            // 1. 付け替え、骨の連動、狙い: 射出口がこのフレームの姿勢を取る。持ち主の狙いは、ほかの全員には同期したもの。
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

            // 2. 連射、次に間引いた送信(連射の記録と狙いで共有する)。
            if (stream != null) streamBusy = stream._Step(now, streamOn, owner, owner && collideOn);
            if (owner && AimSynced() && AimChanged())
            {
                aimSyncMode = (byte)aimMode;
                aimSync = aimVector;
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
            if (aimMode == AimDirection) return Vector3.Angle(aimVector, aimSync) > AimSyncAngle;
            if (aimMode == AimTarget) return (aimVector - aimSync).sqrMagnitude > AimSyncMove * AimSyncMove;
            if (aimMode == AimAngles) return (aimVector - aimSync).sqrMagnitude > AimSyncAngle * AimSyncAngle;
            return false;
        }

        // ================= 狙いと骨の連動 =================

        private void ApplyMount(int i)
        {
            Transform mount = Mount(i);
            if (mount != null && emitters[i] != null) emitters[i].SetPositionAndRotation(mount.position, mount.rotation);
        }

        // (mode, vec) が射出口 i に求める向きを、円錐の中に収めて wish へ。false = 狙いなし。
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

        // ワールドでの休みの回転。連動して持っている骨は、狙いで回していない時の姿勢として扱う。
        private Quaternion RestWorld(int i)
        {
            Transform r = restRef[i];
            if (r == null) return restRel[i];
            int b = linkOf[i];
            if (b < 0 || !linkHeld[b]) return r.rotation * restRel[i];
            // 鎖のいちばん上の骨の親から、道の骨のローカル回転を順に掛ける(鎖の骨は休みの値、間の骨は今の値)。
            // 鎖の骨を曲げると先頭の骨の親も回るので、親の今の回転は使わない。骨が1本なら今までと同じ式になる。
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

        // 全員。追従の角度は狙いへ少しずつ寄り、骨はそのうち上限付きの割合を受け持つ。入力は狙いと
        // 休みの座標系だけ(回した骨は使わない)なので、ずれがたまらない。
        private void StepLinks(float dt, int mode, Vector3 vec)
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
                }
                else
                {
                    // Animator が書いた骨は、それを新しい基準にする(骨ごと)。
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
                    RestoreChain(b);
                    linkHeld[b] = false;
                    turnYaw[b] = 0f;
                    turnPitch[b] = 0f;
                    continue;
                }
                float k = Mathf.Clamp01(boneStrength);
                float yawLim = ChainLimit(boneYawMax, chainYawLimit, cn);
                turnYaw[b] = Mathf.Clamp(followYaw[b] * k, -yawLim, yawLim);
                turnPitch[b] = Mathf.Clamp(followPitch[b] * k, -ChainLimit(bonePitchDownMax, chainPitchDownLimit, cn),
                    ChainLimit(bonePitchUpMax, chainPitchUpLimit, cn));
                // 鎖全体の回転を n 等分する(同じ軸で角度だけ 1/n)。n 回重ねると全体の回転になる。
                Quaternion q = Quaternion.Euler(-turnPitch[b], turnYaw[b], 0f);
                if (cn > 1) q = Quaternion.Slerp(Quaternion.identity, q, 1f / cn);
                // 回転を先頭の射出口の休みの座標系で表し、各骨のローカル空間へ移す。どの骨も同じ軸・同じ角度で曲がる。
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

        // 鎖全体の上限: 骨1本あたり × 本数。chain が 0 より大きく、それより小さければ chain。
        private float ChainLimit(float perBone, float chain, int n)
        {
            float t = perBone * n;
            if (chain > 0f && chain < t) t = chain;
            return t;
        }

        private void ApplyAimAll(int mode, Vector3 vec)
        {
            if (mode == AimNone && !aimedOnce) return;
            bool any = false;
            for (int i = 0; i < emitterCount; i++) if (AimEmitter(i, mode, vec)) any = true;
            aimedOnce = any;
        }

        // 連動した射出口は寄せた角度に従う(弾は骨と一緒に出て、骨より先に行かない)。ほかはすぐに向く。
        // true = 射出口が休みから回されている。
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

        // ================= 小さな補助 =================

        private bool ValidEmitter(int e) { return e >= 0 && e < emitterCount && emitters[e] != null; }

        private ParticleSystem Particles(int e) { return shotParticles != null && e >= 0 && e < shotParticles.Length ? shotParticles[e] : null; }

        private Transform Mount(int e) { return mounts != null && e >= 0 && e < mounts.Length ? mounts[e] : null; }

        private float Speed() { return Mathf.Max(shotSpeed, 0.01f); }

        public int _EmitterCount() { return emitterCount; }
    }
}
