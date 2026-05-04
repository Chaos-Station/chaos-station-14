using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Chaos;

public sealed class SharedBloodstreamInfectionSystem : EntitySystem
{
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodstreamInfectionComponent, GetEyeOffsetEvent>(OnGetEyeOffset);
        SubscribeLocalEvent<BloodstreamInfectionComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeedModifiers);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BloodstreamInfectionComponent>();
        while (query.MoveNext(out var uid, out var infection))
        {
            if (infection.LastVisualizedStage == infection.CurrentStage)
                continue;

            infection.LastVisualizedStage = infection.CurrentStage;
            _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);

            if (TryComp<EyeComponent>(uid, out var eye))
                _contentEye.UpdateEyeOffset((uid, eye));
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<BloodstreamInfectionComponent, EyeComponent>();
        while (query.MoveNext(out var uid, out var infection, out var eye))
        {
            if (infection.CurrentStage >= BloodstreamInfectionStage.Stage4 || eye.Offset != Vector2.Zero)
                _contentEye.UpdateEyeOffset((uid, eye));
        }
    }

    private void OnRefreshMovementSpeedModifiers(Entity<BloodstreamInfectionComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = GetInfectionMovementModifier(ent.Comp.CurrentStage);
        if (modifier < 1f)
            args.ModifySpeed(modifier);
    }

    private void OnGetEyeOffset(Entity<BloodstreamInfectionComponent> ent, ref GetEyeOffsetEvent args)
    {
        if (ent.Comp.CurrentStage < BloodstreamInfectionStage.Stage4)
            return;

        var time = (float) _timing.CurTime.TotalSeconds;
        var amplitude = GetInfectionEyeOffsetAmplitude(ent.Comp.CurrentStage);
        var speed = GetInfectionEyeOffsetFrequency(ent.Comp.CurrentStage);

        args.Offset += new Vector2(
            MathF.Sin(time * speed) * amplitude,
            MathF.Cos(time * speed * 0.85f) * amplitude * 0.5f);
    }

    private static float GetInfectionMovementModifier(BloodstreamInfectionStage stage)
    {
        return stage switch
        {
            BloodstreamInfectionStage.Stage1 => 0.90f,
            BloodstreamInfectionStage.Stage2 => 0.80f,
            BloodstreamInfectionStage.Stage3 => 0.70f,
            BloodstreamInfectionStage.Stage4 => 0.60f,
            BloodstreamInfectionStage.Stage5 => 0.50f,
            BloodstreamInfectionStage.Stage6 => 0.50f,
            _ => 1f,
        };
    }

    private static float GetInfectionEyeOffsetAmplitude(BloodstreamInfectionStage stage)
    {
        return stage switch
        {
            BloodstreamInfectionStage.Stage4 => 0.03f,
            BloodstreamInfectionStage.Stage5 => 0.055f,
            BloodstreamInfectionStage.Stage6 => 0.08f,
            _ => 0f,
        };
    }

    private static float GetInfectionEyeOffsetFrequency(BloodstreamInfectionStage stage)
    {
        return stage switch
        {
            BloodstreamInfectionStage.Stage4 => 1.9f,
            BloodstreamInfectionStage.Stage5 => 2.5f,
            BloodstreamInfectionStage.Stage6 => 3.1f,
            _ => 0f,
        };
    }
}
