using Newtonsoft.Json;
using PPPredictor.Core;

namespace UnitTests.Data
{
    [TestClass]
    public class TestAtomicJsonStore
    {
        private string directory = null!;
        private string path = null!;
        private AtomicJsonStore store = null!;
        private sealed class Profile { public int Revision { get; set; } }

        [TestInitialize]
        public void Initialize()
        {
            directory = Path.Combine(Path.GetTempPath(), "PPPredictor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "profile.json");
            store = new AtomicJsonStore(path);
        }

        [TestCleanup]
        public void Cleanup() { Directory.Delete(directory, true); }

        private void Write(int revision) => store.Write(store.ReserveVersion(), JsonConvert.SerializeObject(new Profile { Revision = revision }));

        [TestMethod]
        public void ReplacementKeepsPreviousCompleteProfileAsBackup()
        {
            Write(1);
            Write(2);
            Assert.AreEqual(2, store.Load(JsonConvert.DeserializeObject<Profile>)!.Revision);
            Assert.AreEqual(1, JsonConvert.DeserializeObject<Profile>(File.ReadAllText(path + ".bak"))!.Revision);
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }

        [TestMethod]
        public void CorruptOrNullPrimaryRecoversFromBackup()
        {
            Write(1);
            Write(2);
            foreach (var corrupt in new[] { "{broken", "null" })
            {
                File.WriteAllText(path, corrupt);
                int failures = 0;
                var profile = store.Load(JsonConvert.DeserializeObject<Profile>, (_, _) => failures++);
                Assert.IsNotNull(profile);
                Assert.AreEqual(1, profile.Revision);
                Assert.AreEqual(1, failures);
            }
        }

        [TestMethod]
        public void FailedReplacementDoesNotDamageExistingProfile()
        {
            Write(1);
            Directory.CreateDirectory(path + ".bak");
            Exception? error = null;
            try { Write(2); }
            catch (Exception ex) { error = ex; }
            Assert.IsNotNull(error);
            Assert.AreEqual(1, JsonConvert.DeserializeObject<Profile>(File.ReadAllText(path))!.Revision);
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
        }

        [TestMethod]
        public void OlderAutosaveCannotOverwriteNewerFinalSave()
        {
            var older = store.ReserveVersion();
            var newer = store.ReserveVersion();
            Assert.IsTrue(store.Write(newer, "{\"Revision\":2}"));
            Assert.IsFalse(store.Write(older, "{\"Revision\":1}"));
            Assert.AreEqual(2, store.Load(JsonConvert.DeserializeObject<Profile>)!.Revision);
        }

        [TestMethod]
        public void InvalidSnapshotDoesNotRotateGoodFiles()
        {
            Write(1);
            Write(2);
            Assert.ThrowsException<Newtonsoft.Json.JsonReaderException>(() => store.Write(store.ReserveVersion(), "{broken"));
            Assert.AreEqual(2, store.Load(JsonConvert.DeserializeObject<Profile>)!.Revision);
            Assert.AreEqual(1, JsonConvert.DeserializeObject<Profile>(File.ReadAllText(path + ".bak"))!.Revision);
        }
    }
}
