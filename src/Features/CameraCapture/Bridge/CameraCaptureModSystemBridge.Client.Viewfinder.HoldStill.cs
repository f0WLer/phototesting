using Phototesting.CameraCapture.Contracts;
using Vintagestory.API.Common;

namespace Phototesting.CameraCapture
{
    // Client-side viewfinder: state machine, hold-still coordinator, capture gate, effects profile, and zoom harmony patch.
    internal sealed partial class CameraCaptureModSystemBridge
    {

        // Dedicated stateful collaborator for post-shutter hold-still tracking and final PhotoTaken send gating.
        private sealed class ViewfinderHoldStillCoordinator
        {
            private readonly CameraCaptureModSystemBridge _owner;

            private bool _active;
            private float _remainingSeconds;
            private float _movementScore;
            private bool _captureReady;
            private string? _photoId;
            private double _lastX;
            private double _lastY;
            private double _lastZ;
            private float _lastYaw;
            private float _lastPitch;
            private bool _hasLastSample;
            private long _lastPendingMessageMs;

            internal ViewfinderHoldStillCoordinator(CameraCaptureModSystemBridge owner)
            {
                this._owner = owner;
            }

            // Indicates that capture has started but the hold-still packet cannot be sent yet.
            internal bool IsPending => _active || _captureReady;

            // Starts tracking post-capture movement so hold-still scoring can continue until the configured delay expires.
            internal void StartTracking(EntityAgent player, string photoId)
            {
                _active = _owner.HoldStillDurationSecondsCfg > 0f;
                _remainingSeconds = _owner.HoldStillDurationSecondsCfg;
                _movementScore = 0f;
                _captureReady = false;
                _photoId = photoId;
                _hasLastSample = false;
                RecordPositionSample(player, accumulate: false);

                if (!_active)
                {
                    _remainingSeconds = 0f;
                    TrySendPacket();
                }
            }

            // Marks the screenshot as ready so the client can send the final packet once hold-still timing is complete.
            internal void MarkCaptureReady(string photoId)
            {
                if (string.IsNullOrEmpty(photoId)) return;

                _photoId = photoId;
                _captureReady = true;
                TrySendPacket();
            }

            // Clears all hold-still state when capture setup fails or is abandoned.
            internal void Cancel()
            {
                _active = false;
                _remainingSeconds = 0f;
                _movementScore = 0f;
                _captureReady = false;
                _photoId = null;
                _hasLastSample = false;
            }

            // Advances hold-still timing and movement scoring while exposure completion is pending.
            internal void Update(float dt)
            {
                if (!_active) return;

                EntityAgent? playerEnt = _owner.ClientApi?.World?.Player?.Entity;
                if (playerEnt != null)
                {
                    RecordPositionSample(playerEnt, accumulate: true);
                }

                _remainingSeconds -= dt;
                if (_remainingSeconds <= 0f)
                {
                    _active = false;
                    _remainingSeconds = 0f;
                    TrySendPacket();
                }
            }

            // Shows a throttled hold-still reminder while an unload action is blocked by pending capture completion.
            internal void MaybeShowPendingMessage()
            {
                var clientApi = _owner.ClientApi;
                if (clientApi == null) return;

                long nowMs = Environment.TickCount64;
                if (nowMs - _lastPendingMessageMs <= 1000) return;

                _lastPendingMessageMs = nowMs;
                clientApi.ShowChatMessage("Wetplate: hold still to finish the exposure.");
            }

            // Samples player position and look direction, optionally adding their deltas into the movement score.
            private void RecordPositionSample(EntityAgent player, bool accumulate)
            {
                var pos = player.Pos;
                if (pos == null) return;

                double x = pos.X;
                double y = pos.Y;
                double z = pos.Z;
                float yaw = pos.Yaw;
                float pitch = pos.Pitch;

                if (accumulate && _hasLastSample)
                {
                    double dx = x - _lastX;
                    double dy = y - _lastY;
                    double dz = z - _lastZ;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

                    float yawDelta = AngleDeltaRadians(_lastYaw, yaw);
                    float pitchDelta = pitch - _lastPitch;
                    float lookDelta = Math.Abs(yawDelta) + Math.Abs(pitchDelta);

                    _movementScore += distance + lookDelta * _owner.HoldStillLookWeightCfg * _owner.HoldStillLookContributionScaleCfg;
                }

                _lastX = x;
                _lastY = y;
                _lastZ = z;
                _lastYaw = yaw;
                _lastPitch = pitch;
                _hasLastSample = true;
            }

            // Normalizes a yaw delta into the shortest signed angle distance so turn scoring wraps correctly.
            private static float AngleDeltaRadians(float from, float to)
            {
                float diff = from - to;
                float twoPi = (float)(Math.PI * 2.0);

                while (diff > Math.PI) diff -= twoPi;
                while (diff < -Math.PI) diff += twoPi;

                return diff;
            }

            // Truncates movement scoring so packet values stay stable and human-readable.
            private static float TruncateToTwoDecimals(float value)
            {
                return MathF.Truncate(value * 100f) / 100f;
            }

            // Sends the final PhotoTaken packet only after both screenshot completion and hold-still delay have completed.
            private void TrySendPacket()
            {
                if (_owner.ClientChannel == null) return;
                if (!_captureReady) return;
                if (_active) return;

                string? photoId = _photoId;
                if (string.IsNullOrEmpty(photoId)) return;

                _owner.ClientChannel.SendPacket(new PhotoTakenPacket()
                {
                    PhotoId = photoId,
                    HoldStillSeconds = _owner.HoldStillDurationSecondsCfg,
                    HoldStillMovement = TruncateToTwoDecimals(_movementScore)
                });

                _photoId = null;
                _captureReady = false;
                _movementScore = 0f;
                _hasLastSample = false;
            }
        }
    }
}
