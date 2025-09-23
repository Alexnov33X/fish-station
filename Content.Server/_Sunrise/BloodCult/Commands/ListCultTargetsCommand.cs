using Content.Server._Sunrise.BloodCult.GameRule;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Sunrise.BloodCult.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class ListCultTargetsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "bloodcult_listtargets";
    public string Description => "List all current Blood cult targets";
    public string Help => "Usage: bloodcult_listtargets";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!_entManager.EntitySysManager.TryGetEntitySystem<BloodCultRuleSystem>(out var cultRuleSystem))
        {
            shell.WriteError("Blood cult system not found.");
            return;
        }

        var rule = cultRuleSystem.GetRule();
        if (rule?.CultTargets == null || rule.CultTargets.Count == 0)
        {
            shell.WriteLine("No cult targets found.");
            return;
        }

        shell.WriteLine($"Current cult targets ({rule.CultTargets.Count}):");
        foreach (var (target, isSacrificed) in rule.CultTargets)
        {
            var targetName = _entManager.GetComponent<MetaDataComponent>(target).EntityName;
            var status = isSacrificed ? "Sacrificed" : "Alive";
            shell.WriteLine($"  {targetName} ({target}) - {status}");
        }
    }
}
