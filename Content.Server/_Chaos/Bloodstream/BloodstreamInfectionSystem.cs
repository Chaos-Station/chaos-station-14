using Content.Goobstation.Maths.FixedPoint;
using Content.Server.Body.Systems;
using Content.Shared._Chaos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Content.Shared._Shitmed.Medical.Surgery.Pain.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using LegacyStatusEffectsSystem = Content.Shared.StatusEffect.StatusEffectsSystem;
using NewStatusEffectsSystem = Content.Shared.StatusEffectNew.StatusEffectsSystem;

namespace Content.Server._Chaos.Bloodstream;

public sealed class BloodstreamInfectionSystem : EntitySystem
{
    private const string InfectionPainModifierId = "BloodstreamInfection";
    private const string CeffenafReagentId = "Ceffenaf";
    private const string TerbinarReagentId = "Terbinar";
    private static readonly ReagentId CeffenafReagent = new(CeffenafReagentId, null);
    private static readonly ReagentId TerbinarReagent = new(TerbinarReagentId, null);
    private static readonly FixedPoint2 CeffenafRequiredQuantity = FixedPoint2.New(14);
    private static readonly FixedPoint2 TerbinarRequiredQuantity = FixedPoint2.New(10);
    private static readonly TimeSpan InfectionAttemptInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CeffenafRollbackDelay = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan InfectionStage2Start = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan InfectionStage3Start = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan InfectionStage4Start = TimeSpan.FromSeconds(140);
    private static readonly TimeSpan InfectionStage5Start = TimeSpan.FromSeconds(200);
    private static readonly TimeSpan InfectionStage6Start = TimeSpan.FromSeconds(270);
    private static readonly TimeSpan InfectionBlindnessRefreshDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InfectionComaRefreshDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InfectionBriefFaintDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InfectionFaintInterval = TimeSpan.FromSeconds(30);

