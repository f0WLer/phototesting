using SkiaSharp;

namespace Phototesting.ImageEffects
{
    public static partial class WetplateEffects
    {
        // Draws subtle dust specks with optional edge bias for plate-imperfection realism.
        private static void DrawDust(SKCanvas canvas, int w, int h, Random rng, WetplateEffectsConfig cfg)
        {
            if (cfg.DustCount <= 0 || cfg.DustOpacity <= 0.001f) return;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Screen,
                Color = new SKColor(255, 255, 255, (byte)(255 * cfg.DustOpacity))
            };

            int count = cfg.DustCount;
            for (int i = 0; i < count; i++)
            {
                float x;
                float y;
                if (cfg.Imperfection > 0.001f && rng.NextDouble() < (cfg.DustEdgeBiasChanceScale * cfg.Imperfection))
                {
                    SampleEdgeBiasedPoint(w, h, rng, cfg, out x, out y);
                }
                else
                {
                    x = (float)rng.NextDouble() * w;
                    y = (float)rng.NextDouble() * h;
                }
                float r = cfg.DustRadiusMin + (float)rng.NextDouble() * cfg.DustRadiusRange;

                // A few larger flecks
                if ((i % cfg.DustLargeFleckInterval) == 0) r *= cfg.DustLargeFleckScale;

                canvas.DrawCircle(x, y, r, paint);
            }
        }

        // Draws fine bright scratches with occasional darker companion marks.
        private static void DrawScratches(SKCanvas canvas, int w, int h, Random rng, WetplateEffectsConfig cfg)
        {
            if (cfg.ScratchCount <= 0 || cfg.ScratchOpacity <= 0.001f) return;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                BlendMode = SKBlendMode.Screen,
                Color = new SKColor(255, 255, 255, (byte)(255 * cfg.ScratchOpacity))
            };

            int count = cfg.ScratchCount;
            for (int i = 0; i < count; i++)
            {
                float x0 = (float)rng.NextDouble() * w;
                float y0 = (float)rng.NextDouble() * h;

                // Mostly vertical-ish scratches.
                float angle = cfg.ScratchAngleMin + (cfg.ScratchAngleRange * (float)rng.NextDouble());
                float len = (cfg.ScratchLengthMin + cfg.ScratchLengthRange * (float)rng.NextDouble()) * Math.Max(w, h);

                float dx = (float)Math.Sin(angle) * len;
                float dy = (float)Math.Cos(angle) * len;

                paint.StrokeWidth = cfg.ScratchWidthMin + cfg.ScratchWidthRange * (float)rng.NextDouble();
                canvas.DrawLine(x0, y0, x0 + dx, y0 + dy, paint);

                // Occasionally add a darker scratch.
                if ((i % cfg.DarkScratchInterval) == 0)
                {
                    paint.BlendMode = SKBlendMode.Multiply;
                    paint.Color = new SKColor(0, 0, 0, (byte)(255 * (cfg.ScratchOpacity * cfg.DarkScratchOpacityScale)));
                    paint.StrokeWidth *= cfg.DarkScratchWidthScale;
                    canvas.DrawLine(x0 + cfg.DarkScratchOffsetPx, y0, x0 + dx + cfg.DarkScratchOffsetPx, y0 + dy, paint);
                    paint.BlendMode = SKBlendMode.Screen;
                    paint.Color = new SKColor(255, 255, 255, (byte)(255 * cfg.ScratchOpacity));
                }
            }
        }

        // Samples a point near a random image edge using an exponential falloff distribution.
        private static void SampleEdgeBiasedPoint(int w, int h, Random rng, WetplateEffectsConfig cfg, out float x, out float y)
        {
            // Pick an edge and sample close to it with an exponential falloff.
            int edge = rng.Next(4); // 0=top,1=right,2=bottom,3=left

            // Exponential-ish: smaller values are more likely.
            float t = (float)rng.NextDouble();
            float d = (float)(-Math.Log(Math.Max(1e-6, t))); // 0..inf
            float maxD = Math.Min(w, h) * cfg.EdgeBiasMaxDistanceFraction;
            d = Math.Min(maxD, d * (maxD / cfg.EdgeBiasDistanceScaleDivisor));

            switch (edge)
            {
                case 0: // top
                    x = (float)rng.NextDouble() * w;
                    y = d;
                    break;
                case 1: // right
                    x = (w - 1) - d;
                    y = (float)rng.NextDouble() * h;
                    break;
                case 2: // bottom
                    x = (float)rng.NextDouble() * w;
                    y = (h - 1) - d;
                    break;
                default: // left
                    x = d;
                    y = (float)rng.NextDouble() * h;
                    break;
            }
        }
    }
}