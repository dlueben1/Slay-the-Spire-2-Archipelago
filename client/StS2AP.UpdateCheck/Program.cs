using System.Runtime.Loader;
using System.Text.Json;
using StS2AP.UpdateCheck;

// Checks a built client against a game version without launching the game, and writes the findings
// as JSON. scripts/check_game_update.py builds the client, runs both steps and renders the report.
//
//   StS2AP.UpdateCheck attach --mod <dir> --game <dir> --game-data <dir> --output <file>
//   StS2AP.UpdateCheck compare --game <dir> --previous-game <dir> --game-data <dir> --patched <file> --output <file>
//
// They are separate runs because compare allocates heavily, and a garbage collection can crash a
// process holding the half-built detour that a failed patch leaves behind.

string step = args[0];
Dictionary<string, string> options = args[1..]
    .Chunk(2)
    .ToDictionary(pair => pair[0].TrimStart('-'), pair => Path.GetFullPath(pair[1]));
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

object findings;
if (step == "attach")
{
    // The checked game's DLLs win, so Harmony and the game code are that version's. This must be in
    // place before any method that uses Harmony types is compiled.
    string[] searchOrder = [options["game"], options["mod"], options["game-data"]];
    AssemblyLoadContext.Default.Resolving += (context, name) => searchOrder
        .Select(directory => Path.Combine(directory, name.Name + ".dll"))
        .Where(File.Exists)
        .Select(context.LoadFromAssemblyPath)
        .FirstOrDefault();
    GC.TryStartNoGCRegion(200_000_000);
    findings = PatchAttachCheck.Run(options["mod"]);
}
else if (step == "compare")
{
    var patched = JsonSerializer.Deserialize<PatchAttachCheck.Result>(File.ReadAllText(options["patched"]), json)!;
    findings = GameComparison.Run(
        patched.PatchedMethodTokens,
        options["game"],
        options["previous-game"],
        options["game-data"]);
}
else
{
    throw new ArgumentException($"Unknown step '{step}'.");
}

File.WriteAllText(options["output"], JsonSerializer.Serialize(findings, findings.GetType(), json));
