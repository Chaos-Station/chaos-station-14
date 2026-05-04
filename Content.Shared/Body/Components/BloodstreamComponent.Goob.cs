using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Body.Components;

public sealed partial class BloodstreamComponent
{
    /// <summary>
    /// Goobstation - Prevents this entity from absorbing reagents from smoke/foam.
    /// </summary>
    [DataField]
    public bool SmokeImmune;

    /// <summary>
    /// Separated bleeding to base bleeding for simple mobs and abilities and bleeds
    /// based on BleedInflictors from wounds
    /// WoundMed Change
    [DataField, AutoNetworkedField]
    public float BleedAmountFromWounds;

    [DataField, AutoNetworkedField]
    public float BleedAmountNotFromWounds;
    // Chaos-Station-Start
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBloodlossKnockdownAttempt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBloodlossItemDropAttempt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextBloodlossFaintAttempt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan MinimumBloodlossUnconsciousUntil;

    public BloodlossStage LastProcessedBloodlossStage;
    // Chaos-Station-End
}
