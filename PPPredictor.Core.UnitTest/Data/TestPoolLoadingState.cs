using PPPredictor.Core;

namespace UnitTests.Data
{
    [TestClass]
    public class TestPoolLoadingState
    {
        [TestMethod]
        public void BackgroundPoolDoesNotBlockSelectedPool()
        {
            var state = new PoolLoadingState();
            state.SetLoading("no-pauses", true);
            Assert.IsFalse(state.IsLoading("general"));
            Assert.IsTrue(state.IsLoading("no-pauses"));
            state.SetLoading("general", true);
            state.SetLoading("no-pauses", false);
            Assert.IsTrue(state.IsLoading("general"));
            state.SetLoading("general", false);
            Assert.IsFalse(state.IsLoading("general"));
        }

        [TestMethod]
        public void NestedOperationsStayBusyUntilLastOneEnds()
        {
            var state = new PoolLoadingState();
            state.SetLoading("general", true);
            state.SetLoading("general", true);
            state.SetLoading("general", false);
            Assert.IsTrue(state.IsLoading("general"));
            state.SetLoading("general", false);
            state.SetLoading("general", false);
            Assert.IsFalse(state.IsLoading("general"));
            state.SetLoading("general", true);
            Assert.IsTrue(state.IsLoading("general"));
        }

        [TestMethod]
        public void BoardAndSelectionChangesUseOnlyTheSelectedState()
        {
            var beatLeader = new PoolLoadingState();
            var scoreSaber = new PoolLoadingState();
            beatLeader.SetLoading("-1", true);
            Assert.IsFalse(scoreSaber.IsLoading("-1"));
            Assert.IsFalse(beatLeader.IsLoading("-2"));
            Assert.IsTrue(beatLeader.IsLoading("-1"));
            Assert.IsFalse(beatLeader.IsLoading(null));
            beatLeader.SetLoading("-1", false);
            Assert.IsFalse(beatLeader.IsLoading("-1"));
        }
    }
}
