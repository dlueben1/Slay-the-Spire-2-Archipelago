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

## Validate and build

On Windows, use the PowerShell wrapper:

```powershell
.\scripts\release.ps1 validate
.\scripts\release.ps1 build
```

The equivalent direct commands are:

```text
py -3.13 scripts/release.py validate
py -3.13 scripts/release.py build
```

Pass `--expected-mod-version` and `--expected-apworld-version` to make the
operator's intended versions explicit. `build` replaces
`../Archipelago/worlds/spire2`, invokes the Archipelago APWorld builder, builds
the C# client and Godot pack, and creates:

```text
dist/Archipelago.zip
dist/spire2.apworld
dist/release-build.json
```

`Archipelago.zip` is deliberately flat. GUI archive tools should create the
required `Archipelago` installation directory from the archive name. The build
also verifies required entries, excluded game/debug libraries, APWorld contents,
artifact hashes, source commit, and both versions.

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
