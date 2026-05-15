# Plate Processing Flow

Runtime map for ground plate sensitization and development tray progression. Updated: 2026-05-12.

## Triggers

- Right-click a placed glass plate with a chemical / polish item.
- Placed coated plate with a pending Dry sensitization step ticks down passively (no input).
- Right-click the development tray with a plate or processing reagent.

## First entry points

- Ground plate interaction: [src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.Interaction.cs](../src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.Interaction.cs); chemistry-aware partial in [src/Features/PlateLifecycle/Chemistry/BlockGlassPlate.PlateLifecycleChemistry.cs](../src/Features/PlateLifecycle/Chemistry/BlockGlassPlate.PlateLifecycleChemistry.cs); shared plate-placement helper in [src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.PlateLifecycleIntegration.cs](../src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.PlateLifecycleIntegration.cs).
- Development tray interaction: [src/Features/PlateLifecycle/Tray/Block/BlockDevelopmentTray.Interaction.cs](../src/Features/PlateLifecycle/Tray/Block/BlockDevelopmentTray.Interaction.cs) and [.Interaction.ClientServer.cs](../src/Features/PlateLifecycle/Tray/Block/BlockDevelopmentTray.Interaction.ClientServer.cs); plate-lifecycle partial in [.PlateLifecycle.cs](../src/Features/PlateLifecycle/Tray/Block/BlockDevelopmentTray.PlateLifecycle.cs).

## Ground sensitization path

1. `BlockGlassPlate.OnBlockInteractStart` (in `Interaction.cs`) routes the placed-plate interaction. RMB only engages for Chemical steps and polish; Dry steps fall through.
2. The chemistry partial resolves the current process via [`ProcessRegistry`](../src/Features/PlateLifecycle/Chemistry/ProcessRegistry.cs) and the next expected step.
3. [`PlateSensitizationService`](../src/Features/PlateLifecycle/Chemistry/PlateSensitizationService.cs) validates the next chemical step (or skips a contiguous run of Dry steps when the player holds the next matching chemical) and advances plate state.
4. [`PlateStateService`](../src/Features/PlateLifecycle/State/PlateStateService.cs) writes process / stage / naming attributes onto the resulting plate stack; [`PlateStateAttributes`](../src/Features/PlateLifecycle/State/PlateStateAttributes.cs) defines keys; [`PlateStateTransitions`](../src/Features/PlateLifecycle/State/PlateStateTransitions.cs) coordinates multi-attribute transitions.
5. [`BlockEntityPlateProcessState`](../src/Features/PlateLifecycle/BlockEntity/BlockEntityPlateProcessState.cs) persists process lock and step index for the placed ground plate, and runs the passive air-dry countdown (1 Hz server tick). On elapse it calls back into `BlockGlassPlate.OnDryWaitElapsed` which advances the Dry step (or finalizes the sensitized plate).
6. Polish path is owned by [`BlockGlassPlate.PolishInteraction.cs`](../src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.PolishInteraction.cs).

## Development tray path

1. `BlockDevelopmentTray.OnBlockInteractStart` routes insert / remove / action requests via the interaction partials.
2. [`BlockEntityDevelopmentTray`](../src/Features/PlateLifecycle/Tray/BlockEntity/BlockEntityDevelopmentTray.cs) owns the inserted plate stack and runtime tray state. Client-side mesh rebuild is split across `BlockEntityDevelopmentTray.Client.cs` and `.ClientMesh.{Tray,Plate,Overlay,Queue}.cs`.
3. Timing + spec resolution lives in [src/Features/PlateLifecycle/Tray/Runtime/](../src/Features/PlateLifecycle/Tray/Runtime): [`TrayDurationProvider`](../src/Features/PlateLifecycle/Tray/Runtime/TrayDurationProvider.cs), [`TrayDevelopmentSpecResolver`](../src/Features/PlateLifecycle/Tray/Runtime/TrayDevelopmentSpecResolver.cs), [`TrayTimedInteractionState`](../src/Features/PlateLifecycle/Tray/Runtime/TrayTimedInteractionState.cs).
4. Chemistry semantics are deferred to [`PlateDevelopmentService`](../src/Features/PlateLifecycle/Chemistry/PlateDevelopmentService.cs) and `PlateStateService`.
5. Client latch (timed interaction UX): [`DevTrayLatch.Client.cs`](../src/Features/PlateLifecycle/Tray/Client/DevTrayLatch.Client.cs).

## State owners

