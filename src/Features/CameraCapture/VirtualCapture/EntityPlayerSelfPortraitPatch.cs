#pragma warning disable IDE1006 // Harmony magic parameters require __ prefix
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Phototesting.CameraCapture
{
    // During self-portrait virtual renders, the game builds the local player's model matrix
    // relative to EntityPlayer.CameraPos. VirtualCamera temporarily moves CameraPos to the
    // virtual eye anchor, so this postfix shifts the self model back to the player's body.
    internal static class EntityPlayerSelfPortraitPatch
    {
        // Resolved once on first invocation; null means the field is absent on this engine version.
        private static FieldInfo? _modelMatField;
        private static bool _modelMatChecked;

        [HarmonyPrefix]
        internal static void Prefix(
            object __instance,
            Entity entity,
            bool isSelf,
            float dt,
            bool isShadowPass)
        {
            if (!isSelf || !VirtualCameraSelfPortraitContext.Active)
                return;

            if (entity is not EntityPlayer val)
                return;

            if (val.MountedOn != null)
                return;

            float targetYaw = val.SidedPos.Yaw;
            Traverse traverse = Traverse.Create(__instance);
            traverse.Field<float>("smoothedBodyYaw").Value = targetYaw;
            traverse.Field<float>("bodyYawLerped").Value = targetYaw;
        }

        [HarmonyPostfix]
        internal static void Postfix(
            object __instance,
            Entity entity,
            bool isSelf,
            float dt,
            bool isShadowPass)
        {
            if (!isSelf || !VirtualCameraSelfPortraitContext.Active)
                return;

            if (entity is not EntityPlayer val)
                return;

            if (val.MountedOn != null)
                return;

            // Resolve the ModelMat field once and cache; degrade gracefully if absent.
            if (!_modelMatChecked)
            {
                _modelMatChecked = true;
                FieldInfo? f = AccessTools.Field(__instance.GetType(), "ModelMat");
                _modelMatField = f?.FieldType == typeof(float[]) ? f : null;
                if (_modelMatField == null)
                    Log.Warn(null, "'ModelMat' field not found on player shape renderer — self-portrait matrix correction disabled.");
            }
            if (_modelMatField == null) return;

            EntityPos entityPos = val.SidedPos;

            double dx = entityPos.X         - val.CameraPos.X;
            double dy = entityPos.InternalY - val.CameraPos.Y;
            double dz = entityPos.Z         - val.CameraPos.Z;

            if (Math.Abs(dx) < 1e-7 && Math.Abs(dy) < 1e-7 && Math.Abs(dz) < 1e-7)
                return;

            // Reuse thread-local scratch matrices; the correction is prepended to the existing
            // pose so orientation and scale are preserved.
            float[] translate = VirtualCameraSelfPortraitContext.TmpTranslate;
            float[] copy      = VirtualCameraSelfPortraitContext.TmpModel;
            float[] modelMat  = (float[])_modelMatField.GetValue(__instance)!;

            Array.Copy(modelMat, copy, 16);
            Mat4f.Identity(translate);
            Mat4f.Translate(translate, translate, (float)dx, (float)dy, (float)dz);
            Mat4f.Mul(modelMat, translate, copy);
        }
    }
}
