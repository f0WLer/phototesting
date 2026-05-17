using Vintagestory.API.Client;

namespace Phototesting.CameraCapture.Exposure
{
    // Accumulator that ingests frames directly from a GPU framebuffer.
    // No per-sample CPU readback; the GPU downsamples and accumulates in-place.
    // CPU readback happens only when Develop() or an export is requested.
    internal interface IGpuExposureAccumulator : IExposureAccumulator
    {
        // Downsamples sourceFbo into the GPU accumulation texture and adds it to the
        // running exposure sum. sourceFbo must remain valid until this call returns.
        void Accumulate(FrameBufferRef sourceFbo);
    }
}
