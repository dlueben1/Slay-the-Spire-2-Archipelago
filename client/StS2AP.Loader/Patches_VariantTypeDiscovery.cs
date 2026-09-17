using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StS2AP.Loader;

/// <summary>
/// Makes the selected implementation visible before ModEntry registers content with RitsuLib.
/// ModelDb.Init and the serialization caches must discover the same types during normal startup.
/// </summary>
internal static class Patches_VariantTypeDiscovery
{
    private const string ModId = "Archipelago";
    private static Assembly? _variantAssembly;
    private static Type[] _variantTypes = [];

    internal static void Register(Assembly assembly)
    {
        if (_variantAssembly == assembly)
            return;
        if (_variantAssembly is not null)
            throw new InvalidOperationException("Cannot register two Archipelago variants in one process.");

        // Do not accept partial type loads: a missing model would otherwise first surface
        // as a misleading pool lookup failure, or a different multiplayer serialization map.
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Log.Error($"[Archipelago.Loader] Variant type discovery failed: {string.Join(Environment.NewLine, ex.LoaderExceptions.OfType<Exception>())}");
            throw;
        }

        AssociateWithHost(assembly);
        _variantTypes = types;

        // Public 0.107.1 scans only Mod.assembly. Keep this small merge on beta too:
        // a previously cached ModTypes array may predate native assembly association.
        // Never clear or replace the host's cache, or scan other mods' dependencies.
        new Harmony("archipelago.loader.type-discovery").PatchAll(typeof(Patches_VariantTypeDiscovery).Assembly);
        _variantAssembly = assembly;
        Log.Info($"[Archipelago.Loader] Type-discovery bridge registered {types.Length} Archipelago variant types before content initialization.");
    }

    private static void AssociateWithHost(Assembly assembly)
    {
        MethodInfo? associate = typeof(ModManager).GetMethod(
            "AssociateAssemblyWithMod", BindingFlags.Public | BindingFlags.Static,
            binder: null, types: [typeof(string), typeof(Assembly)], modifiers: null
        );
        if (associate is null)
        {
            Log.Info("[Archipelago.Loader] Host has no assembly-association API; using the ModTypes bridge.");
            return;
        }

        // The loader is called before its Mod becomes Loaded. Use Mods, not GetLoadedMods.
        // Match the root directory as well as the ID so duplicate installations cannot
        // associate the variant with the wrong mod record.
        string directory = Path.GetDirectoryName(typeof(Patches_VariantTypeDiscovery).Assembly.Location)!;
        Mod mod = ModManager.Mods.Single(mod => mod.manifest?.id == ModId
            && string.Equals(Path.GetFullPath(mod.path), directory, StringComparison.OrdinalIgnoreCase));
        var assemblies = typeof(Mod).GetField("assemblies", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(mod) as IList<Assembly>
            ?? throw new InvalidOperationException("Host supports assembly association but its mod assembly list is unavailable.");
        if (assemblies.Contains(assembly))
            return;

        try
        {
            associate.Invoke(null, [ModId, assembly]);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Archipelago.Loader] Native assembly association failed; trying the initializer assembly-list fallback: {ex}");
        }

        // Some hosts reject association while a mod initializer is still running.
        // RitsuLib uses this same fallback. Do it now, before AssemblyInfo.Init builds
        // the beta multiplayer maps; a reflection-only bridge cannot supply ownership.
        if (!assemblies.Contains(assembly))
        {
            Log.Warn("[Archipelago.Loader] Native association did not record the variant; adding it to Archipelago's assembly list during initialization.");
            assemblies.Add(assembly);
        }
        Log.Info("[Archipelago.Loader] Variant associated with Archipelago for host type and multiplayer discovery.");
    }

    [HarmonyPatch(typeof(ReflectionHelper), nameof(ReflectionHelper.ModTypes), MethodType.Getter)]
    private static class ReflectionHelperModTypesPatch
    {
        private static void Postfix(ref Type[] __result)
        {
            if (_variantTypes.Length == 0)
                return;

            // Preserve host/other-mod order and add each selected type only once,
            // regardless of whether native discovery or another bridge already saw it.
            var seen = new HashSet<Type>(__result);
            Type[] missing = _variantTypes.Where(seen.Add).ToArray();
            if (missing.Length > 0)
                __result = __result.Concat(missing).ToArray();
        }
    }
}
