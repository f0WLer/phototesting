namespace Phototesting.CameraCapture.Exposure
{
    // Shared flag that tells the Harmony patch on EntityPlayerShapeRenderer
    // to skip rendering the local player during viewport exposure accumulation.
    // Only ever read/written on the main game thread.
    internal static class ViewportExposureSuppressContext
    {
        internal static bool SuppressLocalPlayer;
    }
}
