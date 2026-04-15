using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Chaos;

public enum BloodstreamInfectionStage : byte
{
    None,
    Stage1,
    Stage2,
    Stage3,
    Stage4,
    Stage5,
    Stage6,
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class BloodstreamInfectionComponent : Component
{
    [DataField, AutoNetworkedField]
    public BloodstreamInfectionStage CurrentStage;

    [DataField, AutoNetworkedField]
    public bool Infected;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan InfectionStartTime;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextInfectionAttempt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextToxinDamage;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFaintAttempt;

    public BloodstreamInfectionStage LastProcessedStage;
    public BloodstreamInfectionStage LastVisualizedStage;
}
