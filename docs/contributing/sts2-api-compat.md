# Selecting an STS2 API for development

`Sts2ApiCompat` selects the supported Slay the Spire 2 API that the C# client is compiled against. It controls the versioned game DLL directory, compatibility symbols, isolated `obj`/`bin` paths, and assembly build metadata. The supported main and beta versions are set in the tracked `client/StS2AP/SupportedGameVersions.props`; the client defaults to beta, and the loader targets main.

For local development, copy `client/StS2AP/local.props.template` to the gitignored `client/StS2AP/local.props`. Set `Sts2ApiCompat` to the version you want your IDE or editor to inspect. The simplest setup uses the MegaCrit-permissioned, stripped `Book.StS2.RefLib` reference assemblies from NuGet:

```xml
<Project>
  <PropertyGroup>
    <Sts2ApiCompat>$(Sts2BetaApiCompat)</Sts2ApiCompat>
    <UseSts2RefLib>true</UseSts2RefLib>
  </PropertyGroup>
</Project>
```

The main API selects the matching `Book.StS2.RefLib` version; beta selects the matching version with a `-beta` suffix. This works in tools that evaluate the C# project through MSBuild, including Rider, Visual Studio, and Visual Studio Code C# tooling. After changing `Sts2ApiCompat`, reload the project or solution (or restart the editor's C# language server) so autocomplete, navigation, and compile-time diagnostics reflect the newly selected API.

For an authoritative comparison with files from your own game installations, set `UseSts2RefLib` to `false` and point `Sts2ApiSignatureRoot` at a directory containing one subdirectory per supported API:

```xml
<Project>
  <PropertyGroup>
    <Sts2ApiCompat>$(Sts2BetaApiCompat)</Sts2ApiCompat>
    <UseSts2RefLib>false</UseSts2RefLib>
    <Sts2ApiSignatureRoot>C:\dev\sts2-reference</Sts2ApiSignatureRoot>
  </PropertyGroup>
</Project>
```

That resolves `sts2.dll`, `0Harmony.dll`, and `GodotSharp.dll` from the selected version's directory under `C:\dev\sts2-reference`.

When fixing a compatibility error, select the failing version in `local.props`, reload the project, and work against that API. An editor evaluates one selected project configuration at a time; it does not check every supported STS2 API simultaneously. Final compatibility verification therefore requires separate builds for every supported version:

```powershell
$versions = (dotnet msbuild client/StS2AP/StS2AP.csproj -getProperty:Sts2MainApiCompat,Sts2BetaApiCompat | ConvertFrom-Json).Properties
dotnet build client/StS2AP/StS2AP.csproj -p:Sts2ApiCompat=$($versions.Sts2MainApiCompat) -p:UseSts2RefLib=true -p:BuildMode=CompileOnly
dotnet build client/StS2AP/StS2AP.csproj -p:Sts2ApiCompat=$($versions.Sts2BetaApiCompat) -p:UseSts2RefLib=true -p:BuildMode=CompileOnly
```

Command-line `-p:` properties override `local.props`, so CI and release tooling can build all supported variants without changing a developer's local editor selection. The `Build STS2 API compatibility` GitHub workflow runs both RefLib commands independently for pull requests and pushes to `main` or `experimental-branch`; a change is compatible only when both jobs pass.

## Writing version-specific code

Each variant compiles against its own game API, so a difference between versions is a compile-time branch on a symbol named after a game version. `STS2_AT_LEAST_0_111_0` means the targeted API is 0.111.0 or newer. Because the symbols describe the API rather than a game branch, every branch stays correct when main or beta moves to a new version, with no source edits.

Put each difference in a method of `client/StS2AP/Utils/GameApiCompat.cs`. Branch in place only when the patch or override itself differs and can't go through a method, as in `Patches_LastingCandy` and `ApEnemyDamageScaling`. Name a new symbol after the first version you saw with the change, add its `DefineConstants` line to `client/StS2AP/StS2AP.csproj`, and put the newer code first:

```csharp
#if STS2_AT_LEAST_0_111_0
        return lobby.PlayerIds.ToArray();
#else
        return lobby.ConnectedPlayerIds.ToArray();
#endif
```

Name patch targets with `nameof`, so a renamed game method is a compile error instead of a failure at startup:

```csharp
[HarmonyPatch(typeof(NTreasureRoom), nameof(NTreasureRoom.OpenChest))]
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.Pool), MethodType.Getter)]
```

If that gives `CS0122` because the member is protected and virtual, add a `Publicize` item for it in `StS2AP.csproj`, unless the mod overrides that member.

## When a game updates

Start with `scripts/check_game_update.py`, described in [the scripts README](../../scripts/README.md#game-update-check). It lists the patches that no longer attach, the compile errors, and the patched game methods to read.

When a game branch updates, change its version in `client/StS2AP/SupportedGameVersions.props` and provide the matching `Book.StS2.RefLib` package or local game assemblies. The compatibility workflow, package assembly, loader default, and archive checks then use the new versions. Run both compatibility builds and the Domain and Regression tests; fix any API differences in the client before releasing. Check that the pinned RitsuLib compatibility package still supports the new game API. Update user-facing version examples separately. Changing a number cannot by itself make an incompatible game API work.

If you cannot resolve a version-specific compile error, do not guess at the API difference or remove the compatibility code just to make the build green. Run the failing command above, keep its complete compiler output, and hand the error off with the affected API version and source change. You can switch `local.props` back to a working version and continue unrelated development, but the client change is not ready to merge or release until every supported compatibility build passes.
