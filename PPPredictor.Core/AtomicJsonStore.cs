using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace PPPredictor.Core
{
    internal sealed class AtomicJsonStore
    {
        private readonly string path;
        private readonly object gate = new object();
        private long nextVersion, writtenVersion;

        internal AtomicJsonStore(string path) { this.path = Path.GetFullPath(path); }
        internal long ReserveVersion() => Interlocked.Increment(ref nextVersion);

        internal T Load<T>(Func<string, T> deserialize, Action<string, Exception> failed = null) where T : class
        {
            lock (gate)
            {
                foreach (var candidate in new[] { path, path + ".bak" })
                {
                    if (!File.Exists(candidate)) continue;
                    try
                    {
                        return deserialize(File.ReadAllText(candidate)) ?? throw new InvalidDataException("Profile is null.");
                    }
                    catch (Exception ex) { failed?.Invoke(candidate, ex); }
                }
                return null;
            }
        }

        internal bool Write(long version, string json)
        {
            if (JToken.Parse(json).Type == JTokenType.Null) throw new InvalidDataException("Profile snapshot is null.");
            lock (gate)
            {
                if (version <= writtenVersion) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var bytes = new UTF8Encoding(false).GetBytes(json);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                    else File.Move(temporary, path);
                    writtenVersion = version;
                    return true;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
    }
}
