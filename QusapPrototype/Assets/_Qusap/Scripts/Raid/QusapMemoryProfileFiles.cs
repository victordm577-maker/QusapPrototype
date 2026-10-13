using System;
using System.Collections.Generic;
using System.IO;

namespace Qusap
{
    // The existing profile codec/file-store still validates and commits every document.
    // This storage adapter never accesses disk; each participant owns a separate instance.
    public class QusapMemoryProfileFiles : IQusapProfileFiles
    {
        private readonly Dictionary<string, string> contents = new(StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => contents.ContainsKey(path);
        public string Read(string path) => contents[path];
        public void CreateDirectory(string path) { }
        public virtual void WriteClosedAndFlushed(string path, string value)
        { if (contents.ContainsKey(path)) throw new IOException("File already exists"); contents.Add(path, value); }
        public void Replace(string temporary, string main, string backup)
        { string value = Read(temporary); contents[backup] = Read(main); contents[main] = value; contents.Remove(temporary); }
        public void Move(string temporary, string main)
        { if (Exists(main)) throw new IOException("File already exists"); contents.Add(main, Read(temporary)); contents.Remove(temporary); }
        internal Dictionary<string, string> Capture() => new(contents, StringComparer.OrdinalIgnoreCase);
        internal void Restore(Dictionary<string, string> snapshot)
        { contents.Clear(); foreach (var pair in snapshot) contents.Add(pair.Key, pair.Value); }
    }
}
