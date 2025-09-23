using Content.Server._Sunrise.BloodCult.GameRule;
using Content.Server.Administration;
using Content.Shared._Sunrise.BloodCult.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server._Sunrise.BloodCult.Commands;

[AdminCommand(AdminFlags.Admin)]
public sealed class AddCultTargetCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "bloodcult_addtarget";
    public string Description => "Add a target to the Blood cult";
    public string Help => "Usage: bloodcult_addtarget <ckey>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError("Usage: bloodcult_addtarget <ckey>");
            return;
        }

        var ckey = args[0];
        var playerManager = IoCManager.Resolve<Robust.Server.Player.IPlayerManager>();
        
        if (!playerManager.TryGetSessionByUsername(ckey, out var session) || session.AttachedEntity == null)
        {
            shell.WriteError($"Player with ckey '{ckey}' not found or not in game.");
            return;
        }

        var entityUid = session.AttachedEntity.Value;

        if (!_entManager.EntitySysManager.TryGetEntitySystem<BloodCultRuleSystem>(out var cultRuleSystem))
        {
            shell.WriteError("Blood cult system not found.");
            return;
        }

        var rule = cultRuleSystem.GetRule();
        if (rule == null)
        {
            shell.WriteError("No active Blood cult rule found.");
            return;
        }

        // Use the system method to add target properly
        if (!cultRuleSystem.AddSpecificCultTarget(entityUid, rule))
        {
            shell.WriteError("Entity is already a cult target.");
            return;
        }

        shell.WriteLine($"Added {_entManager.GetComponent<MetaDataComponent>(entityUid).EntityName} as a cult target.");
    }
}
