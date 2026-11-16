using Mecaota.Gunner.Tests.Models;
using NUnit.Framework;

namespace Mecaota.Gunner.Tests
{
    // 狙いと連動の式の純計算のモデル(Models)。式は GunnerRig.cs の写しで、Unity の外でも Tools~/verify/build.sh が同じものを
    // 動かす。式を変えたら、モデルも同じように直す。結果の最後の行が「全部 OK」なら合格。
    public class ModelTests
    {
        static void Pass(string report)
        {
            string[] lines = report.Trim().Split('\n');
            Assert.AreEqual("全部 OK", lines[lines.Length - 1].Trim(), report);
        }

        [Test] public void Chain() => Pass(ChainMathTest.Run());
        [Test] public void EmitterSelect() => Pass(EmitterSelectTest.Run());
        [Test] public void FollowSpeed() => Pass(FollowSpeedTest.Run());
        [Test] public void RealPosition() => Pass(RealPositionTest.Run());
        [Test] public void Swing() => Pass(SwingTest.Run());
        [Test] public void Switch() => Pass(SwitchTest.Run());
        [Test] public void StreamWidth() => Pass(StreamWidthTest.Run());
    }
}
