# .phototesting Command Reference

All commands are run in Vintage Story chat. The root command is `.phototesting` (no alias). Subcommands are case-insensitive. Arguments in `<angle brackets>` are required; arguments in `[square brackets]` are optional.

---

## `.phototesting preview`

Controls the live virtual-camera viewfinder overlay. Settings are persisted to the client config.

Running `.phototesting preview` with no subcommand prints the current state.

| Subcommand | Description |
|---|---|
| `show` | Print current preview state (same as no argument). |
| `on` / `enable` | Enable the preview overlay. |
| `off` / `disable` | Disable the preview overlay. |
| `toggle` | Flip the preview overlay on or off. |
| `size <w> <h>` | Set the preview panel size in pixels (e.g. `size 320 240`). |
| `refresh <ms>` | Set the refresh interval in milliseconds (e.g. `refresh 100` for 10 fps). |
| `anchor <pos>` | Pin the overlay to a screen corner: `topleft`, `topright`, `bottomleft`, `bottomright`. |
| `quality <px>` | Set the longest side of the rendered preview image in pixels. Higher = sharper but slower. |
| `peak [show\|on\|off\|toggle]` | Enable/disable focus-peak highlighting. `show` prints current state. |
| `effects [show\|on\|off\|toggle]` | Apply wetplate image effects to the live preview. `show` prints current state. |
| `virtualcamera [nobody]` | Start a virtual-camera preview from your current eye position and heading. The local player's body is rendered in the frame by default (self-portrait). Pass `nobody` as the next word to suppress the self-portrait: `.phototesting preview virtualcamera nobody`. Automatically enables the overlay if it is off. |
| `virtualcamera stop` / `vcam stop` | Stop the active virtual-camera preview session. |

**Example workflow:**
```
.phototesting preview on
.phototesting preview size 480 270
.phototesting preview refresh 80
.phototesting preview virtualcamera
```

---

## `.phototesting effects`

Manages the wetplate image-effects pipeline. Changes apply to all photos taken after the change; use `.phototesting clearcache` to reprocess photos already on display.

Alias: `.phototesting fx`

| Subcommand | Description |
|---|---|
| `show` (default) | Print all current effect values. |
| `enable` | Enable the effects pipeline. |
| `disable` | Disable the effects pipeline (photos come out unprocessed). |
| `reset` | Reset all effect properties to built-in defaults. |
| `preset <indoor\|outdoor>` | Load the saved indoor or outdoor preset as the active config. If no preset has been saved, seeds it from the current config. |
| `set <property> <value>` | Set a single effect property by name (see table below). |

### Effect properties (`effects set`)

Pass the property name (case-insensitive) and a numeric or boolean value.

**Global**

| Property | Type | Default | Description |
|---|---|---|---|
| `greyscale` | bool | `true` | Convert the image to greyscale after channel adjustments. |
| `enabled` | bool | `true` | Master on/off for the whole pipeline. |

**Pre-greyscale channel bias** — applied before any other stage; shifts colour channels before greyscale or sepia.

| Property | Type | Default | Description |
|---|---|---|---|
| `pregrayred` | float | `0.92` | Red channel multiplier before greyscale (1 = unchanged). |
| `pregraygreen` | float | `1.00` | Green channel multiplier. |
| `pregrayblue` | float | `1.18` | Blue channel multiplier. Boosting this gives a historically accurate blue-sensitive look. |

**Tone / contrast**

| Property | Type | Default | Description |
|---|---|---|---|
| `contrast` | float | `1.32` | Contrast multiplier (1 = flat). |
| `brightness` | float | `0.065` | Additive brightness offset (–1..1, 0 = unchanged). |
| `shadowfloor` | float | `0.035` | Minimum luminance after tone mapping; prevents pure-black voids. |
| `contraststart` | float | `0.38` | Luminance below which contrast compression is reduced (protects shadows). |
| `highlightshoulder` | float | `0.60` | Strength of the highlight roll-off shoulder (0 = none, 1 = maximum). |
| `highlightthreshold` | float | `0.65` | Luminance level where highlight compression begins. |

**Per-channel tone curves** — quadratic Bézier through toe, mid, and shoulder for each channel.

| Property | Type | Default | Description |
|---|---|---|---|
| `curveredtoe` | float | — | Red output at input = 0 (shadow point). |
| `curveredmid` | float | — | Red output at input = 0.5. |
| `curveredshoulder` | float | — | Red output at input = 1 (highlight point). |
| `curvegreentoe` | float | — | Green shadow point. |
| `curvegreenmid` | float | — | Green mid point. |
| `curvegreenshoulder` | float | — | Green highlight point. |
| `curvebluetoe` | float | — | Blue shadow point. |
| `curvebluemid` | float | — | Blue mid point. |
| `curveblueshoulder` | float | — | Blue highlight point. |

**Finish / toning**

| Property | Type | Default | Description |
|---|---|---|---|
| `sepia` | float | `0.07` | Sepia-tone blend strength (0 = no toning). |
| `vignette` | float | `0.24` | Radial darkening toward the frame edges (0 = none). |
| `vignetteradius` | float | `0.78` | Radius of the unvignetted centre as a fraction of half the shorter dimension. |
| `skyblowout` | float | `0.40` | Brightness bloom applied to the top of the frame, simulating halation from the sky. |
| `grain` | float | `0.08` | Film grain intensity (0 = none). |

**Realism details**

