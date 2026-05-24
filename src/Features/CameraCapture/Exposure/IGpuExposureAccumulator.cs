using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Accumulator variant that ingests frames directly from a GPU framebuffer, with no per-sample CPU readback.
    /// GPU accumulation and downsampling happen in-place; a CPU readback is only performed when
    /// <see cref="IExposureAccumulator.Develop"/> or a full export is requested.
    /// </summary>
    internal interface IGpuExposureAccumulator : IExposureAccumulator
    {
        /// <summary>
        /// Downsamples <paramref name="sourceFbo"/> into the GPU accumulation texture and adds it to the running exposure sum.
        /// <paramref name="sourceFbo"/> must remain valid for the duration of this call.
        /// </summary>
        void Accumulate(FrameBufferRef sourceFbo);
    }
}
