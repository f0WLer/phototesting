using System;
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

            EntityPos entityPos = val.SidedPos;

            double dx = entityPos.X         - val.CameraPos.X;
            double dy = entityPos.InternalY - val.CameraPos.Y;
            double dz = entityPos.Z         - val.CameraPos.Z;

            if (Math.Abs(dx) < 1e-7 && Math.Abs(dy) < 1e-7 && Math.Abs(dz) < 1e-7)
                return;

            // The patch runs during rendering, so reuse thread-local scratch matrices.
            float[] translate = VirtualCameraSelfPortraitContext.TmpTranslate;
            float[] copy      = VirtualCameraSelfPortraitContext.TmpModel;

            float[] modelMat = Traverse.Create(__instance).Field<float[]>("ModelMat").Value;

            Array.Copy(modelMat, copy, 16);
            Mat4f.Identity(translate);
            Mat4f.Translate(translate, translate, (float)dx, (float)dy, (float)dz);
            // Prepend the correction so the existing pose/orientation stays intact.
            Mat4f.Mul(modelMat, translate, copy);
        }
    }
}