    private static readonly EntProtoId InfectionFaintStatusEffect = "StatusEffectBloodstreamInfectionFaint";
    private static readonly EntProtoId InfectionComaStatusEffect = "StatusEffectBloodstreamInfectionComa";

    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LegacyStatusEffectsSystem _legacyStatusEffects = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PainSystem _pain = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NewStatusEffectsSystem _statusEffects = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodstreamInfectionComponent, RejuvenateEvent>(OnRejuvenate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var bloodstream))
        {
            var infection = EnsureComp<BloodstreamInfectionComponent>(uid);

            if (_mobState.IsDead(uid))
                continue;

            TryRollBloodstreamInfection(uid, bloodstream, infection, curTime);
            UpdateInfectionStage(uid, infection, curTime);
            ProcessTreatments(uid, bloodstream, infection, curTime);
            ProcessInfectionEffects(uid, infection, curTime);
        }
    }

    private void OnRejuvenate(Entity<BloodstreamInfectionComponent> ent, ref RejuvenateEvent args)
    {
        ClearInfection(ent.Owner, ent.Comp);
    }

    private void TryRollBloodstreamInfection(EntityUid uid, BloodstreamComponent bloodstream, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        if (infection.Infected)
            return;

        if (bloodstream.BleedAmountFromWounds <= 0)
        {
            infection.NextInfectionAttempt = TimeSpan.Zero;
            return;
        }

        var bloodPercentage = _bloodstream.GetBloodLevelPercentage((uid, bloodstream));
        var chance = GetInfectionChance(bloodPercentage);
        if (chance <= 0f)
        {
            infection.NextInfectionAttempt = TimeSpan.Zero;
            return;
        }

        if (infection.NextInfectionAttempt != TimeSpan.Zero && curTime < infection.NextInfectionAttempt)
            return;

        infection.NextInfectionAttempt = curTime + InfectionAttemptInterval;
        TryStartInfection(uid, infection, curTime, chance, bloodPercentage);
    }

    private bool TryStartInfection(EntityUid uid, BloodstreamInfectionComponent infection, TimeSpan curTime, float chance, float bloodPercentage)
    {
        if (!_random.Prob(chance))
            return false;

        var initialElapsed = GetInitialInfectionElapsed(bloodPercentage);
        infection.Infected = true;
        DirtyField(uid, infection, nameof(BloodstreamInfectionComponent.Infected));
        infection.InfectionStartTime = curTime - initialElapsed;
        infection.NextInfectionAttempt = TimeSpan.Zero;
        SetCurrentStage(uid, infection, GetInfectionStage(initialElapsed));
        return true;
    }

    public bool ForceInfect(EntityUid uid, BloodstreamInfectionStage stage = BloodstreamInfectionStage.Stage4)
    {
        if (stage == BloodstreamInfectionStage.None || !HasComp<BloodstreamComponent>(uid) || _mobState.IsDead(uid))
            return false;

        var infection = EnsureComp<BloodstreamInfectionComponent>(uid);
        var elapsed = GetInfectionStageStart(stage);

        infection.Infected = true;
        DirtyField(uid, infection, nameof(BloodstreamInfectionComponent.Infected));
        infection.InfectionStartTime = _timing.CurTime - elapsed;
        infection.NextInfectionAttempt = TimeSpan.Zero;
        infection.NextToxinDamage = TimeSpan.Zero;
        infection.NextFaintAttempt = TimeSpan.Zero;
        infection.PendingCeffenafRollbackTime = TimeSpan.Zero;
        infection.PendingCeffenafTargetStage = BloodstreamInfectionStage.None;
        infection.LastProcessedStage = BloodstreamInfectionStage.None;

        SetCurrentStage(uid, infection, stage);
        return true;
    }

    private void UpdateInfectionStage(EntityUid uid, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        var newStage = BloodstreamInfectionStage.None;
        if (infection.Infected)
            newStage = GetInfectionStage(curTime - infection.InfectionStartTime);

        SetCurrentStage(uid, infection, newStage);
    }

    private void SetCurrentStage(EntityUid uid, BloodstreamInfectionComponent infection, BloodstreamInfectionStage newStage)
    {
        if (infection.CurrentStage == newStage)
            return;

        infection.CurrentStage = newStage;
        DirtyField(uid, infection, nameof(BloodstreamInfectionComponent.CurrentStage));
    }

    private void ProcessInfectionEffects(EntityUid uid, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        var stage = infection.CurrentStage;
        if (stage != infection.LastProcessedStage)
        {
            OnStageChanged(uid, infection, stage);
            infection.LastProcessedStage = stage;
        }

        switch (stage)
        {
            case BloodstreamInfectionStage.Stage3:
                TryProcessToxinDamage(uid, ref infection.NextToxinDamage, curTime, TimeSpan.FromSeconds(5), 0.5f);
                break;
            case BloodstreamInfectionStage.Stage4:
                TryProcessToxinDamage(uid, ref infection.NextToxinDamage, curTime, TimeSpan.FromSeconds(3), 2f);
                break;
            case BloodstreamInfectionStage.Stage5:
                TryProcessToxinDamage(uid, ref infection.NextToxinDamage, curTime, TimeSpan.FromSeconds(3), 5f);
                EnsureBlindness(uid);
                TryProcessTimedEffect(ref infection.NextFaintAttempt, curTime, InfectionFaintInterval, 0.15f, () =>
                {
                    _statusEffects.TrySetStatusEffectDuration(uid, InfectionFaintStatusEffect, InfectionBriefFaintDuration);
                });
                break;
            case BloodstreamInfectionStage.Stage6:
                TryProcessToxinDamage(uid, ref infection.NextToxinDamage, curTime, TimeSpan.FromSeconds(3), 10f);
                EnsureBlindness(uid);
                _statusEffects.TrySetStatusEffectDuration(uid, InfectionComaStatusEffect, InfectionComaRefreshDuration);
                break;
        }
    }

    private void ProcessTreatments(EntityUid uid, BloodstreamComponent bloodstream, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        TryApplyPendingCeffenaf(uid, infection, curTime);

        if (!infection.Infected)
            return;

        if (!_solutionContainer.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicalSolution))
            return;

        if (infection.CurrentStage == BloodstreamInfectionStage.Stage1
            && chemicalSolution.GetReagentQuantity(TerbinarReagent) >= TerbinarRequiredQuantity)
        {
            _solutionContainer.RemoveReagent(bloodstream.ChemicalSolution.Value, TerbinarReagentId, TerbinarRequiredQuantity);
            ClearInfection(uid, infection);
            return;
        }

        if (infection.PendingCeffenafRollbackTime == TimeSpan.Zero
            && chemicalSolution.GetReagentQuantity(CeffenafReagent) >= CeffenafRequiredQuantity)
        {
            _solutionContainer.RemoveReagent(bloodstream.ChemicalSolution.Value, CeffenafReagentId, CeffenafRequiredQuantity);
            infection.PendingCeffenafTargetStage = GetRolledBackStage(infection.CurrentStage);
            infection.PendingCeffenafRollbackTime = curTime + CeffenafRollbackDelay;
        }
    }

    private void TryApplyPendingCeffenaf(EntityUid uid, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        if (infection.PendingCeffenafRollbackTime == TimeSpan.Zero || curTime < infection.PendingCeffenafRollbackTime)
            return;

        infection.PendingCeffenafRollbackTime = TimeSpan.Zero;

        if (!infection.Infected)
            return;

        ApplyCeffenafRollback(uid, infection, curTime);
    }

    private void OnStageChanged(EntityUid uid, BloodstreamInfectionComponent infection, BloodstreamInfectionStage stage)
    {
        infection.NextToxinDamage = TimeSpan.Zero;
        infection.NextFaintAttempt = TimeSpan.Zero;

        UpdatePain(uid, stage);

        if (stage < BloodstreamInfectionStage.Stage5)
        {
            _legacyStatusEffects.TryRemoveStatusEffect(uid, TemporaryBlindnessSystem.BlindingStatusEffect);
            _statusEffects.TryRemoveStatusEffect(uid, InfectionFaintStatusEffect);
        }

        if (stage < BloodstreamInfectionStage.Stage6)
            _statusEffects.TryRemoveStatusEffect(uid, InfectionComaStatusEffect);
    }

    private void UpdatePain(EntityUid uid, BloodstreamInfectionStage stage)
    {
        var amount = stage switch
        {
            BloodstreamInfectionStage.Stage1 => FixedPoint2.New(4),
            BloodstreamInfectionStage.Stage2 => FixedPoint2.New(8),
            BloodstreamInfectionStage.Stage3 => FixedPoint2.New(8),
            BloodstreamInfectionStage.Stage4 => FixedPoint2.New(8),
            BloodstreamInfectionStage.Stage5 => FixedPoint2.New(18),
            BloodstreamInfectionStage.Stage6 => FixedPoint2.New(18),
            _ => FixedPoint2.Zero,
        };

        foreach (var (bodyPart, _) in _body.GetBodyChildren(uid))
        {
            if (amount == FixedPoint2.Zero)
            {
                _pain.TryRemovePainFeelsModifier(uid, InfectionPainModifierId, bodyPart);
                continue;
            }

            if (!_pain.TryChangePainFeelsModifier(uid, InfectionPainModifierId, bodyPart, amount))
                _pain.TryAddPainFeelsModifier(uid, InfectionPainModifierId, bodyPart, amount);
        }
    }

    private void EnsureBlindness(EntityUid uid)
    {
        _legacyStatusEffects.TryAddStatusEffect(
            uid,
            TemporaryBlindnessSystem.BlindingStatusEffect,
            InfectionBlindnessRefreshDuration,
            true,
            TemporaryBlindnessSystem.BlindingStatusEffect);
    }

    private void TryProcessToxinDamage(EntityUid uid, ref TimeSpan nextAttempt, TimeSpan curTime, TimeSpan interval, float amount)
    {
        if (nextAttempt == TimeSpan.Zero)
        {
            nextAttempt = curTime + interval;
            return;
        }

        if (curTime < nextAttempt)
            return;

        nextAttempt = curTime + interval;
        _damageable.TryChangeDamage(
            uid,
            new DamageSpecifier(_prototype.Index<DamageTypePrototype>("Poison"), FixedPoint2.New(amount)),
            ignoreResistances: false,
            interruptsDoAfters: false);
    }

    private void TryProcessTimedEffect(ref TimeSpan nextAttempt, TimeSpan curTime, TimeSpan interval, float chance, Action effect)
    {
        if (nextAttempt == TimeSpan.Zero)
        {
            nextAttempt = curTime + interval;
            return;
        }

        if (curTime < nextAttempt)
            return;

        nextAttempt = curTime + interval;
        if (_random.Prob(chance))
            effect();
    }

    private void ClearInfection(EntityUid uid, BloodstreamInfectionComponent infection)
    {
        infection.Infected = false;
        DirtyField(uid, infection, nameof(BloodstreamInfectionComponent.Infected));
        infection.InfectionStartTime = TimeSpan.Zero;
        infection.NextInfectionAttempt = TimeSpan.Zero;
        infection.NextToxinDamage = TimeSpan.Zero;
        infection.NextFaintAttempt = TimeSpan.Zero;
        infection.PendingCeffenafRollbackTime = TimeSpan.Zero;
        infection.PendingCeffenafTargetStage = BloodstreamInfectionStage.None;
        infection.LastProcessedStage = BloodstreamInfectionStage.None;
        SetCurrentStage(uid, infection, BloodstreamInfectionStage.None);

        UpdatePain(uid, BloodstreamInfectionStage.None);
        _legacyStatusEffects.TryRemoveStatusEffect(uid, TemporaryBlindnessSystem.BlindingStatusEffect);
        _statusEffects.TryRemoveStatusEffect(uid, InfectionFaintStatusEffect);
        _statusEffects.TryRemoveStatusEffect(uid, InfectionComaStatusEffect);
    }

    private void ApplyCeffenafRollback(EntityUid uid, BloodstreamInfectionComponent infection, TimeSpan curTime)
    {
        var targetStage = infection.PendingCeffenafTargetStage;
        infection.PendingCeffenafTargetStage = BloodstreamInfectionStage.None;

        if (targetStage == BloodstreamInfectionStage.None)
        {
            ClearInfection(uid, infection);
            return;
        }

        if (infection.CurrentStage != BloodstreamInfectionStage.None && infection.CurrentStage <= targetStage)
            return;

        infection.InfectionStartTime = curTime - GetInfectionStageStart(targetStage);
        infection.NextToxinDamage = TimeSpan.Zero;
        infection.NextFaintAttempt = TimeSpan.Zero;
        SetCurrentStage(uid, infection, targetStage);
    }

    private static BloodstreamInfectionStage GetRolledBackStage(BloodstreamInfectionStage currentStage)
    {
        if (currentStage <= BloodstreamInfectionStage.Stage1)
            return BloodstreamInfectionStage.None;

        return (BloodstreamInfectionStage) (currentStage - 1);
    }

    private static float GetInfectionChance(float bloodPercentage)
    {
        if (bloodPercentage <= 0.30f)
            return 0.45f;

        if (bloodPercentage <= 0.50f)
            return 0.35f;

        if (bloodPercentage <= 0.70f)
            return 0.15f;

        return 0f;
    }

    public static BloodstreamInfectionStage GetInfectionStage(TimeSpan infectionDuration)
    {
        if (infectionDuration < InfectionStage2Start)
            return BloodstreamInfectionStage.Stage1;

        if (infectionDuration < InfectionStage3Start)
            return BloodstreamInfectionStage.Stage2;

        if (infectionDuration < InfectionStage4Start)
            return BloodstreamInfectionStage.Stage3;

        if (infectionDuration < InfectionStage5Start)
            return BloodstreamInfectionStage.Stage4;

        if (infectionDuration < InfectionStage6Start)
            return BloodstreamInfectionStage.Stage5;

        return BloodstreamInfectionStage.Stage6;
    }

    private static TimeSpan GetInitialInfectionElapsed(float bloodPercentage)
    {
        if (bloodPercentage <= 0.30f)
            return InfectionStage4Start;

        if (bloodPercentage <= 0.50f)
            return InfectionStage3Start;

        if (bloodPercentage <= 0.70f)
            return InfectionStage2Start;

        return TimeSpan.Zero;
    }

    private static TimeSpan GetInfectionStageStart(BloodstreamInfectionStage stage)
    {
        return stage switch
        {
            BloodstreamInfectionStage.Stage1 => TimeSpan.Zero,
            BloodstreamInfectionStage.Stage2 => InfectionStage2Start,
            BloodstreamInfectionStage.Stage3 => InfectionStage3Start,
            BloodstreamInfectionStage.Stage4 => InfectionStage4Start,
            BloodstreamInfectionStage.Stage5 => InfectionStage5Start,
            BloodstreamInfectionStage.Stage6 => InfectionStage6Start,
            _ => TimeSpan.Zero,
        };
    }
}
