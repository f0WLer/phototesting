namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Shared flag consulted by the Harmony patch on <c>EntityPlayerShapeRenderer</c>
    /// to suppress local-player rendering during viewport exposure accumulation.
    /// Only ever read or written on the main game thread.
    /// </summary>
    internal static class ViewportExposureSuppressContext
    {
        /// <summary>When <see langword="true"/>, the patched renderer skips drawing the local player for the current frame.</summary>
        internal static bool SuppressLocalPlayer;
    }
}
