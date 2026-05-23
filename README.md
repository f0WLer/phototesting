## Quickstart

Create a `.env` file in the repo root with at least `VINTAGE_STORY_PATH` and `MODS_PATH` set to your local Vintage Story install and mods folder.

## Docs

- `docs/FEATURE_PILLARS.md` for a feature-level overview
- `docs/FILEMAP.md` for a folder and key-file map
- `docs/FLOW_CAMERA.md`, `docs/FLOW_PLATE_PROCESSING.md`, `docs/FLOW_PHOTO_DISPLAY.md`, and `docs/FLOW_PHOTO_SYNC.md` for runtime flow notes

## What Affects Exposure

Excluding the final image-effects pipeline, the exposure result is shaped by these inputs:

- Process preset: `.phototesting exposure start <chloride|iodide|bromide>` selects the preset in `src/Features/CameraCapture/Exposure/PlateProcessProfile.cs`. That preset defines exposure duration, target sample count, target sample interval (`DurationSeconds / SampleCount`), ISO-equivalent speed, spectral sensitivity (`R/G/B` weights), development strength, and H&D gamma.
- Actual captured frames: multi-frame exposures only accumulate as many samples as the renderer actually manages to grab before stop or timeout. Use `.phototesting exposure status` while running, and the completion/stop log, to see actual frames versus the preset target.
- Normalize mode: `.phototesting exposure physics normalize <on|off>` controls whether development divides by actual captured frames (`on`) or by the preset target sample count (`off`). With `off`, early or undersampled exposures develop darker.
- Linearization: `.phototesting exposure physics linearize <on|off>` controls whether source pixels are converted from sRGB into linear light before exposure math.
- Spectral weighting: `.phototesting exposure physics spectral <on|off>` controls whether the process preset's spectral sensitivity weights are used when collapsing RGB into silver density.
- H&D curve: `.phototesting exposure physics hdcurve <on|off>` controls whether the developed density passes through the preset's characteristic curve, driven by development strength and H&D gamma.
- Exposure finishing: `.phototesting exposure finishing <on|off>` controls whether the post-development finishing pass is applied to exposure preview frames and exposure exports.
- Code path: multi-frame exposures run through `src/Features/CameraCapture/Exposure/VirtualExposureRenderer.cs` and the accumulator implementations. Single-frame previews and captures use `src/Features/CameraCapture/Exposure/EmulsionDevelop.cs`, which applies the same core emulsion math to one frame instead of an accumulated stack.

Use `.phototesting exposure process [name]`, `.phototesting exposure physics`, `.phototesting exposure finishing`, and `.phototesting exposure status` to inspect the current runtime values.