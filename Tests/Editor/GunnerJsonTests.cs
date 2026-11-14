using System.Collections.Generic;
using Mecaota.Gunner.EditorTools;
using NUnit.Framework;

namespace Mecaota.Gunner.Tests
{
    // Editor の小さな JSON(GunnerJson)の読み書き。数値の JSON の道具はすべてここを通る。
    public class GunnerJsonTests
    {
        [Test]
        public void ReadsNestedValuesWithBomAndEscapes()
        {
            string text = "﻿{ \"a\": 1, \"b\": [1.5, -2e3, true, null, \"x\\\"y\\u3042\"], \"c\": { \"d\": {} } }";
            Assert.IsTrue(GunnerJson.TryParse(text, out object v, out string err), err);
            var o = v as GunnerJsonObject;
            Assert.IsNotNull(o);
            Assert.AreEqual(3, o.Count);
            Assert.IsTrue(o.TryGet("b", out object b));
            var list = b as List<object>;
            Assert.IsNotNull(list);
            Assert.AreEqual(1.5, (double)list[0]);
            Assert.AreEqual(-2000.0, (double)list[1]);
            Assert.IsTrue((bool)list[2]);
            Assert.IsNull(list[3]);
            Assert.AreEqual("x\"yあ", (string)list[4]);
        }

        [Test]
        public void WriteThenReadGivesTheSameText()
        {
            string text = "{ \"f\": 0.15, \"g\": [6, 4, 6.5], \"h\": { \"on\": true, \"name\": \"a\\\\b\" } }";
            Assert.IsTrue(GunnerJson.TryParse(text, out object v, out string err), err);
            string back = GunnerJson.Write(v);
            Assert.IsTrue(GunnerJson.TryParse(back, out object v2, out err), err);
            Assert.AreEqual(back, GunnerJson.Write(v2));
        }

        [Test]
        public void KeepsKeyOrder()
        {
            var o = new GunnerJsonObject();
            o.Set("z", 1f);
            o.Set("a", 2f);
            o.Set("m", 3f);
            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, new List<string>(o.Keys));
        }

        // 読めない JSON は全部断り、行と桁を返す(同じキーが2回、末尾の余計な文字も断る)
        [TestCase("{ \"a\": 1, }")]
        [TestCase("{ \"a\": 1, \"a\": 2 }")]
        [TestCase("{ \"a\": 1 } x")]
        [TestCase("{ \"a\": \"abc }")]
        [TestCase("{ \"a\": 01 }")]
        [TestCase("[1, 2")]
        [TestCase("{ a: 1 }")]
        [TestCase("{ \"a\": tru }")]
        [TestCase("")]
        public void RejectsBrokenJson(string text)
        {
            Assert.IsFalse(GunnerJson.TryParse(text, out _, out string err));
            Assert.IsFalse(string.IsNullOrEmpty(err));
            StringAssert.Contains(":", err);
        }
    }
}
