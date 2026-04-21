using Content.Server.Administration;
using Content.Server._Chaos.Bloodstream;
using Content.Shared.Administration;
using Content.Shared._Chaos;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._Chaos.Administration.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class InfectBloodstreamCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "infect_bloodstream";
    public string Description => "Instantly infects an entity with bloodstream infection.";
    public string Help => $"Usage: {Command} <entity uid> [stage 1-6]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var uidNet) || !_entManager.TryGetEntity(uidNet, out var uid))
        {
            shell.WriteError("Invalid entity uid.");
            return;
        }

        var stage = BloodstreamInfectionStage.Stage4;
        if (args.Length == 2)
        {
            if (!int.TryParse(args[1], out var stageInt) || stageInt < 1 || stageInt > 6)
            {
                shell.WriteError("Stage must be a number from 1 to 6.");
                return;
            }

            stage = (BloodstreamInfectionStage) stageInt;
        }

        var system = _entManager.EntitySysManager.GetEntitySystem<BloodstreamInfectionSystem>();
        if (!system.ForceInfect(uid.Value, stage))
        {
            shell.WriteError("Could not infect this entity.");
            return;
        }

        shell.WriteLine($"Entity {uid.Value} infected at stage {(int) stage}.");
    }
}
