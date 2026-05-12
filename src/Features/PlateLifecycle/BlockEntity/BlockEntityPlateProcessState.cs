using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Phototesting.PlateLifecycle
{
    /// <summary>
    /// Lightweight state holder for placed clean/coated plates.
    /// Persists process lock and sensitization progress across block interactions.
    /// </summary>
    public sealed class BlockEntityPlateProcessState : BlockEntity
    {
        private const string ProcessIdAttr = "phototestingProcessId";
        private const string StepIndexAttr = "phototestingSensitizationStep";

        public string ProcessId { get; private set; } = string.Empty;
        public int SensitizationStepIndex { get; private set; } = -1;

        public bool HasProcessState => !string.IsNullOrWhiteSpace(ProcessId) && SensitizationStepIndex >= 0;

        public void SetProcessState(string processId, int stepIndex)
        {
            ProcessId = processId ?? string.Empty;
            SensitizationStepIndex = stepIndex < -1 ? -1 : stepIndex;
            MarkDirty(true);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (!string.IsNullOrWhiteSpace(ProcessId))
            {
                tree.SetString(ProcessIdAttr, ProcessId);
            }

            tree.SetInt(StepIndexAttr, SensitizationStepIndex);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            ProcessId = tree.GetString(ProcessIdAttr, string.Empty);
            SensitizationStepIndex = tree.GetInt(StepIndexAttr, -1);
            if (SensitizationStepIndex < -1) SensitizationStepIndex = -1;
        }
    }
}

