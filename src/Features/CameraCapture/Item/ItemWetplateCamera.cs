using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Shared camera item surface used by both client and server paths.
    // Keeps loaded-plate attribute keys and tooltip behavior centralized.
    // Related: ItemWetplateCamera.Client*.cs (client behavior), CameraCaptureModSystemBridge.Server.cs (authority).
    public partial class ItemWetplateCamera : Item
    {
        public const string AttrLoadedPlate = "phototestingLoadedPlate";
        public const string AttrLoadedPlateStack = "phototestingLoadedPlateStack";
        internal const string ExposureTimedAttrKey = "phototestingCameraExposureTimed";
        internal const string ExposureTimedStartMsKey = "startMs";
        internal const string ExposureTimedDurationMsKey = "durationMs";
        internal const string ExposureLmbPrevKey = "lmbPrev";

        // Prevents normal item use so the client tick and held-interact callbacks can own the camera's custom viewfinder flow.
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;

            // Viewfinder mode is driven by the client tick polling RMB state in PhotoTestingModSystem.
            // We still prevent default use/interact while holding the camera.
        }

        // Shows the camera's basic controls plus the current loaded-plate state and the relevant load/unload hint.
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("RMB + LMB to look through the viewfinder and expose a plate.");

            string? loadedPlate = inSlot?.Itemstack?.Attributes?.GetString(AttrLoadedPlate, null);
            if (!string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine($"Loaded plate: {loadedPlate}");
            }
            else
            {
                dsc.AppendLine("Loaded plate: (none)");
            }

            if (string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine("Shift+Right click with a Sensitized Plate in offhand to load.");
            }
            else
            {
                dsc.AppendLine("Shift+Right click with empty offhand to unload.");
            }
        }
    }
}
