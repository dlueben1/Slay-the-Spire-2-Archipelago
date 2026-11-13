# Slay the Spire II Setup

This guide takes you from installing the mod to connecting to a game. If someone
else is generating and hosting, you only need steps 1, 2 and 4.

We recommend the game's **beta branch**, where Terairk mainly develops and tests
the mod. Beta updates can temporarily break the mod until it is updated.

Main-branch support is on thin ice. The mod still runs there, but it is largely
untested and new features are not supported on it: The Fool, Dread Mode and Blitz
Merchants need the beta branch. Multiplayer on the main branch will stop being
supported in a future release, because beta 0.113.0 changed the game's multiplayer
code substantially.

## 1. Install the mod

1. Subscribe to [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
   (version 0.6.11 or newer) and [Slay the Spire II Archipelago](https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296)
   on Steam Workshop.
2. Wait for Steam to finish downloading both mods, then start the game.

Prefer not to use Workshop? See [Manual installation](#manual-installation).

We also recommend [Mod Launch Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=3788556097) by Snowline on Steam Workshop.
With it, you can enable all your mods in Steam's Workshop tab, see when mods update,
and keep mod profiles to switch between different sets of mods.

## 2. Make your YAML

A YAML is the file that holds your settings for the game. The easiest way to make
one is the YAML Builder at <https://sts2ap.terairk.dev/#/builder>. It walks through
every option and downloads a finished YAML, and it can import a YAML you already have
to edit it. The mod's **Mod Website & YAML Builder** main-menu button opens the same
builder.

**Modded characters (optional):** see [Predefined modded characters](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/predefined_characters_en.md)
for those with ready-made names and [Finding a modded character ID](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/modded_characters_en.md)
for any other.

Other ways to make a YAML:

- The builder on the main site, <https://sts2ap.net/#/builder>, works too, but it
  gets new options later.
- Edit `Spire2-template.yaml` from the mod release by hand, or use the Archipelago
  Launcher's **Options Creator**. Set `name` to the slot name you want.

YAMLs from either builder still work regardless of if they miss options or have extra options 
compared to options-creator and template: extra options are ignored and missing options
use their defaults.

## 3. Generate and host the game

If someone else is doing this, send them your YAML (and the link to the APworld releases) and 
skip to step 4.

1. Install [Archipelago Launcher](https://github.com/ArchipelagoMW/Archipelago/releases)
   v0.6.7 or newer.
2. Download the `.apworld` file from the same mod release.
3. Open the Launcher, select **Install APWorld**, and choose that file. On Windows,
   double-clicking the file also works if the Launcher installed its file association.
   The mod's main-menu **Install APWorld** action is another way to use the bundled file.
4. Use **Browse Files** in the Launcher, open `Players`, and put the players' YAML
   files there.
5. Select **Generate**. The resulting ZIP is written to `output` in the Archipelago
   folder.

To play online, upload the ZIP at [Archipelago](https://archipelago.gg/) and create
a room, or use your preferred Archipelago server. Keep the room's server address,
port and any password. You can also host locally to playtest before a sync.

## 4. Connect and play

1. Start Slay the Spire II with the Archipelago mod and RitsuLib enabled.
2. Select **Connect to Archipelago** on the main menu.
3. Enter the server address and port, your YAML's slot name and any password.
4. Connect, then follow the mod's character and run selection.

You don't need the Launcher to play, but we recommend it for two optional tools.
**Universal Tracker** shows what's in logic; search up the `universal-tracker`
channel on the main Archipelago Discord. The **Text Client** lets you see and send hints.

The [hinting guide](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/hinting_en.md)
explains what each item and location means, how to earn each check, and how to hint.

## Multiplayer

The mod supports the game's co-op. See the [multiplayer setup guide](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/multiplayer_setup_en.md)
for what everyone needs, hosting and joining.

## Troubleshooting

See the [troubleshooting guide](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/troubleshooting_en.md) for things to check and
fixes for common problems.

## Manual installation

Install only one copy of the Archipelago mod. If you were using the Workshop item,
unsubscribe from it first, and keep RitsuLib installed separately.

1. Download `Archipelago.zip` from the release you want to play.
2. In Steam, open Slay the Spire 2's **Browse Local Files** action.
3. Create `mods/Archipelago` inside that game directory and extract the ZIP's
   contents into it.
4. Confirm that `Archipelago.dll`, `Archipelago.json`, the PCK and the `lib/` and
   `data/` directories are directly inside `mods/Archipelago`, without an extra
   nested `Archipelago` folder.
5. Start the game.
