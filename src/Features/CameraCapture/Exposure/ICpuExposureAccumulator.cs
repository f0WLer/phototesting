namespace Phototesting.CameraCapture.Exposure
{
    /// <summary>
    /// Accumulator variant that ingests frames already read back to CPU memory.
    /// Used with the async PBO readback path in <see cref="ExposureReadbackPipeline"/>.
    /// </summary>
    internal interface ICpuExposureAccumulator : IExposureAccumulator
    {
        /// <summary>
        /// Adds one BGRA8888 frame to the running exposure sum.
        /// Frames whose dimensions differ from <see cref="IExposureAccumulator.Width"/> × <see cref="IExposureAccumulator.Height"/> are silently ignored.
        /// </summary>
        void Accumulate(byte[] bgra, int width, int height);
    }
}