| Property | Type | Default | Description |
|---|---|---|---|
| `imperfection` | float | `0.60` | Biases dust toward the frame edges and adds subtle density pooling. |
| `microblur` | float | `0.18` | Edge-preserving micro-blur that softens thin geometry without killing sharp edges. |
| `edgewarmth` | float | `0.12` | Applies a slight warm/sepia tint toward the frame edges. |
| `skyunevenness` | float | `0.30` | Adds non-uniform density banding/mottle in the upper portion of the frame. |
| `skytopfraction` | float | `0.50` | Fraction of the image height treated as "sky" for sky-unevenness effects. |

**Decorative artifacts**

| Property | Type | Default | Description |
|---|---|---|---|
| `dust` | int | `80` | Number of dust-spot particles drawn on the plate. |
| `dustopacity` | float | `0.07` | Opacity of each dust spot. |
| `scratches` | int | `5` | Number of scratch lines drawn on the plate. |
| `scratchopacity` | float | `0.02` | Opacity of each scratch. |

**Halation** — back-scatter glow around bright areas (default off).

| Property | Type | Default | Description |
|---|---|---|---|
| `halation` | float | `0.0` | Halation glow strength (0 = disabled). |
| `halationthreshold` | float | `0.75` | Luminance above which glow starts. |
| `halationradius` | float | `0.018` | Blur radius as a fraction of the longest image dimension. |
| `halationtint` | float | `0.0` | Tint shift: 0 = neutral white glow, 1 = warm reddish glow. |

**Lens aberration** — progressive edge softness from historical optics (default off).

| Property | Type | Default | Description |
|---|---|---|---|
| `lensaberration` | float | `0.0` | Edge-softening strength (0 = disabled). |
| `lensaberrationstart` | float | `0.55` | Normalised radius (0..1) where softening begins. |
| `lensaberrationsigma` | float | `2.0` | Maximum blur sigma at the image corner. |

**Dynamic variance**

| Property | Type | Default | Description |
|---|---|---|---|
| `dynamic` | bool | `false` | When enabled, randomly varies effect parameters per image for an organic look. |
| `dynamicscale` | float | — | Strength of the per-image random variation (0 = none, 1 = full range). |

### `.phototesting effect` (legacy alias)

A single-property setter and profile save/load utility. These are aliases for the `effects set` and `effects preset` family:

```
.phototesting effect <property> <value>        — set one property (same as effects set)
.phototesting effect save [profilename]        — save current config to <profilename>.json
.phototesting effect load [profilename]        — load config from <profilename>.json
```

Profile files are stored in the mod's config directory. The default profile name is `effects-tuning`. Names may only contain letters, digits, hyphens, and underscores.

---

## `.phototesting exposure`

Controls the multi-frame virtual-camera exposure session that accumulates a long-exposure image into a float buffer, then exports it as a developed plate.

Alias: `.phototesting exp`

If a virtual-camera preview is active when `start` is called, the exposure inherits that camera's exact position and heading. Otherwise it defaults to the player's current eye position and heading.

### Plate process chemistry

Three historical emulsion types are supported. Each has fixed spectral sensitivity and a target exposure duration:

| Process | Duration | Spectral sensitivity | Notes |
|---|---|---|---|
| `chloride` | 90 s | Strongly blue-shifted (R 0.04 / G 0.35 / B 1.00) | Earliest wet-plate collodion. Tripod essential. |
| `iodide` | 20 s | Partially expanded (R 0.12 / G 0.45 / B 1.00) | Mid-tier. Tripod recommended for moving subjects. |
| `bromide` | 3 s | Near-panchromatic (R 0.30 / G 0.59 / B 1.00) | Fast gelatin-silver. Handheld viable. |

All processes accumulate 128 samples spread evenly over the target duration (one virtual render per `duration / 128` seconds of real time).

### Exposure lifecycle commands

| Command | Description |
|---|---|
| `start [process]` | Begin exposure. Process defaults to `iodide` if omitted. Prints the sample count, duration, and self-portrait flag. |
| `stop` | Close the shutter. Stops accumulation while keeping the buffer. Use `export` or `discard` after. |
| `pause` | Pause accumulation mid-exposure. |
| `resume` | Resume a paused exposure. |
| `reset` | Clear the accumulation buffer and restart from the same camera position. Does not stop the session. |
| `discard` | Abort the exposure and clear the buffer entirely. Returns to idle. |
| `export` | Develop and save the accumulated image to disk. Prints the output filename and frame count. Can be called while still capturing or after `stop`. |

### Diagnostic / info commands

| Command | Description |
|---|---|
| `status` | Print the active process name, current state, accumulated/total samples, duration, and sample interval. |
| `process` | Print the active process name and remind how to change it. |
| `process <name>` | Print the full profile for the named process (sensitivity, sample count, duration, interval) without starting an exposure. |

### Physics overrides

The `physics` subcommand toggles the three simulation stages that run per accumulated frame. These are on by default and rarely need changing.

```
.phototesting exposure physics                        — print current physics flags
.phototesting exposure physics <flag> <on|off>        — toggle one flag
```

| Flag | Description |
|---|---|
| `linearize` | Convert sRGB pixels to linear light before accumulation (reverses gamma). |
| `spectral` | Apply per-channel spectral weights from the active process profile. |
| `hdcurve` | Apply the Hurter–Driffield (H&D) contrast curve and gamma during accumulation. |

**Example workflow:**
```
.phototesting preview virtualcamera
.phototesting exposure start chloride
... wait 90 seconds while facing the scene ...
.phototesting exposure stop
.phototesting exposure export
```
