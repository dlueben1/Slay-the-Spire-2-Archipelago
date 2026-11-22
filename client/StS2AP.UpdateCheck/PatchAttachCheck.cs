using System.Reflection;
using HarmonyLib;

namespace StS2AP.UpdateCheck;

/// <summary>
/// Applies every Harmony patch class in a client build, one class at a time. In the game PatchAll
/// stops at the first failure, so this is what lists all of them at once. Harmony also rejects stale
/// ___field and argument names here. No game code runs, and a patch that attaches can still be wrong.
/// </summary>
internal static class PatchAttachCheck
{
    internal sealed record Failure(string PatchClass, string Error);

    internal sealed record Result(
        int AppliedClasses,
        List<Failure> Failures,
        List<string> TypeLoadErrors,
        List<int> PatchedMethodTokens);

    internal static Result Run(string modDirectory)
    {
        Assembly mod = Assembly.LoadFrom(Path.Combine(modDirectory, "Archipelago.dll"));
        var harmony = new Harmony("update-check");
        var failures = new List<Failure>();
        var typeLoadErrors = new List<string>();
        int appliedClasses = 0;

        Type?[] types;
        try
        {
            types = mod.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // An old build on a new game: types whose game base classes changed can't load at all.
            types = ex.Types;
            typeLoadErrors.AddRange(ex.LoaderExceptions.OfType<Exception>().Select(error => error.Message).Distinct());
        }

        foreach (Type type in types.OfType<Type>())
        {
            try
            {
                if (HarmonyMethodExtensions.GetFromType(type).Count == 0)
                    continue;
                harmony.CreateClassProcessor(type).Patch();
                appliedClasses++;
            }
            catch (Exception ex)
            {
                failures.Add(new Failure(type.FullName ?? type.Name, Describe(ex)));
            }
        }

        List<int> gameMethods = harmony.GetPatchedMethods()
            .Where(method => method.Module.Assembly.GetName().Name == "sts2")
            .Select(method => method.MetadataToken)
            .ToList();
        return new Result(appliedClasses, failures, typeLoadErrors, gameMethods);
    }

    private static string Describe(Exception ex)
    {
        while (ex.InnerException != null)
            ex = ex.InnerException;
        return ex.Message.ReplaceLineEndings(" ");
    }
}
