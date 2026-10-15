# Creating a release

The source manifests are authoritative. Review the source, versions and changelog
before building a release. Building never uploads to GitHub or Steam and never
installs the candidate into the game's mods directory. Publishing creates a GitHub
**draft**; it does not run the Steam uploader or publish that draft publicly.

## Versions and compatibility

- Client: `client/StS2AP/Archipelago.json` and `<Version>` in `StS2AP.csproj` must match.
- APWorld: `world/spire2/archipelago.json` owns `world_version`. Archipelago loads it
  into the world metadata. `fill_slot_data()` emits that version using the existing
  `mod_compat_version` wire key; there is no second version literal to maintain.
- `mod_compat_version` is still required by the client: it parses player settings,
  records the server APWorld version, and compares it against its embedded APWorld
  manifest. Do not remove the wire key. Malformed/missing versions reject connection.
- `CompatFlag` is a separate protocol diagnostic. A different APWorld major/minor
  or flag produces an unverified-combination warning and user confirmation on a
  fresh connection. It is **not** an unconditional incompatibility rejection.
  Patch-only version differences are silent. Change the flag intentionally when
  changing the slot-data contract; it is not the mod's version number.

The client embeds the APWorld manifest at compile time. Rebuild the client after
changing that manifest. Keep the APWorld version unchanged for a client-only
release; bump it for APWorld logic/options/format changes. Historical APWorld-only
releases can reuse an explicitly identified client, but the manual builder below
always recompiles a full bundle.

Choose versions for the **destination repository and release channel**. A fork's
beta tags are not the upstream public release sequence. Do not copy a beta version
number into a public release merely because it is numerically higher. The release
CLI checks selected-source ancestry and tags belonging to the destination repo;
fetch the intended remote's tags before a public release. When local tag names
conflict with another remote's tags, use a clean checkout for that release.

For ordinary increments, `python scripts/prepare_release.py --client-bump minor
--world-bump patch` updates the two client files and APWorld manifest. It never
rewrites world.py. This is a source edit to review/commit, not a publish command.
For an intentional channel/version reset, edit the three authoritative fields to
the chosen values explicitly instead of applying an inappropriate numeric bump.

## Local prerequisites

- .NET 10 SDK, Python 3.11–3.13 and Archipelago 0.6.8 dependencies.
- Archipelago checkout at `../Archipelago`, or `--archipelago-root PATH`, with
  `worlds/spire2` linked to this checkout's `world/spire2`. The builder refuses a
  different world source; it no longer deletes/replaces another checkout's folder.
  Directory symlinks on Windows can require Developer Mode or elevated privileges.
- Godot .NET editor and `GodotExePath` in ignored `client/StS2AP/local.props`.
- NuGet access/cache for both game reference assemblies and other dependencies.
  No live game install is required for compilation with reference assemblies.

On the configured Linux machine, source `.local-tools/env.sh` from the project
root and use `.venv/bin/python`. Windows can use `py -3.13` or scripts/release.ps1.
The local Workshop setup is documented in the ignored
`.local-tools/release-prep/README.md`; it is not part of a clone of this repository.

## Validate and build

```sh
python scripts/release.py validate
python scripts/release.py build
```

Both commands require clean source by default. `--allow-dirty` permits a local
rehearsal only; its artifacts are marked dirty and cannot be passed to `publish`.
The optional `--sts2-api-signature-root PATH` supplies versioned local game DLLs
for the outer build; nested package variant builds use the pinned NuGet references.
Use `--expected-mod-version` and `--expected-apworld-version` to assert your intent.

The builder uses the shared headless APWorld script, calls `BuildMode=Package` with
an explicit temporary staging directory, assembles the loader and both game API
variants, and outputs:

```text
dist/Archipelago.zip
dist/spire2.apworld
dist/Spire2-template.yaml
dist/release-build.json
```

The client ZIP has install files directly at its root. Root `Archipelago.dll` is
the loader. Keep `lib/<game-version>/Archipelago.dll`, compatibility markers,
`archipelago-variants.manifest`, dependencies, PCK, `data/` and APWorld together.
The builder checks the root loader, variant hashes, required dependencies,
excluded game/debug/RitsuLib libraries, matching bundled/standalone APWorld bytes,
source commit and asset hashes. It never reuses a dirty installed mod directory.

Supported game versions come from `client/StS2AP/SupportedGameVersions.props`.
For compilation only, use explicit `-p:BuildMode=CompileOnly`; the old DllOnlyBuild
alias can be overridden by a developer's existing Local mode and is unsuitable
for release instructions.

## Tests and review

```sh
python -m unittest discover -s scripts/tests -p 'test_*.py'
dotnet test client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj -c Release
```

Extract the exact candidate ZIP into a fresh staging folder, then run regressions
with `STS2AP_TEST_BUNDLE` pointing to that folder and `STS2AP_TEST_ASSEMBLY` pointing
to its `lib/<public-version>/Archipelago.dll`. Run Category=Manifest again for beta.
Run the APWorld tests using Archipelago's test framework and generate a seed from
the actual packaged APWorld and release YAML. Python changes should be checked
on 3.11, 3.12 and 3.13. Edit the example YAML's slot name/options before playing.

Compilation/tests do not replace game checks: test loader selection on both game
branches, AP connection, rewards/checkpoints and multiplayer as appropriate.
Keep RitsuLib installed separately and avoid simultaneous manual/Workshop copies.

## Create a GitHub draft only when releasing

After reviewing/committing and pushing the selected source, rebuild from that
clean commit. Check out the branch intended for the chosen repository. Example:

```sh
python scripts/release.py publish --remote upstream --branch main \
  --notes-file /absolute/path/release-notes.md \
  --expected-mod-version CHOSEN_CLIENT_VERSION \
  --expected-apworld-version CHOSEN_APWORLD_VERSION
```

For a fork beta, use `--remote origin --branch v2 --prerelease`. The destination is
inferred from that remote; an optional --repo must match it. The CLI verifies the
branch equals the destination's pushed SHA through authenticated gh, validates
build hashes/versions, rejects a reused remote tag, and creates a draft targeted
at that exact SHA. It never pushes a source branch. Inspect any uncertain result
on GitHub before retrying. No publish command is needed to prepare/test artifacts.

Provide reviewed Markdown notes, or omit --notes-file to create a draft with the
version-expanded [release notes template](../scripts/release-notes-template.md).
Replace its changelist/known-issues placeholders before publishing. Keep setup
instructions in [the player setup guide](../world/spire2/docs/setup_en.md) and
link there from every release; release notes should focus on that update. The
player requirement is Archipelago Launcher v0.6.7+ for generation, independent
of the 0.6.8 environment used to test/build releases here.
Manual assets use `spire2.apworld`; CI uses a versioned filename. Both include
the YAML template, and the setup guide covers both APWorld naming conventions.

The release workflow remains limited to labelled merges into **upstream main**.
Its APWorld build targets 0.6.8; its version bump edits only the authoritative
manifests/project. Local tests do not prove GitHub Actions publishing end-to-end;
inspect a real draft workflow run before relying on it unattended.

## Steam Workshop

Treat Workshop upload as a separate explicit release action after candidate and
changelog review. The local uploader fork accepts per-update `changelog.md` and
has a separate offline formatter for previews. Neither the release CLI nor local
build/test commands execute it. The current user's item ID, preserved thumbnail,
workspace and exact live-upload command are recorded in the locally excluded
runbook. Do not use the live upload command to test or preview anything.
