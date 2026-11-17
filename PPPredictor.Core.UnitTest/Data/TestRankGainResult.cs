using PPPredictor.Core.DataType;

namespace UnitTests.Data
{
    [TestClass]
    public class TestRankGainResult
    {
        [TestMethod]
        public void DefaultConstuctor()
        {
            RankGainResult ppGainResult = new RankGainResult();
            Assert.IsTrue(ppGainResult.RankGlobal == -1, "RankGlobal should be -1");
            Assert.IsTrue(ppGainResult.RankCountry == -1, "RankCountry should be -1");
            Assert.AreEqual(0, ppGainResult.RankGainGlobal, "An unknown rank is not a lost place");
            Assert.AreEqual(0, ppGainResult.RankGainCountry, "An unknown rank is not a lost place");
            Assert.IsFalse(ppGainResult.HasGlobalRank);
            Assert.IsFalse(ppGainResult.HasCountryRank);
            Assert.IsFalse(ppGainResult.IsRankGainCanceledByLimit, "IsRankGainCanceledByLimit should be false");
        }
        [TestMethod]
        public void TripleConstructor()
        {
            RankGainResult ppGainResult = new RankGainResult(123, 234, new PPPPlayer());
            Assert.IsTrue(ppGainResult.RankGlobal == 123, "RankGlobal should be 123");
            Assert.IsTrue(ppGainResult.RankCountry == 234, "RankCountry should be 234");
            Assert.IsTrue(ppGainResult.RankGainGlobal == -123, "RankGainGlobal should be -123");
            Assert.IsTrue(ppGainResult.RankGainCountry == -234, "RankGainCountry should be -234");
            Assert.IsFalse(ppGainResult.IsRankGainCanceledByLimit, "IsRankGainCanceledByLimit should be false");
            ppGainResult = new RankGainResult(123, 234, new PPPPlayer(), true);
            Assert.IsTrue(ppGainResult.IsRankGainCanceledByLimit, "IsRankGainCanceledByLimit should be true");
            ppGainResult.IsRankGainCanceledByLimit = false;
            Assert.IsFalse(ppGainResult.IsRankGainCanceledByLimit, "IsRankGainCanceledByLimit should be false");
        }
        [TestMethod]
        public void QuadConstructor()
        {
            RankGainResult ppGainResult = new RankGainResult(123, 234, 345, 456);
            Assert.IsTrue(ppGainResult.RankGlobal == 123, "RankGlobal should be 123");
            Assert.IsTrue(ppGainResult.RankCountry == 234, "RankCountry should be 234");
            Assert.IsTrue(ppGainResult.RankGainGlobal == 345, "RankGainGlobal should be 345");
            Assert.IsTrue(ppGainResult.RankGainCountry == 456, "RankGainCountry should be 456");
            Assert.IsFalse(ppGainResult.IsRankGainCanceledByLimit, "IsRankGainCanceledByLimit should be false");
        }
    }
}
