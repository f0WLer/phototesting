using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Pipeline owner: orchestrates stage order for wetplate effects application.
        public static void ApplyInPlace(SKBitmap bmp, string seedKey, WetplateEffectsConfig cfg)
        {
            if (bmp == null) return;
            if (cfg == null) return;
            if (!cfg.Enabled) return;

            var effectiveCfg = cfg;
            if (cfg.DynamicEnabled && cfg.DynamicScale > 0f)
            {
                effectiveCfg = cfg.Clone();
                ApplyDynamicVariance(effectiveCfg, seedKey);
            }

            int w = bmp.Width;
            int h = bmp.Height;
            if (w <= 0 || h <= 0) return;

            var rng = new Random(StableHash(seedKey ?? string.Empty));

            // 0) Per-channel tone curves — applied to raw colour pixels before any bias or greyscale.
            //    No-op when all curves are at linear defaults (fast path).
            ApplyChannelCurvesInPlace(bmp, effectiveCfg);

            // 1) Channel bias (orthochromatic simulation) + 2) Greyscale conversion
            using (var srcCopy = bmp.Copy())
            using (var srcImg = SKImage.FromBitmap(srcCopy))
            using (var canvas = new SKCanvas(bmp))
#pragma warning disable CS0618 // Preserve existing sampling behavior on current Skia API
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
#pragma warning restore CS0618
            {
                paint.ColorFilter = CreateBaseColorFilter(effectiveCfg);
                paint.BlendMode = SKBlendMode.Src;
                canvas.DrawImage(srcImg, new SKRect(0, 0, w, h), paint);
            }

            // 3) Nonlinear contrast curve + 4) Highlight shoulder/clipping
            ApplyToneCurveAndShoulderInPlace(bmp, effectiveCfg);

            // 3b) Halation — glow around bright areas from light scatter through the glass base.
            //     Must run after tone (operates on toned luminance) and before sky blowout.
            ApplyHalation(bmp, effectiveCfg);

            // 5) Sky blowout/bloom + vignette
            if (effectiveCfg.SkyBlowout > 0.001f)
            {
                ApplySkyBlowout(bmp, rng, effectiveCfg);
            }

            if (effectiveCfg.Vignette > 0.001f)
            {
                using var canvas = new SKCanvas(bmp);
                using var paint = new SKPaint { IsAntialias = true };

                float centerOffsetX = ((float)rng.NextDouble() * 2f - 1f) * w * 0.05f;
                float centerOffsetY = ((float)rng.NextDouble() * 2f - 1f) * h * 0.04f;
                var center = new SKPoint(w / 2f + centerOffsetX, h / 2f + centerOffsetY);
                float radius = Math.Max(w, h) * effectiveCfg.VignetteRadius;

                byte a = (byte)(255 * effectiveCfg.Vignette);
                var colors = new[]
                {
                    new SKColor(0, 0, 0, 0),
                    new SKColor(0, 0, 0, a)
                };
                var pos = new[] { 0.0f, 1.0f };

                paint.Shader = SKShader.CreateRadialGradient(center, radius, colors, pos, SKShaderTileMode.Clamp);
                paint.BlendMode = SKBlendMode.Multiply;
                canvas.DrawRect(new SKRect(0, 0, w, h), paint);

                // Add very subtle directional falloff so the vignette is not perfectly radial every frame.
                float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dx = (float)Math.Cos(ang);
                float dy = (float)Math.Sin(ang);
                float far = Math.Max(w, h) * 0.75f;
                var p0 = new SKPoint(center.X - dx * far, center.Y - dy * far);
                var p1 = new SKPoint(center.X + dx * far, center.Y + dy * far);
                byte a2 = (byte)Math.Max(0, Math.Min(255, (int)(a * 0.28f)));
                using var chemPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Multiply };
                chemPaint.Shader = SKShader.CreateLinearGradient(
                    p0,
                    p1,
                    new[] { new SKColor(0, 0, 0, a2), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, a2) },
                    new[] { 0f, 0.5f, 1f },
                    SKShaderTileMode.Clamp);
                canvas.DrawRect(new SKRect(0, 0, w, h), chemPaint);
            }

            // 6) Development defects (imperfection / uneven chemistry)
            // Note: micro-blur is a cheap stand-in for long-exposure motion softness.
            if (effectiveCfg.MicroBlur > 0.001f)
            {
                ApplyEdgePreservingMicroBlur(bmp, effectiveCfg.MicroBlur, effectiveCfg);
            }

            if (effectiveCfg.Imperfection > 0.001f || effectiveCfg.SkyUnevenness > 0.001f)
            {
                ApplyUnevenDensity(bmp, rng, effectiveCfg);
            }

            // 6b) Radial lens aberration — edge softness from uncorrected historical optics.
            //     After micro-blur (both are softening passes; aberration must come last to
            //     preserve the radial mask), before grain (grain lands on soft edges naturally).
            ApplyLensAberration(bmp, effectiveCfg);

            // 7) Grain (silver clumps / density variations)
            if (effectiveCfg.Grain > 0.001f)
            {
                ApplySilverClumpGrain(bmp, rng, effectiveCfg);
            }

            // 4) Dust + scratches
            if (effectiveCfg.DustCount > 0 || effectiveCfg.ScratchCount > 0)
            {
                using var canvas = new SKCanvas(bmp);
                DrawDust(canvas, w, h, rng, effectiveCfg);
                DrawScratches(canvas, w, h, rng, effectiveCfg);
            }

            // 9) Very light sepia (optional) at the end
            if (effectiveCfg.SepiaStrength > 0.001f)
            {
                ApplySepiaAtEnd(bmp, effectiveCfg);
            }
        }
    }
}