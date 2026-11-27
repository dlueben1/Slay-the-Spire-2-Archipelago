# Slay the Spire II Setup

This guide takes you from installing the mod to connecting to a game. If someone
else is generating and hosting, you only need steps 1, 2 and 4.

We recommend the game's **beta branch**, where Terairk mainly develops and tests
the mod. Beta updates can temporarily break the mod until it is updated.

To switch, right-click Slay the Spire 2 in your Steam library, select
**Properties** > **Game Versions & Betas**, and select **public-beta** instead of
**Default Public Version**. Steam then downloads the beta version. To switch back,
select **Default Public Version** again.

Main-branch support is on thin ice. The mod still runs there, but it is largely
untested and new features are not supported on it: The Fool, Dread Mode and Blitz
Merchants need the beta branch. Multiplayer on the main branch will stop being
supported in a future release, because beta 0.113.0 changed the game's multiplayer
code substantially.

## 1. Install the mod

1. Subscribe to [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
   (version 0.6.16 or newer) and [Slay the Spire II Archipelago](https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296)
   on Steam Workshop. The [Slay the Spire 2 Archipelago Minimal Install](https://steamcommunity.com/sharedfiles/filedetails/?id=3839147144)
   collection has both, plus Mod Launch Manager (below): select **Subscribe to all**
   to get all three at once.
2. Wait for Steam to finish downloading the mods, then start the game.

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

Install only one copy of each mod. If you were using the Workshop item for the
Archipelago mod, unsubscribe from it first. RitsuLib can come from Workshop or be
installed by hand as below.

1. Download the two ZIPs:
   - `Archipelago.zip` from the [Archipelago mod releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases),
     for the release you want to play.
   - `STS2-RitsuLib.<version>.variant-pack.zip` from the [RitsuLib releases](https://github.com/BAKAOLC/STS2-RitsuLib/releases)
     (version 0.6.16 or newer; the latest is best). Take the **variant-pack**: it
     works on both the main and beta branches. The other ZIPs on that page are each
     for a single game version, and the `.nupkg` files are for mod developers.
2. In Steam, right-click Slay the Spire 2 and select **Manage** > **Browse local
   files**. This opens the game folder, which holds `SlayTheSpire2.exe` (or
   `SlayTheSpire2` on Linux).
3. Create a folder named `mods` there, if it isn't there yet.
4. Copy both ZIPs into `mods`.
5. Extract each ZIP into its own folder inside `mods`:
   - **Windows:** right-click the ZIP, select **Extract All...**, and keep the
     suggested folder. With 7-Zip or WinRAR, use the **Extract to** option named
     after the ZIP (such as `Archipelago\`), not **Extract Here**, which spills the
     files loose into `mods`.
   - **Linux:** your file manager's **Extract Here** creates the folder.

   You can delete the ZIPs afterwards or leave them; the game ignores them.
6. Start the game.

Afterwards, `mods` should look like this (the folder names don't matter, as long
as each mod has its own folder):

```
Slay the Spire 2/
├── SlayTheSpire2.exe
└── mods/
    ├── Archipelago.zip                              (optional to keep)
    ├── Archipelago/
    │   ├── Archipelago.dll
    │   ├── Archipelago.json
    │   ├── Archipelago.pck
    │   ├── ...
    │   ├── data/
    │   └── lib/
    ├── STS2-RitsuLib.0.6.16.variant-pack.zip        (optional to keep)
    └── STS2-RitsuLib.0.6.16.variant-pack/
        ├── STS2-RitsuLib.dll
        ├── mod_manifest.json
        ├── ...
        ├── compat/
        └── shared/
```

If the browser renamed a download, for example to `Archipelago (1).zip`, the folder
gets that name too, which is fine.

**Updating:** delete the mod's old folder first, then extract the new ZIP the same
way. Two copies of the same mod in `mods` conflict with each other.
