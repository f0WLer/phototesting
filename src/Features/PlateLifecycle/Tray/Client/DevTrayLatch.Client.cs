using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Phototesting.PlateLifecycle.Tray
{
    internal sealed class DevelopmentTrayModSystemBridge
    {
        private readonly PhotoTestingModSystem _owner;
        private MouseEventDelegate? _mouseUpHandler;

        internal DevelopmentTrayModSystemBridge(PhotoTestingModSystem owner)
        {
            _owner = owner;
        }

        // Subscribes to the engine's MouseUp event so the RMB-release latch clears without polling.
        internal void ConfigureClientDevelopmentTrayInputListeners(ICoreClientAPI api)
        {
            _mouseUpHandler = (MouseEvent e) =>
            {
                if (e.Button != EnumMouseButton.Right) return;
                BestEffort.Try(_owner.BestEffortLogger,
                    "clear dev tray release latch",
                    () => TrayTimedInteractionState.ClearNeedsRelease(api.World?.Player));
            };
            api.Event.MouseUp += _mouseUpHandler;
        }

        // Unsubscribes the MouseUp listener.
        internal void DisposeClientDevelopmentTrayTickListeners()
        {
            if (_owner.ClientApi == null || _mouseUpHandler == null) return;

            var handler = _mouseUpHandler;
            BestEffort.Try(_owner.BestEffortLogger, "unsubscribe dev tray mouseup", () => _owner.ClientApi.Event.MouseUp -= handler);
            _mouseUpHandler = null;
        }
    }
}
