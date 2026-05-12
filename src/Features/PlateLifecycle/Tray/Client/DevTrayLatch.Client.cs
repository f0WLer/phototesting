using Vintagestory.API.Client;

namespace Phototesting.PlateLifecycle.Tray
{
    internal sealed class DevelopmentTrayModSystemBridge
    {
        private readonly PhotoTestingModSystem _owner;
        private long? _clientDevTrayLatchTickListenerId;

        internal DevelopmentTrayModSystemBridge(PhotoTestingModSystem owner)
        {
            _owner = owner;
        }

        // Registers client listener(s) owned by DevelopmentTray latch flow.
        internal void ConfigureClientDevelopmentTrayInputListeners(ICoreClientAPI api)
        {
            _clientDevTrayLatchTickListenerId = api.Event.RegisterGameTickListener(OnClientDevTrayLatchTick, 20, 0);
        }

        // Unregisters client listener(s) owned by DevelopmentTray latch flow.
        internal void DisposeClientDevelopmentTrayTickListeners()
        {
            if (_owner.ClientApi == null) return;

            if (_clientDevTrayLatchTickListenerId.HasValue && _clientDevTrayLatchTickListenerId.Value > 0)
            {
                long id = _clientDevTrayLatchTickListenerId.Value;
                BestEffort.Try(_owner.BestEffortLogger, "unregister dev tray latch tick listener", () => _owner.ClientApi.Event.UnregisterGameTickListener(id));
                _clientDevTrayLatchTickListenerId = null;
            }
        }

        private void OnClientDevTrayLatchTick(float dt)
        {
            if (_owner.ClientApi == null) return;

            BestEffort.Try(_owner.BestEffortLogger,
                "clear dev tray release latch",
                () => DevelopmentTrayLatchRuntimeCoordinator.TryClearNeedsRelease(_owner.ClientApi));
        }
    }

    internal static class DevelopmentTrayLatchRuntimeCoordinator
    {
        // Clears tray latch only when RMB is no longer held.
        internal static void TryClearNeedsRelease(ICoreClientAPI api)
        {
            if (api == null || IsRightMouseDown(api)) return;
            TrayTimedInteractionState.ClearNeedsRelease(api.World?.Player);
        }

        // Reads right mouse state without throwing across API-version input differences.
        private static bool IsRightMouseDown(ICoreClientAPI api)
        {
            try
            {
                return api.Input.InWorldMouseButton.Right || api.Input.MouseButton.Right;
            }
            catch
            {
                return false;
            }
        }
    }
}