| Concern | Owner |
| --- | --- |
| Process registry + built-in process definitions | [`ProcessRegistry`](../src/Features/PlateLifecycle/Chemistry/ProcessRegistry.cs), [`PhotographyProcessDefinition`](../src/Features/PlateLifecycle/Chemistry/PhotographyProcessDefinition.cs) |
| Sensitization step model + service | [`SensitizationStep`](../src/Features/PlateLifecycle/Chemistry/SensitizationStep.cs), [`PlateSensitizationService`](../src/Features/PlateLifecycle/Chemistry/PlateSensitizationService.cs) |
| Development step model + service | [`DevelopmentStep`](../src/Features/PlateLifecycle/Chemistry/DevelopmentStep.cs), [`PlateDevelopmentService`](../src/Features/PlateLifecycle/Chemistry/PlateDevelopmentService.cs) |
| Plate stage / process / name attributes | [`PlateStage`](../src/Features/PlateLifecycle/State/PlateStage.cs), [`PlateStateAttributes`](../src/Features/PlateLifecycle/State/PlateStateAttributes.cs), [`PlateStateService`](../src/Features/PlateLifecycle/State/PlateStateService.cs), [`PlateStateTransitions`](../src/Features/PlateLifecycle/State/PlateStateTransitions.cs) |
| Wet-plate drying (vanilla `EnumTransitionType.Dry` wrapper) | [`PlateDryingTransition`](../src/Shared/PlateDryingTransition.cs), back-compat surface in [`WetPlateAttrs`](../src/Shared/WetPlateAttrs.cs); plate JSON `transitionableProps` blocks; per-stack overrides via `ItemPlateBase`. |
| Placed ground-plate process progress + passive dry timer | [`BlockEntityPlateProcessState`](../src/Features/PlateLifecycle/BlockEntity/BlockEntityPlateProcessState.cs) |
| Tray inserted plate runtime | [`BlockEntityDevelopmentTray`](../src/Features/PlateLifecycle/Tray/BlockEntity/BlockEntityDevelopmentTray.cs) |
| Tray timing + spec config | [`DevelopmentTrayInteractionConfig`](../src/Features/PlateLifecycle/Tray/Config/DevelopmentTrayInteractionConfig.cs), [`TimedInteractionConfig`](../src/Features/AdminTooling/Config/TimedInteractionConfig.cs) |
| Camera/exposure rules feeding plates | [`ExposureParameters`](../src/Features/PlateLifecycle/Chemistry/ExposureParameters.cs), [`CameraPlateEligibility`](../src/Features/PlateLifecycle/CameraPlateEligibility.cs) |
| Process registry resolution helper | [`ProcessRegistryLookup`](../src/Shared/ProcessRegistryLookup.cs) |

## Client / server boundary

- **Server owns**: world writes, chemical consumption, plate-stack replacement, tray progression authority.
- **Client owns**: tray mesh rebuild, overlay presentation, timed-interaction latch UX.

## Where to add X

| Goal | File(s) to edit |
| --- | --- |
| Add a new photography process | [`ProcessRegistry`](../src/Features/PlateLifecycle/Chemistry/ProcessRegistry.cs) (registration), define a [`PhotographyProcessDefinition`](../src/Features/PlateLifecycle/Chemistry/PhotographyProcessDefinition.cs), reference it from `PlateStateService` if it needs new attribute semantics. |
| Add or change a sensitization step | [`SensitizationStep`](../src/Features/PlateLifecycle/Chemistry/SensitizationStep.cs), validation in [`PlateSensitizationService`](../src/Features/PlateLifecycle/Chemistry/PlateSensitizationService.cs). |
| Add or change a development step | [`DevelopmentStep`](../src/Features/PlateLifecycle/Chemistry/DevelopmentStep.cs), validation in [`PlateDevelopmentService`](../src/Features/PlateLifecycle/Chemistry/PlateDevelopmentService.cs). |
| Add a plate stage | [`PlateStage`](../src/Features/PlateLifecycle/State/PlateStage.cs) and the coordinator/attributes pair. |
| Change ground-plate interaction | [`BlockGlassPlate.Interaction.cs`](../src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.Interaction.cs); shared plate-placement semantics in [`BlockGlassPlate.PlateLifecycleIntegration.cs`](../src/Features/PlateLifecycle/GroundPlate/BlockGlassPlate.PlateLifecycleIntegration.cs); chemistry-aware decisions in [`Chemistry/BlockGlassPlate.PlateLifecycleChemistry.cs`](../src/Features/PlateLifecycle/Chemistry/BlockGlassPlate.PlateLifecycleChemistry.cs). |
| Change tray gating / timing | [`DevelopmentTrayInteractionConfig`](../src/Features/PlateLifecycle/Tray/Config/DevelopmentTrayInteractionConfig.cs) (knobs), [`TrayDurationProvider`](../src/Features/PlateLifecycle/Tray/Runtime/TrayDurationProvider.cs) (logic). |
| Tune plate-processing config | [`PlateProcessingConfig`](../src/Features/AdminTooling/Config/PlateProcessingConfig.cs). |

## Related docs

- [FLOW_CAMERA.md](FLOW_CAMERA.md) — sensitized → exposed transition triggered by capture.
- [FLOW_PHOTO_DISPLAY.md](FLOW_PHOTO_DISPLAY.md) — exposed plate → developed plate render path.
