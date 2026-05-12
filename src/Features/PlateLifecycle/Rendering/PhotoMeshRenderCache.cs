using Vintagestory.API.Client;

namespace Phototesting.PlateLifecycle.Rendering
{
    // Shared mesh render cache used by both item and plate photo render paths.
    // Stores uploaded MultiTextureMeshRef + atlas texture id per string cache key.
    // Versioning allows callers to detect atlas invalidation between snapshot and store.
    internal sealed class PhotoMeshRenderCache
    {
        private sealed class CachedRender
        {
            internal MultiTextureMeshRef MeshRef { get; }
            internal int TextureId { get; }

            internal CachedRender(MultiTextureMeshRef meshRef, int textureId)
            {
                MeshRef = meshRef;
                TextureId = textureId;
            }
        }

        private readonly object _syncRoot = new();
        private readonly Dictionary<string, CachedRender> _meshCache = new(StringComparer.OrdinalIgnoreCase);
        private int _atlasVersion;

        internal int GetAtlasVersionSnapshot()
        {
            lock (_syncRoot)
            {
                return _atlasVersion;
            }
        }

        internal bool TryGetCachedRender(string cacheKey, out MultiTextureMeshRef? meshRef, out int textureId)
        {
            lock (_syncRoot)
            {
                if (_meshCache.TryGetValue(cacheKey, out CachedRender? cached) && cached != null)
                {
                    meshRef = cached.MeshRef;
                    textureId = cached.TextureId;
                    return true;
                }
            }

            meshRef = null;
            textureId = 0;
            return false;
        }

        internal bool TryStore(string cacheKey, int versionSnapshot, MultiTextureMeshRef meshRef, int textureId)
        {
            lock (_syncRoot)
            {
                if (_atlasVersion != versionSnapshot)
                {
                    return false;
                }

                _meshCache[cacheKey] = new CachedRender(meshRef, textureId);
                return true;
            }
        }

        internal void DisposeAll()
        {
            lock (_syncRoot)
            {
                DisposeEntriesAndClear();
            }
        }

        internal int ClearAndBumpVersion()
        {
            lock (_syncRoot)
            {
                int cleared = DisposeEntriesAndClear();
                _atlasVersion++;
                return cleared;
            }
        }

        private int DisposeEntriesAndClear()
        {
            int cleared = 0;

            foreach (var kvp in _meshCache)
            {
                kvp.Value.MeshRef.Dispose();
                cleared++;
            }

            _meshCache.Clear();
            return cleared;
        }
    }
}
