using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Phototesting.PhotoMetadata.Model;

namespace Phototesting.PhotoMetadata.Runtime
{
    // Owns the server-side photo-seen index: in-memory state, dirty tracking, and persistence.
    // TryFlush is single-flight (Interlocked guard) and dispatches the actual file write through
    // TyronThreadPool.QueueTask so it never blocks the main server thread or a packet handler.
    internal sealed class ServerPhotoSeenService
    {
        private readonly string _configFileName;
        private readonly PhotoLastSeenIndex _index;
        private bool _isDirty;

        // Guards against overlapping flush tasks. Only one persist-to-disk task may be in flight.
        private int _flushInFlight;

        private ServerPhotoSeenService(string configFileName, PhotoLastSeenIndex index)
        {
            this._configFileName = configFileName;
            this._index = index;
        }

        internal static ServerPhotoSeenService LoadOrCreate(ICoreServerAPI sapi, string configFileName)
        {
            PhotoLastSeenIndex? loaded = null;
            try
            {
                loaded = sapi.LoadModConfig<PhotoLastSeenIndex>(configFileName);
            }
            catch
            {
                loaded = null;
            }

            if (loaded == null)
            {
                loaded = new PhotoLastSeenIndex();
                try
                {
                    loaded.ClampInPlace();
                    sapi.StoreModConfig(loaded, configFileName);
                }
                catch
                {
                    // ignore
                }
            }

            loaded.ClampInPlace();
            return new ServerPhotoSeenService(configFileName, loaded);
        }

        internal void Touch(string photoId)
        {
            _index.Touch(photoId);
            _isDirty = true;
        }

        // Called on the main server thread by a periodic tick listener. Snapshots the index and dispatches
        // the actual JSON serialize + disk write to the thread pool so the tick is not blocked by I/O.
        internal void TryFlush(ICoreServerAPI sapi)
        {
            if (!_isDirty) return;

            // Skip if a previous flush is still in flight; we'll retry on the next tick.
            if (Interlocked.CompareExchange(ref _flushInFlight, 1, 0) != 0) return;

            // Optimistically clear dirty; restore on failure so the next tick retries.
            _isDirty = false;

            // Snapshot under control of the main thread before handing off to the thread pool.
            // The clone keeps the thread-pool serializer isolated from concurrent main-thread Touch() writes.
            PhotoLastSeenIndex snapshot;
            try
            {
                _index.ClampInPlace();
                snapshot = new PhotoLastSeenIndex();
                foreach (KeyValuePair<string, PhotoLastSeenEntry> kvp in _index.Entries)
                {
                    if (kvp.Value == null) continue;
                    snapshot.Entries[kvp.Key] = new PhotoLastSeenEntry
                    {
                        FirstSeenUtc = kvp.Value.FirstSeenUtc,
                        LastSeenUtc = kvp.Value.LastSeenUtc
                    };
                }
            }
            catch
            {
                _isDirty = true;
                Interlocked.Exchange(ref _flushInFlight, 0);
                return;
            }

            string fileName = _configFileName;

            TyronThreadPool.QueueTask(() =>
            {
                try
                {
                    sapi.StoreModConfig(snapshot, fileName);
                }
                catch
                {
                    // Mark dirty so periodic flush retries. Main-thread visibility comes from next tick read.
                    _isDirty = true;
                }
                finally
                {
                    Interlocked.Exchange(ref _flushInFlight, 0);
                }
            }, "phototesting:SeenIndexFlush");
        }
    }
}
