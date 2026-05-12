using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Phototesting.AdminTooling;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Phototesting.CameraCapture
{
    // Standalone Harmony patch on ClientMain.Set3DProjection used as the preferred viewfinder
    // zoom mechanism. Reads viewfinder activation state and zoom multiplier from the bridge via
    // PhotoTestingModSystem.ClientInstance, so this class has no instance dependencies on the bridge.
    //
    // All state is process-static because the patch itself is process-static — Harmony cannot have
    // per-instance hooks. Diagnostic counters are kept here for the bridge's debug-tip logic to read.
    internal static class ViewfinderZoomHarmony
    {
        private static bool _patched;
        private static string? _mechanism;

        private static long _lastSet3DProjectionMs;
        private static int _lastSet3DProjectionScaledIndex;

        // Whether the patch is currently installed in the running process.
        internal static bool IsActive => _patched;

        // Human-readable mechanism string for debug-tip output, or null if not installed.
        internal static string? MechanismDescription => _patched ? _mechanism : null;

        // Reports whether the patch recently applied a scaled projection so the bridge knows whether
        // a forced refresh is required to re-trigger zoom.
        internal static bool WasScaledRecently(long maxAgeMs)
        {
            if (!_patched || _lastSet3DProjectionMs <= 0) return false;

            long ageMs = Environment.TickCount64 - _lastSet3DProjectionMs;
            if (ageMs < 0) ageMs = 0;

            return ageMs <= maxAgeMs && _lastSet3DProjectionScaledIndex >= 0;
        }

        // Installs the Harmony patch on ClientMain.Set3DProjection(float,float). Returns true if the
        // patch is active after the call (already patched or freshly patched). Idempotent.
        internal static bool TryInstall(ICoreClientAPI capi, PhotoTestingClientConfig? clientConfig)
        {
            if (_patched) return true;
            if (capi == null) return false;

            try
            {
                var harmony = new Harmony("phototesting.viewfinderzoom");

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                MethodInfo? set3d = typeof(ClientMain).GetMethod(
                    "Set3DProjection",
                    flags,
                    binder: null,
                    types: new[] { typeof(float), typeof(float) },
                    modifiers: null
                );

                if (set3d == null) return false;

                MethodInfo? transpilerMi = typeof(ViewfinderZoomHarmony).GetMethod(
                    nameof(Set3DProjectionTranspiler),
                    BindingFlags.Static | BindingFlags.NonPublic
                );

                if (transpilerMi == null) return false;

                harmony.Patch(set3d, transpiler: new HarmonyMethod(transpilerMi));

                _patched = true;
                _mechanism = $"Harmony: {typeof(ClientMain).FullName}.Set3DProjection(float,float)";

                _lastSet3DProjectionMs = 0;
                _lastSet3DProjectionScaledIndex = -1;

                if (clientConfig?.ShowZoomMechanismChat == true || clientConfig?.ShowDebugLogs == true)
                {
                    Log.Notify(capi.Logger, "" + _mechanism);
                }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    Log.Warn(capi.Logger, "Harmony projection zoom patch failed: " + ex);
                }
                catch
                {
                    // ignore
                }
                return false;
            }
        }

        // Forces the engine to rebuild its 3D projection so this patch is re-applied immediately.
        internal static void Refresh(ICoreClientAPI? capi)
        {
            if (capi?.Render == null || !_patched) return;

            try
            {
                capi.Render.Reset3DProjection();
            }
            catch
            {
                // ignore
            }
        }

        // Harmony transpiler: rewrites the engine's FOV argument to AdjustFov(ztar, fov) before use.
        private static IEnumerable<CodeInstruction> Set3DProjectionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> output = new List<CodeInstruction>();

            // fov = AdjustFov(ztar, fov);
            output.Add(new CodeInstruction(OpCodes.Ldarg_1));
            output.Add(new CodeInstruction(OpCodes.Ldarg_2));
            output.Add(new CodeInstruction(OpCodes.Call, typeof(ViewfinderZoomHarmony).GetMethod(
                nameof(AdjustFov),
                BindingFlags.Static | BindingFlags.NonPublic
            )));
            output.Add(new CodeInstruction(OpCodes.Starg_S, 2));

            output.AddRange(instructions);
            return output;
        }

        // Reads viewfinder state from the bridge via the singleton ClientInstance and applies the zoom
        // multiplier when active. Cached projection inputs feed WasScaledRecently for refresh logic.
        private static float AdjustFov(float ztar, float fov)
        {
            var inst = PhotoTestingModSystem.ClientInstance;

            float outFov = fov;
            int scaledIndex = -1;

            bool shouldZoom = inst?.CameraCaptureBridge.IsViewfinderActive == true;
            if (shouldZoom)
            {
                float mult = inst?.Config?.Viewfinder?.ZoomMultiplier ?? 0.65f;
                outFov = ClampZoomedFov(fov * mult, fov);
                scaledIndex = 1;
            }

            _lastSet3DProjectionScaledIndex = scaledIndex;
            _lastSet3DProjectionMs = Environment.TickCount64;

            return outFov;
        }

        // Static clamp used by the Harmony patch so it does not depend on instance helpers.
        private static float ClampZoomedFov(float proposed, float oldValue)
        {
            // Mirror ClampFov() logic but keep it static for Harmony patching.
            float basis = oldValue;
            if (basis > 0f && basis < 10f)
            {
                return Math.Max(0.3f, Math.Min(2.5f, proposed));
            }

            return Math.Max(30f, Math.Min(110f, proposed));
        }
    }
}
