using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace StS2AP.Patches;

/// <summary>
/// Lets singleplayer treasure chests offer the character's own relics.
/// </summary>
/// <remarks>
/// The base game fills chests from the run's shared relic bag, which holds only the shared relic
/// pool. That suits multiplayer, where every player votes on the same chest and could otherwise
/// take another character's relic, but singleplayer shares the code and so never finds a
/// character relic in a chest. Elite rewards and events already draw from the player's own bag,
/// which holds both pools; with one player the chest draws from it the same way. The rarity roll
/// and its RNG are unchanged.
/// </remarks>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
public static class Patches_CharacterRelicsInChests
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo sharedPull = AccessTools.Method(
            typeof(RelicGrabBag),
            nameof(RelicGrabBag.PullFromFront),
            [typeof(RelicRarity), typeof(IRunState)]
        );
        MethodInfo chestPull = AccessTools.Method(
            typeof(Patches_CharacterRelicsInChests),
            nameof(PullChestRelic)
        );

        // Every chest candidate comes from this call: one per player, plus Blitz's extra relics
        // on 0.113. The replacement takes the same stack (bag, rarity, run state).
        List<CodeInstruction> pulls = code.Where(instruction => instruction.Calls(sharedPull)).ToList();
        if (pulls.Count == 0)
        {
            // A game update changed how chests pick relics; fail startup so the update check
            // reports it rather than silently returning to shared-pool-only chests.
            throw new InvalidOperationException(
                "Could not find the shared relic pull in TreasureRoomRelicSynchronizer.BeginRelicPicking.");
        }
        foreach (CodeInstruction pull in pulls)
        {
            pull.opcode = System.Reflection.Emit.OpCodes.Call;
            pull.operand = chestPull;
        }
        return code;
    }

    private static RelicModel? PullChestRelic(RelicGrabBag sharedBag, RelicRarity rarity, IRunState runState)
    {
        if (runState.Players.Count != 1)
            return sharedBag.PullFromFront(rarity, runState);

        // Takes the relic out of both bags, as elite rewards do, so neither offers it again.
        return RelicFactory.PullNextRelicFromFront(runState.Players[0], rarity);
    }
}
