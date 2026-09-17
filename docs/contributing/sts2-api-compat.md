# Selecting an STS2 API for development

`Sts2ApiCompat` selects the supported Slay the Spire 2 API that the C# client is compiled against. It controls the versioned game DLL directory, compatibility symbols, isolated `obj`/`bin` paths, and assembly build metadata. The tracked default is the newest normally supported API.

For local development, copy `client/StS2AP/local.props.template` to the gitignored `client/StS2AP/local.props`. Set `Sts2ApiCompat` to the version you want your IDE or editor to inspect. The simplest setup uses the MegaCrit-permissioned, stripped `Book.StS2.RefLib` reference assemblies from NuGet:

```xml
<Project>
  <PropertyGroup>
    <Sts2ApiCompat>0.111.0</Sts2ApiCompat>
    <UseSts2RefLib>true</UseSts2RefLib>
  </PropertyGroup>
</Project>
```

`Sts2ApiCompat=0.107.1` selects `Book.StS2.RefLib` version `0.107.1`; `Sts2ApiCompat=0.111.0` selects package version `0.111.0-beta`. This works in tools that evaluate the C# project through MSBuild, including Rider, Visual Studio, and Visual Studio Code C# tooling. After changing `Sts2ApiCompat`, reload the project or solution (or restart the editor's C# language server) so autocomplete, navigation, and compile-time diagnostics reflect the newly selected API.

For an authoritative comparison with files from your own game installations, set `UseSts2RefLib` to `false` and point `Sts2ApiSignatureRoot` at a directory containing one subdirectory per supported API:

```xml
<Project>
  <PropertyGroup>
    <Sts2ApiCompat>0.111.0</Sts2ApiCompat>
    <UseSts2RefLib>false</UseSts2RefLib>
    <Sts2ApiSignatureRoot>C:\dev\sts2-reference</Sts2ApiSignatureRoot>
  </PropertyGroup>
</Project>
```

That resolves `sts2.dll`, `0Harmony.dll`, and `GodotSharp.dll` from `C:\dev\sts2-reference\0.111.0`.

When fixing a compatibility error, select the failing version in `local.props`, reload the project, and work against that API. An editor evaluates one selected project configuration at a time; it does not check every supported STS2 API simultaneously. Final compatibility verification therefore requires separate builds for every supported version:

```powershell
dotnet build client/StS2AP/StS2AP.csproj -p:Sts2ApiCompat=0.107.1 -p:UseSts2RefLib=true -p:DllOnlyBuild=true
dotnet build client/StS2AP/StS2AP.csproj -p:Sts2ApiCompat=0.111.0 -p:UseSts2RefLib=true -p:DllOnlyBuild=true
```

Command-line `-p:` properties override `local.props`, so CI and release tooling can build all supported variants without changing a developer's local editor selection. The `Build STS2 API compatibility` GitHub workflow runs both RefLib commands independently for pull requests and pushes to `main` or `experimental-branch`; a change is compatible only when both jobs pass.

If you cannot resolve a version-specific compile error, do not guess at the API difference or remove the compatibility code just to make the build green. Run the failing command above, keep its complete compiler output, and hand the error off with the affected API version and source change. You can switch `local.props` back to a working version and continue unrelated development, but the client change is not ready to merge or release until every supported compatibility build passes.
