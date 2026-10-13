# Slay the Spire II Setup

This guide covers installing the mod, preparing a player YAML, generating a
multiworld and connecting to it. Check the release notes for the game versions
supported by the release you are installing.

We recommend the **beta branch**, where Terairk mainly develops and tests the mod.
The main branch remains supported, but receives little, if any, gameplay testing;
compatibility is assessed primarily through compile-time checks and player bug reports.
Beta branch updates can temporarily break compatibility until the mod is updated.

## Install the mod

You need Slay the Spire II and [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
(version 0.6.0 or newer). Install only one copy of the Archipelago mod: either
Steam Workshop or a manual installation.

### Steam Workshop

1. If you previously installed manually, move `mods/Archipelago` outside the
   game's `mods` directory or remove that copy.
2. Subscribe to [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
   and [Slay the Spire II Archipelago](https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296).
3. Wait for Steam to finish downloading both mods, then start the game.

### Manual installation

1. Unsubscribe from the Archipelago Workshop item if you were using it. Keep
   RitsuLib installed separately.
2. Download `Archipelago.zip` from the release you want to play.
3. In Steam, open Slay the Spire 2's **Browse Local Files** action.
4. Create `mods/Archipelago` inside that game directory and extract the ZIP's
   contents into it. This works with Windows and Linux archive tools.
5. Confirm that `Archipelago.dll`, `Archipelago.json`, the PCK and the `lib/` and
   `data/` directories are directly inside `mods/Archipelago`, without an extra
   nested `Archipelago` folder. Keep all packaged dependencies and both game
   variants together: the root DLL is the loader.
6. Start the game.

The loader selects the build for your installed game version. Later patches on a
supported major/minor line can use its newest earlier build, with a log warning.
An earlier patch or a new major/minor line needs a matching mod release. Consult
that release's notes rather than assuming a newer game update is supported.

## Prepare your player YAML

The easiest and recommended way to make your YAML is the
[YAML Builder](https://sts2ap.net/#/builder) on the Slay the Spire II Archipelago
website. It walks through every option and downloads a finished YAML. The mod's
**Mod Website & YAML Builder** main-menu button opens the same website.

Alternatively, download `Spire2-template.yaml` from the same release and edit it
by hand, or use the Archipelago Launcher's **Options Creator**. Set `name` to the
Archipelago slot name you want to use and review the options before generation.

Each participant supplies one YAML. For custom characters, everyone who needs
those characters must install the corresponding character mods; test their
compatibility before a full multiworld.

## Generate a multiworld

**You must generate using Archipelago Launcher v0.6.7 or newer.** This requirement
applies to the Launcher used for generation; the Launcher is not required merely
to play the StS2 mod and connect to an existing session.

1. Install [Archipelago Launcher](https://github.com/ArchipelagoMW/Archipelago/releases)
   v0.6.7 or newer.
2. Download the `.apworld` asset from the same mod release. Depending on the
   release, it is named `spire2.apworld` or `spire2-<version>.apworld`.
3. Open the Launcher, select **Install APWorld**, and choose that file. On Windows,
   double-clicking the file also works if the Launcher installed its file association.
   The mod's main-menu **Install APWorld** action is another way to use the bundled file.
4. Use **Browse Files** in the Launcher, open `Players`, and put the participants'
   YAML files there.
5. Select **Generate**. The resulting multiworld ZIP is written to `output`.

Install a newer APWorld before generating a new multiworld when updating it;
installing the file alone does not change a seed that was already generated.

To play online, upload the generated ZIP at [Archipelago](https://archipelago.gg/)
and create a room, or use your preferred Archipelago server. Keep the room's
server address, port and any password for connecting.

## Connect and play

1. Start Slay the Spire II with the Archipelago mod and RitsuLib enabled.
2. Select **Connect to Archipelago** on the main menu.
3. Enter the session's server address/port, your YAML's slot name and any password.
4. Connect, then follow the mod's character/run selection.

Avoid unrelated gameplay mods when troubleshooting. All players sharing an
in-game multiplayer lobby should use the same Archipelago mod version.

## Troubleshooting

- **The game loads the wrong mod copy:** check both Workshop subscriptions and
  `mods/Archipelago`; keep only the installation you intend to use.
- **The loader rejects the game version:** check the supported main branch and beta branch versions
  in your mod release's notes and select the corresponding game branch.
- **Generation does not recognize Slay the Spire II:** install the release's
  APWorld in the Launcher you are actually using, verify it is v0.6.7+, and retry.
- **Connection reports a version mismatch:** check that generation used the APWorld
  supplied with the mod release. A compatibility warning needs investigation;
  changing the installed APWorld will not rewrite an existing session.
