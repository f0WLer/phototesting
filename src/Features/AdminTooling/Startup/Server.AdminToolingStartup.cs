using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Phototesting.PlateLifecycle;

namespace Phototesting.AdminTooling
{
    // Server-side operator-tooling startup composition.
    // Keeps config bootstrap/logging details out of ModSystem callback bodies.
    internal sealed partial class AdminToolingModSystemBridge
    {
        internal void ConfigureServerOperatorToolingStartup(ICoreServerAPI api)
        {
            _owner.ApplyConfig(OperatorToolingConfigLifecycle.LoadOrCreate(api, PhotoTestingModSystem.ConfigFileName));

            BestEffort.Try(_owner.BestEffortLogger,
                "log server config load",
                () => api.Logger.Notification($"Phototesting: loaded config '{PhotoTestingModSystem.ConfigFileName}'"));

            // Resolve the channel directly — _owner.ServerChannel is not yet assigned at this
            // point in startup (it is set later by ConfigureServerCameraCaptureStartup).
            api.Network.GetChannel("phototesting")
               .SetMessageHandler<GiveSensitizedPlatePacket>(OnGiveSensitizedPlateReceived);
        }

        private void OnGiveSensitizedPlateReceived(IServerPlayer player, GiveSensitizedPlatePacket packet)
        {
            if (_owner.Api?.World == null) return;

            Item? item = _owner.Api.World.GetItem(new AssetLocation("phototesting", "sensitizedplate"));
            if (item == null) return;

            var stack = new ItemStack(item, 1);
            PlateStateTransitions.InitializeSensitizedPlate(_owner.Api.World, stack, ProcessRegistry.DefaultProcess);

            if (!player.InventoryManager.TryGiveItemstack(stack))
                _owner.Api.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        }
    }
}
