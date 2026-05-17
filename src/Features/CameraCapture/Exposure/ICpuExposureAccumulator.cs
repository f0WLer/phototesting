namespace Phototesting.CameraCapture.Exposure
{
    // Accumulator that ingests already-readback CPU byte frames.
    // Used with the async PBO readback path in ExposureReadbackPipeline.
    internal interface ICpuExposureAccumulator : IExposureAccumulator
    {
        // Accumulates one BGRA8888 frame that has already been read back from the GPU.
        // Frames whose dimensions do not match Width x Height are silently ignored.
        void Accumulate(byte[] bgra, int width, int height);
    }
}
