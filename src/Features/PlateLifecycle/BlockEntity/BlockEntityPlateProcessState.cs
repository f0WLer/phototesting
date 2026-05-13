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
        private const string DryFinishTotalHoursAttr = "phototestingDryFinishTotalHours";
        private const string DryTotalHoursAttr = "phototestingDryTotalHours";

        public string ProcessId { get; private set; } = string.Empty;
        public int SensitizationStepIndex { get; private set; } = -1;

        public bool HasProcessState => !string.IsNullOrWhiteSpace(ProcessId) && SensitizationStepIndex >= 0;

        // Absolute in-game hours deadline. Negative when no dry wait is active.
        // Compared against Calendar.TotalHours on each server tick, so it survives
        // chunk unload, save/reload, and calendar speed changes.
        private double _dryFinishTotalHours = -1.0;
        private double _dryTotalHours = 0.0;
        private long _serverTickListenerId;

        public bool IsDryWaitActive => _dryFinishTotalHours > 0.0;
        public double DryFinishTotalHours => _dryFinishTotalHours;

        public float DryRemainingSeconds
        {
            get
            {
                if (_dryFinishTotalHours <= 0.0 || Api?.World?.Calendar == null) return 0f;
                var cal = Api.World.Calendar;
                double remainHours = Math.Max(0.0, _dryFinishTotalHours - cal.TotalHours);
                double speedFactor = cal.SpeedOfTime * cal.CalendarSpeedMul;
                return speedFactor > 0f ? (float)(remainHours * 3600.0 / speedFactor) : 0f;
            }
        }

        public float DryTotalSeconds
        {
            get
            {
                if (_dryTotalHours <= 0.0 || Api?.World?.Calendar == null) return 0f;
                var cal = Api.World.Calendar;
                double speedFactor = cal.SpeedOfTime * cal.CalendarSpeedMul;
                return speedFactor > 0f ? (float)(_dryTotalHours * 3600.0 / speedFactor) : 0f;
            }
        }

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

        // Starts a passive air-dry wait. Called by BlockGlassPlate when a chemical
        // step completes and the next step is a Dry wait.
        // Stores an absolute TotalHours deadline so the wait survives chunk unload and
        // save/reload, matching the behaviour of EnumTransitionType.Dry on items.
        public void StartDryWait(float waitSeconds)
        {
            if (waitSeconds <= 0f || Api?.World?.Calendar == null)
            {
                _dryFinishTotalHours = -1.0;
                _dryTotalHours = 0.0;
            }
            else
            {
                var cal = Api.World.Calendar;
                double waitHours = waitSeconds * cal.SpeedOfTime * cal.CalendarSpeedMul / 3600.0;
                _dryFinishTotalHours = cal.TotalHours + waitHours;
                _dryTotalHours = waitHours;
            }
            MarkDirty(true);
        }

        public void RestoreDryWait(double finishTotalHours, double currentTotalHours)
        {
            if (finishTotalHours <= currentTotalHours)
            {
                CancelDryWait();
                return;
            }

            _dryFinishTotalHours = finishTotalHours;
            _dryTotalHours = Math.Max(0.0, finishTotalHours - currentTotalHours);
            MarkDirty(true);
        }

        public void CancelDryWait()
        {
            if (_dryFinishTotalHours <= 0.0 && _dryTotalHours == 0.0) return;
            _dryFinishTotalHours = -1.0;
            _dryTotalHours = 0.0;
            MarkDirty(true);
        }

        private void OnServerTick(float dt)
        {
            if (_dryFinishTotalHours <= 0.0) return;

            if (Api.World.Calendar.TotalHours < _dryFinishTotalHours) return;

            _dryFinishTotalHours = -1.0;
            _dryTotalHours = 0.0;
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
            tree.SetDouble(DryFinishTotalHoursAttr, _dryFinishTotalHours);
            tree.SetDouble(DryTotalHoursAttr, _dryTotalHours);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            ProcessId = tree.GetString(ProcessIdAttr, string.Empty);
            SensitizationStepIndex = tree.GetInt(StepIndexAttr, -1);
            if (SensitizationStepIndex < -1) SensitizationStepIndex = -1;

            _dryFinishTotalHours = tree.GetDouble(DryFinishTotalHoursAttr, -1.0);
            _dryTotalHours = tree.GetDouble(DryTotalHoursAttr, 0.0);
        }
    }
}

