// 連射が当たった時に、その射出口の粒子を消すかどうか(GunnerStream.Collide と GunnerRig の記録を受け取る所)を確かめる。
// 式は 2 か所の写し:
//   持ち主の端末  … 当たりが見つかった時、hitMode == HitOnce かつ streamClearOnHit なら消す
//   受け取る端末  … 最初の読み込みでなく、stream があり、streamHitMode == HitOnce かつ streamClearOnHit で、射出口の番号が正しい時に消す
// 合格の条件: 既定(streamClearOnHit = 真)では、全部の組み合わせで 0.5.0 の前(欄が無かった時)と同じ結果になる。
// 偽にした時は、どの組み合わせでも消さない。当たり続ける(HitKeep)時は、この欄の値に関係なく消さない。
using System.Text;

namespace Mecaota.Gunner.Tests.Models
{
    public static class StreamClearTest
    {
        const int HitOnce = 0;
        const int HitKeep = 1;

        // GunnerStream.Collide の写し(式を変えたら、ここも直す)
        static bool OwnerClears(int hitMode, bool clearOnHit) => hitMode == HitOnce && clearOnHit;

        // GunnerRig の記録を受け取る所の写し
        static bool ReceiverClears(bool first, bool hasStream, int hitMode, bool clearOnHit, bool validEmitter)
            => !first && hasStream && hitMode == HitOnce && clearOnHit && validEmitter;

        // 欄が無かった時の式
        static bool OldOwner(int hitMode) => hitMode == HitOnce;
        static bool OldReceiver(bool first, bool hasStream, int hitMode, bool validEmitter) => !first && hasStream && hitMode == HitOnce && validEmitter;

        public static string Run()
        {
            var sb = new StringBuilder();
            int fails = 0;
            void Check(string name, bool ok, string detail)
            {
                if (!ok) fails++;
                sb.AppendLine(name + ": " + detail + " " + (ok ? "OK" : "NG"));
            }

            // 既定(真)は前と同じ
            int same = 0, total = 0;
            foreach (int mode in new[] { HitOnce, HitKeep })
            {
                total++;
                if (OwnerClears(mode, true) == OldOwner(mode)) same++;
                foreach (bool first in new[] { false, true })
                    foreach (bool stream in new[] { false, true })
                        foreach (bool valid in new[] { false, true })
                        {
                            total++;
                            if (ReceiverClears(first, stream, mode, true, valid) == OldReceiver(first, stream, mode, valid)) same++;
                        }
            }
            Check("既定(真)", same == total, same + " / " + total + " の組み合わせが前と同じ");

            // 偽ならどこでも消さない
            int kept = 0, all = 0;
            foreach (int mode in new[] { HitOnce, HitKeep })
            {
                all++;
                if (!OwnerClears(mode, false)) kept++;
                foreach (bool first in new[] { false, true })
                    foreach (bool stream in new[] { false, true })
                        foreach (bool valid in new[] { false, true })
                        {
                            all++;
                            if (!ReceiverClears(first, stream, mode, false, valid)) kept++;
                        }
            }
            Check("偽", kept == all, kept + " / " + all + " の組み合わせで消さない");

            // 当たり続ける時は欄に関係なく消さない
            Check("当たり続ける", !OwnerClears(HitKeep, true) && !OwnerClears(HitKeep, false)
                && !ReceiverClears(false, true, HitKeep, true, true) && !ReceiverClears(false, true, HitKeep, false, true), "持ち主・受け取る側とも消さない");

            // 1回で止める・真の時に消す(今までの動き)
            Check("1回で止める・真", OwnerClears(HitOnce, true) && ReceiverClears(false, true, HitOnce, true, true), "持ち主・受け取る側とも消す");

            sb.AppendLine(fails == 0 ? "全部 OK" : "NG " + fails + " 件");
            return sb.ToString();
        }
    }
}
