using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Phototesting.PlateLifecycle.Blocks;

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
        private const string DryRemainingSecondsAttr = "phototestingDryRemainingSeconds";

        public string ProcessId { get; private set; } = string.Empty;
        public int SensitizationStepIndex { get; private set; } = -1;

        public bool HasProcessState => !string.IsNullOrWhiteSpace(ProcessId) && SensitizationStepIndex >= 0;

        // Negative when no dry timer is running. Counted down on the server tick.
        private float _dryRemainingSeconds = -1f;
        private float _dryTotalSeconds = 0f;
        private long _serverTickListenerId;

        public bool IsDryWaitActive => _dryRemainingSeconds > 0f;
        public float DryRemainingSeconds => _dryRemainingSeconds < 0f ? 0f : _dryRemainingSeconds;
        public float DryTotalSeconds => _dryTotalSeconds;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                _serverTickListenerId = RegisterGameTickListener(OnServerTick, 1000);
            }
        }

        public override void OnBlockRemoved()
        {
            if (Api?.Side == EnumAppSide.Server && _serverTickListenerId != 0)
            {
                UnregisterGameTickListener(_serverTickListenerId);
                _serverTickListenerId = 0;
            }
            base.OnBlockRemoved();
        }

        public override void OnBlockUnloaded()
        {
            if (Api?.Side == EnumAppSide.Server && _serverTickListenerId != 0)
            {
                UnregisterGameTickListener(_serverTickListenerId);
                _serverTickListenerId = 0;
            }
            base.OnBlockUnloaded();
        }

        public void SetProcessState(string processId, int stepIndex)
        {
            ProcessId = processId ?? string.Empty;
            SensitizationStepIndex = stepIndex < -1 ? -1 : stepIndex;
            MarkDirty(true);
        }

        // Starts a passive air-dry countdown. Called by BlockGlassPlate when a chemical
        // step completes and the next step is a Dry wait.
        public void StartDryWait(float waitSeconds)
        {
            if (waitSeconds <= 0f)
            {
                _dryRemainingSeconds = -1f;
                _dryTotalSeconds = 0f;
            }
            else
            {
                _dryRemainingSeconds = waitSeconds;
                _dryTotalSeconds = waitSeconds;
            }
            MarkDirty(true);
        }

        public void CancelDryWait()
        {
            if (_dryRemainingSeconds < 0f && _dryTotalSeconds == 0f) return;
            _dryRemainingSeconds = -1f;
            _dryTotalSeconds = 0f;
            MarkDirty(true);
        }

        private void OnServerTick(float dt)
        {
            if (_dryRemainingSeconds < 0f) return;

            _dryRemainingSeconds -= dt;
            if (_dryRemainingSeconds > 0f)
            {
                MarkDirty(false);
                return;
            }

            _dryRemainingSeconds = -1f;
            _dryTotalSeconds = 0f;
            MarkDirty(true);

            if (Block is BlockGlassPlate plateBlock && Api?.World != null)
            {
                plateBlock.OnDryWaitElapsed(Api.World, Pos);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (!string.IsNullOrWhiteSpace(ProcessId))
            {
                tree.SetString(ProcessIdAttr, ProcessId);
            }

            tree.SetInt(StepIndexAttr, SensitizationStepIndex);
            tree.SetFloat(DryRemainingSecondsAttr, _dryRemainingSeconds);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            ProcessId = tree.GetString(ProcessIdAttr, string.Empty);
            SensitizationStepIndex = tree.GetInt(StepIndexAttr, -1);
            if (SensitizationStepIndex < -1) SensitizationStepIndex = -1;

            _dryRemainingSeconds = tree.GetFloat(DryRemainingSecondsAttr, -1f);
            if (_dryRemainingSeconds > 0f && _dryTotalSeconds <= 0f) _dryTotalSeconds = _dryRemainingSeconds;
        }
    }
}

