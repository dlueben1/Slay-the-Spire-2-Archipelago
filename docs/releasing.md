# Creating a release

Version and compatibility changes must be reviewed and merged through a pull
request before a release is built. The release tool reads the committed source;
it never edits versions, creates a commit, or pushes `main`.

The client version is owned by `client/StS2AP/Archipelago.json`. The APWorld
version is owned by `world/spire2/archipelago.json` and must match
`SlayTheSpire2World.mod_compat_version` in `world/spire2/world.py`. Keep the
APWorld version unchanged for a client-only release. Increment it when the
APWorld artifact changes, including logic, options, or user-facing APWorld
descriptions. Increment `CompatFlag` only for an intentionally incompatible
slot-data contract.

The client embeds the tracked APWorld manifest so it can compare a server's
APWorld with the APWorld bundled in that client release. Matching compatibility
flags are the hard compatibility boundary. Patch-only APWorld differences are
silent; the client recommends an update only when the server is on an older
major/minor APWorld line than the bundled copy. Client and APWorld version
numbers are never compared with each other.

## DLL-only development without a game install

The project maps each supported `Sts2ApiCompat` to a pinned
`Book.StS2.RefLib` package containing MegaCrit-permissioned, stripped reference
assemblies. A single variant can therefore be compiled without a game install,
touching a game directory, or exporting a PCK:

```text
dotnet build client/StS2AP/StS2AP.csproj -c Release -p:Sts2ApiCompat=0.107.1 -p:UseSts2RefLib=true -p:DllOnlyBuild=true
dotnet build client/StS2AP/StS2AP.csproj -c Release -p:Sts2ApiCompat=0.111.0 -p:UseSts2RefLib=true -p:DllOnlyBuild=true
dotnet build client/StS2AP.Loader/StS2AP.Loader.csproj -c Release
```

NuGet restore is necessary for RefLib, Archipelago, and RitsuLib. The resulting
variant DLLs are under `client/StS2AP/bin/<version>/Release/net9.0/`. These
commands prove compilation only; use the release command with locally extracted
reference packs to assemble and validate the complete loader layout.

## Validate and build

On Windows, use the PowerShell wrapper:

```powershell
.\scripts\release.ps1 validate
.\scripts\release.ps1 build --sts2-api-signature-root C:\sts2-reference
```

The equivalent direct commands are:

```text
py -3.13 scripts/release.py validate
py -3.13 scripts/release.py build --sts2-api-signature-root C:\sts2-reference
```

Pass `--expected-mod-version` and `--expected-apworld-version` to make the
operator's intended versions explicit. The signature root must contain
`0.107.1/` and `0.111.0/`, each with `sts2.dll`, `0Harmony.dll`, and
`GodotSharp.dll`. These are compile-time reference packs; the full game does not
need to be installed on the build machine, although NuGet restore and Godot are
still required. `build` replaces
`../Archipelago/worlds/spire2`, invokes the Archipelago APWorld builder, builds
the C# client and Godot pack, and creates:

```text
dist/Archipelago.zip
dist/spire2.apworld
dist/release-build.json
```

`Archipelago.zip` has top-level install files plus exact variants at
`lib/0.107.1/Archipelago.dll` and `lib/0.111.0/Archipelago.dll`. GUI archive tools
should still create the required `Archipelago` installation directory from the
archive name. The root `Archipelago.dll` selects an exact variant when possible.
For a later patch on the same major/minor version line, it selects the newest
compiled target not newer than the running game and logs a warning. It refuses
unreadable versions, older patches, and new major/minor lines. Game versions are
never encoded into the mod version. The build bundles
`spire2.apworld` beside the loader so the in-game **Install APWorld**
button can launch it. The exact same APWorld is also kept as the standalone
GitHub asset. The build verifies that both copies are byte-identical, alongside
required entries, excluded game/debug libraries, APWorld contents, artifact
hashes, source commit, and both versions.

## Publish

After the version PR is merged, check out the exact clean `main` commit and build
the artifacts. A maintainer with tag and GitHub release permissions can then run:

```powershell
.\scripts\release.ps1 publish `
  --expected-mod-version 1.0.1 `
  --expected-apworld-version 1.0.0
```

Publishing refuses to run unless local `HEAD` is exactly `origin/main`, the mod
version is greater than the latest semantic-version tag reachable from `main`,
and changed APWorld sources have a greater APWorld version. It tags the existing
commit, pushes only that tag, and uploads only `Archipelago.zip` and
`spire2.apworld`. It never pushes a branch.
