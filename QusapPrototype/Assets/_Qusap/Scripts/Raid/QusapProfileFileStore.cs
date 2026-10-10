using System;
using System.IO;
using System.Text;

namespace Qusap
{
    public enum QusapProfileLoadSource { New, Main, Backup, Recovery }

    public interface IQusapProfileFiles
    {
        bool Exists(string path);
        string Read(string path);
        void CreateDirectory(string path);
        void WriteClosedAndFlushed(string path, string contents);
        void Replace(string temporary, string main, string backup);
        void Move(string temporary, string main);
    }

    public sealed class QusapProfileFiles : IQusapProfileFiles
    {
        public bool Exists(string path) => File.Exists(path);
        public string Read(string path) => File.ReadAllText(path, Encoding.UTF8);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void WriteClosedAndFlushed(string path, string contents)
        {
            // CreateNew protects an orphaned temporary file, including incomplete diagnostic evidence.
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
        }
        public void Replace(string temporary, string main, string backup) => File.Replace(temporary, main, backup);
        public void Move(string temporary, string main) => File.Move(temporary, main);
    }

    public sealed class QusapProfileFileStore
    {
        public const string FileName = "profile-v1.json";
        private readonly IQusapProfileFiles files;
        private readonly QusapProfileCodec codec;
        private string loadedMain;
        private string loadedBackup;
        public string DirectoryPath { get; }
        public string MainPath => Path.Combine(DirectoryPath, FileName);
        public string TemporaryPath => MainPath + ".tmp";
        public string BackupPath => MainPath + ".bak";
        public bool MainExists => files.Exists(MainPath);
        public bool TemporaryExists => files.Exists(TemporaryPath);
        public bool BackupExists => files.Exists(BackupPath);
        public bool CanWrite { get; private set; } = true;
        public QusapProfileLoadSource Source { get; private set; }
        public string LastResult { get; private set; } = "Not loaded";
        public int Writes { get; private set; }

        public QusapProfileFileStore(string directory, QusapProfileCodec codec, IQusapProfileFiles files = null)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory))
                throw new ArgumentException("An absolute storage directory is required.");
            DirectoryPath = Path.GetFullPath(directory);
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.files = files ?? new QusapProfileFiles();
        }

        public QusapProfileDocument Load()
        {
            try
            {
                loadedMain = MainExists ? files.Read(MainPath) : null;
                loadedBackup = BackupExists ? files.Read(BackupPath) : null;
                bool mainValid = Decode(loadedMain, out var main, out var mainError, out bool mainFuture);
                bool backupValid = Decode(loadedBackup, out var backup, out var backupError, out bool backupFuture);
                // A future schema is never replaced with an older backup or a new empty profile.
                if (mainFuture || backupFuture)
                {
                    CanWrite = false; Source = mainValid ? QusapProfileLoadSource.Main : QusapProfileLoadSource.Recovery;
                    LastResult = "Incompatible future SchemaVersion; files preserved; writes blocked. " + mainError + "; " + backupError;
                    return mainValid ? main : null;
                }
                CanWrite = (loadedMain == null || mainValid) && (loadedBackup == null || backupValid);
                if (mainValid)
                {
                    Source = QusapProfileLoadSource.Main;
                    LastResult = CanWrite ? "Loaded Main" : "Loaded Main; corrupt Backup preserved; writes blocked";
                    return main;
                }
                if (backupValid)
                {
                    Source = QusapProfileLoadSource.Backup;
                    LastResult = "Loaded Backup; " + mainError + (CanWrite ? "" : "; corrupt Main preserved; writes blocked");
                    return backup;
                }
                Source = loadedMain == null && loadedBackup == null ? QusapProfileLoadSource.New : QusapProfileLoadSource.Recovery;
                LastResult = Source == QusapProfileLoadSource.New ? "New empty profile; no disk write"
                    : "Recovery: safe empty profile; corrupt files preserved; writes blocked. " + mainError + "; " + backupError;
                return null;
            }
            catch (Exception exception)
            {
                CanWrite = false; Source = QusapProfileLoadSource.Recovery;
                LastResult = "Controlled load failure; safe empty profile; writes blocked: " + exception.Message;
                return null;
            }
        }

        private bool Decode(string text, out QusapProfileDocument document, out string error, out bool future)
        {
            document = null; error = "Missing file"; future = false;
            return text != null && codec.TryDecode(text, out document, out error, out future);
        }

        public bool TryWrite(QusapProfileDocument document)
        {
            try
            {
                if (!CanWrite) { LastResult = "Save rejected: preserved corrupt or incompatible file requires review"; return false; }
                if (!codec.Validate(document, out string validation)) { LastResult = "Save rejected: " + validation; return false; }
                if (TemporaryExists) { LastResult = "Save rejected: orphaned .tmp preserved for diagnosis"; return false; }
                // Optimistic disk guard also prevents a second repository from overwriting another revision.
                string currentMain = MainExists ? files.Read(MainPath) : null;
                string currentBackup = BackupExists ? files.Read(BackupPath) : null;
                if (currentMain != loadedMain || currentBackup != loadedBackup)
                { LastResult = "Save rejected: files changed externally; reload required"; return false; }
                if (currentMain != null && !codec.TryDecode(currentMain, out _, out _, out _))
                { LastResult = "Save rejected: only valid Main must remain intact"; return false; }
                string json = QusapProfileCodec.Encode(document);
                files.CreateDirectory(DirectoryPath);
                files.WriteClosedAndFlushed(TemporaryPath, json);
                string readBack = files.Read(TemporaryPath);
                bool validTemporary = codec.TryDecode(readBack, out _, out string error, out _);
                if (readBack != json || !validTemporary)
                { LastResult = "Temporary validation failed; existing files preserved: " + error; return false; }
                if ((MainExists ? files.Read(MainPath) : null) != currentMain
                    || (BackupExists ? files.Read(BackupPath) : null) != currentBackup)
                { LastResult = "Save rejected: files changed during preparation; .tmp preserved"; return false; }
                if (currentMain != null) files.Replace(TemporaryPath, MainPath, BackupPath);
                else files.Move(TemporaryPath, MainPath);
                // No potentially failing file operation follows the atomic commit point.
                loadedMain = json; loadedBackup = currentMain ?? currentBackup;
                Writes++; LastResult = "Saved atomically; Revision " + document.Content.Revision;
                return true;
            }
            catch (Exception exception)
            { LastResult = "Controlled write failure; previous profile retained: " + exception.Message; return false; }
        }
    }
}
