using System;
using System.IO;

namespace LCAnimationStudio.Runtime
{
    public enum WatchedFileState
    {
        None,
        Changed,
        Conflict
    }

    public sealed class AnimationFileWatcher : IDisposable
    {
        private readonly object sync = new object();
        private FileSystemWatcher watcher;
        private DateTime lastChangeUtc;
        private bool pending;

        public string Path { get; private set; }
        public WatchedFileState State { get; private set; }

        public void Watch(string path)
        {
            DisposeWatcher();
            Path = string.IsNullOrWhiteSpace(path) ? null : System.IO.Path.GetFullPath(path);
            State = WatchedFileState.None;
            if (Path == null) return;
            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!Directory.Exists(directory)) return;
            watcher = new FileSystemWatcher(directory, System.IO.Path.GetFileName(Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnRenamed;
        }

        public WatchedFileState Poll(bool sessionDirty, int debounceMilliseconds)
        {
            lock (sync)
            {
                if (!pending || (DateTime.UtcNow - lastChangeUtc).TotalMilliseconds < debounceMilliseconds) return State;
                pending = false;
                State = sessionDirty ? WatchedFileState.Conflict : WatchedFileState.Changed;
                return State;
            }
        }

        public void Acknowledge() => State = WatchedFileState.None;

        private void OnChanged(object sender, FileSystemEventArgs args)
        {
            lock (sync) { pending = true; lastChangeUtc = DateTime.UtcNow; }
        }

        private void OnRenamed(object sender, RenamedEventArgs args) => OnChanged(sender, args);
        public void Dispose() => DisposeWatcher();

        private void DisposeWatcher()
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }
            lock (sync) pending = false;
        }
    }
}
