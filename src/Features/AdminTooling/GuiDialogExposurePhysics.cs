using Vintagestory.API.Client;
using Phototesting.CameraCapture.Exposure;
using Phototesting.ImageEffects;

namespace Phototesting.AdminTooling
{
    // Dev-time dialog for live-tuning exposure physics toggles and key effects sliders.
    // Opened via: .phototesting exposure gui
    // Changes take effect immediately; use .phototesting effects save to persist effects.
    internal sealed class GuiDialogExposurePhysics : GuiDialog
    {
        private readonly VirtualExposureRenderer _renderer;
        private readonly PhotoTestingModSystem _owner;

        public override string? ToggleKeyCombinationCode => null;

        internal GuiDialogExposurePhysics(
            ICoreClientAPI capi,
            VirtualExposureRenderer renderer,
            PhotoTestingModSystem owner)
            : base(capi)
        {
            _renderer = renderer;
            _owner    = owner;
        }

        public override void OnGuiOpened() => ComposeDialog();

        // Always returns the live effects config, initialising it if somehow null.
        private WetplateEffectsConfig Effects => _owner.Config.Effects ??= new WetplateEffectsConfig();

        private void ComposeDialog()
        {
            const double dialogW  = 520.0;
            const double labelW   = 160.0;
            const double sliderW  = dialogW - labelW - 14.0;
            const double halfW    = dialogW / 2.0;
            const double swSize   = 25.0;
            const double rowH     = 32.0;

            // Helper — absolute child-element bounds (relative to bgBounds content area).
            static ElementBounds B(double x, double y, double w, double h)
                => ElementBounds.Fixed(x, y, w, h);

            double y = 28.0;

            // ── Physics section ──────────────────────────────────────────────────
            var physHeader = B(0, y, dialogW, 20);
            y += 26;

            // Row 1: Linearize  |  Spectral Weights
            var sw1 = B(0,          y,              swSize, swSize);
            var lb1 = B(swSize + 6, y + 3,          halfW - swSize - 12, 20);
            var sw2 = B(halfW,      y,              swSize, swSize);
            var lb2 = B(halfW + swSize + 6, y + 3,  halfW - swSize - 12, 20);
            y += rowH;

            // Row 2: H&D Curve  |  Normalize
            var sw3 = B(0,          y,              swSize, swSize);
            var lb3 = B(swSize + 6, y + 3,          halfW - swSize - 12, 20);
            var sw4 = B(halfW,      y,              swSize, swSize);
            var lb4 = B(halfW + swSize + 6, y + 3,  halfW - swSize - 12, 20);
            y += rowH;

            // Row 3: Apply Finishing
            var sw5 = B(0,          y,              swSize, swSize);
            var lb5 = B(swSize + 6, y + 3,          200,   20);
            y += rowH + 8;
            // ── Chemistry section ─────────────────────────────────────────────────
            var chemHeader = B(0, y, dialogW, 20);
            y += 26;

            // Slider rows for chemistry params
            var (lDev, sDev) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGam, sGam) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lRed, sRed) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGrn2, sGrn2) = SliderRow(y, labelW, sliderW); y += rowH;
            var (lBlu, sBlu) = SliderRow(y, labelW, sliderW); y += rowH;

            // Reset chemistry button (right-aligned, same row after the last slider)
            var resetChemBtn = B(dialogW - 130, y, 130, 22);
            y += rowH + 4;
            // ── Effects section ───────────────────────────────────────────────────
            var fxHeader = B(0, y, dialogW, 20);
            y += 26;

            // Slider rows: [label][slider]
            var (lCon,  sCon)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lBri,  sBri)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSfl,  sSfl)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lCst,  sCst)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSho,  sSho)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lSky,  sSky)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lGrn,  sGrn)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lVig,  sVig)  = SliderRow(y, labelW, sliderW); y += rowH;
            var (lImp,  sImp)  = SliderRow(y, labelW, sliderW); y += rowH + 8;

            // Close button
            var closeBtn = B(dialogW - 90, y, 90, 25);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("phototesting-expphysics", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Exposure Physics Tuner", () => TryClose())
                .BeginChildElements(bgBounds)

                // Physics header
                .AddStaticText("─── Physics ───", CairoFont.WhiteSmallText(), physHeader)

                // Row 1
                .AddSwitch(v => { _renderer.SetPhysics("linearize", v); _renderer.RequestPreviewRefresh(); }, sw1, "sw-linearize", swSize)
                .AddStaticText("Linearize",       CairoFont.WhiteDetailText(), lb1)
                .AddSwitch(v => { _renderer.SetPhysics("spectral",   v); _renderer.RequestPreviewRefresh(); }, sw2, "sw-spectral",  swSize)
                .AddStaticText("Spectral Weights", CairoFont.WhiteDetailText(), lb2)

                // Row 2
                .AddSwitch(v => { _renderer.SetPhysics("hdcurve",  v); _renderer.RequestPreviewRefresh(); }, sw3, "sw-hdcurve",  swSize)
                .AddStaticText("H&D Curve",  CairoFont.WhiteDetailText(), lb3)
                .AddSwitch(v => { _renderer.SetPhysics("normalize", v); _renderer.RequestPreviewRefresh(); }, sw4, "sw-normalize", swSize)
                .AddStaticText("Normalize",  CairoFont.WhiteDetailText(), lb4)

                // Row 3
                .AddSwitch(v => { _renderer.ApplyFinishing = v; _renderer.RequestPreviewRefresh(); }, sw5, "sw-finishing", swSize)
                .AddStaticText("Apply Finishing", CairoFont.WhiteDetailText(), lb5)

                // Chemistry header
                .AddStaticText("─── Chemistry ───", CairoFont.WhiteSmallText(), chemHeader)

                // Dev Strength: float 0..20 → int 0..200 (÷10 = float)
                .AddStaticText("Dev Strength",      CairoFont.WhiteDetailText(), lDev)
                .AddSlider(v => { _renderer.SetChemistry("devstrength", v / 10f); _renderer.RequestPreviewRefresh(); return true; }, sDev, "sl-devstrength")

                // H&D Gamma: float 0.5..2.5 → int 50..250 (÷100 = float)
                .AddStaticText("H&D Gamma",          CairoFont.WhiteDetailText(), lGam)
                .AddSlider(v => { _renderer.SetChemistry("hdgamma", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sGam, "sl-hdgamma")

                // Spectral sensitivities: float 0..2 → int 0..200 (÷100 = float)
                .AddStaticText("Red Sensitivity",    CairoFont.WhiteDetailText(), lRed)
                .AddSlider(v => { _renderer.SetChemistry("redsens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sRed, "sl-redsens")

                .AddStaticText("Green Sensitivity",  CairoFont.WhiteDetailText(), lGrn2)
                .AddSlider(v => { _renderer.SetChemistry("greensens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sGrn2, "sl-greensens")

                .AddStaticText("Blue Sensitivity",   CairoFont.WhiteDetailText(), lBlu)
                .AddSlider(v => { _renderer.SetChemistry("bluesens", v / 100f); _renderer.RequestPreviewRefresh(); return true; }, sBlu, "sl-bluesens")

                .AddSmallButton("Reset to Process", OnResetChemistry, resetChemBtn)

                // Effects header
                .AddStaticText("─── Effects ───", CairoFont.WhiteSmallText(), fxHeader)

                .AddStaticText("Contrast",           CairoFont.WhiteDetailText(), lCon)
                .AddSlider(v => { Effects.Contrast          = v / 100f; return true; }, sCon, "sl-contrast")

                .AddStaticText("Brightness",          CairoFont.WhiteDetailText(), lBri)
                .AddSlider(v => { Effects.Brightness         = v / 100f; return true; }, sBri, "sl-brightness")

                .AddStaticText("Shadow Floor",        CairoFont.WhiteDetailText(), lSfl)
                .AddSlider(v => { Effects.ShadowFloor        = v / 100f; return true; }, sSfl, "sl-shadowfloor")

                .AddStaticText("Contrast Start",      CairoFont.WhiteDetailText(), lCst)
                .AddSlider(v => { Effects.ContrastStart      = v / 100f; return true; }, sCst, "sl-contraststart")

                .AddStaticText("Highlight Shoulder",  CairoFont.WhiteDetailText(), lSho)
                .AddSlider(v => { Effects.HighlightShoulder  = v / 100f; return true; }, sSho, "sl-shoulder")

                .AddStaticText("Sky Blowout",         CairoFont.WhiteDetailText(), lSky)
                .AddSlider(v => { Effects.SkyBlowout         = v / 100f; return true; }, sSky, "sl-skyblowout")

                .AddStaticText("Grain",               CairoFont.WhiteDetailText(), lGrn)
                .AddSlider(v => { Effects.Grain               = v / 100f; return true; }, sGrn, "sl-grain")

                .AddStaticText("Vignette",            CairoFont.WhiteDetailText(), lVig)
                .AddSlider(v => { Effects.Vignette            = v / 100f; return true; }, sVig, "sl-vignette")

                .AddStaticText("Imperfection",        CairoFont.WhiteDetailText(), lImp)
                .AddSlider(v => { Effects.Imperfection        = v / 100f; return true; }, sImp, "sl-imperfection")

                .AddSmallButton("Close", TryClose, closeBtn)

                .EndChildElements()
                .Compose();

            // ── Initialise switch states ──────────────────────────────────────────
            var c = SingleComposer;
            c.GetSwitch("sw-linearize").SetValue(_renderer.PhysicsLinearize);
            c.GetSwitch("sw-spectral") .SetValue(_renderer.PhysicsSpectralWeights);
            c.GetSwitch("sw-hdcurve")  .SetValue(_renderer.PhysicsHDCurve);
            c.GetSwitch("sw-normalize").SetValue(_renderer.PhysicsNormalize);
            c.GetSwitch("sw-finishing").SetValue(_renderer.ApplyFinishing);

            // Sliders: 0-1 floats → int 0-100; Contrast 0-3 → 0-300; Brightness -1..1 → -100..100.
            var fx = Effects;
            c.GetSlider("sl-contrast")    .SetValues((int)(fx.Contrast         * 100), 0,    300, 1);
            c.GetSlider("sl-brightness")  .SetValues((int)(fx.Brightness       * 100), -100, 100, 1);
            c.GetSlider("sl-shadowfloor") .SetValues((int)(fx.ShadowFloor      * 100), 0,    100, 1);
            c.GetSlider("sl-contraststart").SetValues((int)(fx.ContrastStart   * 100), 0,    100, 1);
            c.GetSlider("sl-shoulder")    .SetValues((int)(fx.HighlightShoulder* 100), 0,    100, 1);
            c.GetSlider("sl-skyblowout")  .SetValues((int)(fx.SkyBlowout       * 100), 0,    100, 1);
            c.GetSlider("sl-grain")       .SetValues((int)(fx.Grain            * 100), 0,    100, 1);
            c.GetSlider("sl-vignette")    .SetValues((int)(fx.Vignette         * 100), 0,    100, 1);
            c.GetSlider("sl-imperfection").SetValues((int)(fx.Imperfection     * 100), 0,    100, 1);

            // Chemistry sliders — seeded from effective values (override if active, else process profile).
            // Dev Strength: ×10 → int 0..200  |  H&D Gamma: ×100 → int 50..250  |  Sens: ×100 → int 0..200
            c.GetSlider("sl-devstrength").SetValues((int)(_renderer.EffectiveDevStrength * 10),  0,   200, 1);
            c.GetSlider("sl-hdgamma")    .SetValues((int)(_renderer.EffectiveHDGamma     * 100), 50,  250, 1);
            c.GetSlider("sl-redsens")    .SetValues((int)(_renderer.EffectiveRedSens     * 100), 0,   200, 1);
            c.GetSlider("sl-greensens")  .SetValues((int)(_renderer.EffectiveGreenSens   * 100), 0,   200, 1);
            c.GetSlider("sl-bluesens")   .SetValues((int)(_renderer.EffectiveBlueSens    * 100), 0,   200, 1);
        }

        // Returns a (labelBounds, sliderBounds) pair for a standard side-by-side row.
        private static (ElementBounds label, ElementBounds slider) SliderRow(
            double y, double labelW, double sliderW)
        {
            const double h = 22.0;
            return (
                ElementBounds.Fixed(0,           y, labelW,  h),
                ElementBounds.Fixed(labelW + 14, y, sliderW, h)
            );
        }

        private bool OnResetChemistry()
        {
            _renderer.ResetChemistryOverrides();
            // Reopen the dialog to reseed all sliders from the process profile defaults.
            TryClose();
            TryOpen();
            return true;
        }

    }
}
