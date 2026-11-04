# Troubleshooting

Start with the quick checks, then find what you're seeing below. If nothing helps,
see [Getting help](#getting-help).

## Quick checks

- **Only have one copy of the mod installed:** use either the Workshop item or `mods/Archipelago`,
  not both. Check this if the mod doesn't load i.e. no Archipelago logo on main screen
- **RitsuLib is installed:** you need version 0.6.0 or newer.
- **Get the latest versions of all mods** if your mods look out of date, use RitsuLib in the mod's 
  settings to install the latest version of all mods.
- **Your game version is supported:** your mod release's notes list the supported
  main and beta branch versions.

## The mod doesn't load

**Connect to Archipelago isn't on the main menu:**

- If the game asks whether to load mods, accept and restart the game.
- Open the mods screen in the game's Settings and check that both the Archipelago
  mod and RitsuLib are enabled.
- If you installed manually, check that `Archipelago.dll` is directly inside
  `mods/Archipelago`, without an extra nested `Archipelago` folder.
- Search your [game log](#finding-your-game-log) for `Archipelago.Loader`. It
  reports a missing RitsuLib or an unsupported game version.

**The game just updated and the mod stopped working:** game updates can break the
mod until a new mod release comes out, especially on the beta branch. Check for a
new release, or switch game branch in Steam under the game's **Properties** >
**Betas**.

## My saves are gone

They aren't. The game keeps separate saves for modded and unmodded play. The first
time you play modded, it copies your unmodded saves. After that, the two sets don't
share progress.

To get back to your unmodded saves, start the game without mods. Disabling all the
mods in Steam's Workshop tab isn't enough, so do one of these instead:

- Disable the mods on the mods screen in the game's Settings, then restart the game.
- Add `--nomods` to the game's launch options in Steam, under **Properties** >
  **General**. You can also make a separate entry for the game in Steam with this
  option, so you can choose modded or unmodded from your library.

## Generation fails

- **`No world found to handle game Slay the Spire II`:** install the release's
  APWorld in the Launcher you are actually using, check that it is v0.6.7 or newer,
  and try again.
- **`Configured ... Bonus Items, but only ... filler slots are available`:** remove
  some Bonus Items or add more checks, for example by turning on Include Floor
  Checks.
- **`StS 2 only supports 20 characters without a predefined name`:** select fewer
  modded characters, or use `pick_num_characters` to pick a smaller set from them.
- **The Archipelago website won't generate from your YAML:** generate in your
  Launcher, then upload the resulting ZIP.

## You can't connect

**`Failed to connect. Please check your details and try again.`**

- Check the server address and port, for example `archipelago.gg:38281`.
- Check that the slot name exactly matches `name` in your YAML.
- Check the password, if the room has one.
- Rooms on archipelago.gg pause after a period of inactivity. Refresh the room's
  page to wake it, and check the port shown there in case it changed.

**`APWorld Compatibility Warning`:** the game was generated with a different APWorld
from the one your mod release includes. Installing a different APWorld won't change
a game that has already been generated. Update the mod if it is out of date, or
choose to connect anyway and watch for problems (usually there isn't any problems) 

**A warning about a missing character mod:** see
[If a character mod is missing or broken](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/modded_characters_en.md#if-a-character-mod-is-missing-or-broken).

## During a game

- **You got disconnected:** keep playing. The mod reconnects automatically, and your
  checks are queued and sent once it does.
- **A checkpoint won't load:** checkpoints saved with a different set of mods may
  not load. Start a new run; your other checkpoints are kept.
- **The game crashes with a modded character:** that mod may not be compatible.
  Try a test game without the other mods, or leave that character out.
- **You don't know where to go next:** Universal Tracker shows what's in logic. See
  [Connect and play](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/setup_en.md#4-connect-and-play).

## Multiplayer

- **Mod mismatch or version mismatch when joining:** compare game branches and
  installed mods, including mod versions. See
  [Everyone needs the same mods](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/multiplayer_setup_en.md#everyone-needs-the-same-mods).
- **Error 1106 when joining a lobby:** make sure all players are on the same branch
  of the game and the same game version.
- **Host is blocked:** connect to an AP slot before selecting **Host**.
- **Joined with a slot instead of as a guest:** return to the main menu, disconnect
  from Archipelago and join again.
- **`Could not prepare AP rewards`:** open the menu again.
- **State divergence popup in Slay the Spire 2:** first make sure everyone has the
  exact same mods. If it keeps happening, use **Export Bug Report** under Archipelago
  Settings and include the report when asking for help.

## Getting help

Open an [issue on GitHub](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/issues)
and include:

- What happened, and what you did just before it.
- Your game branch and version, and your mod and APWorld versions.
- Any character mods you use.
- Your game log.

### Finding your game log

- **Windows:** `%APPDATA%\SlayTheSpire2\logs\godot.log`
- **Linux:** `~/.local/share/SlayTheSpire2/logs/godot.log`

`godot.log` is the newest log. Older ones in the same folder have a date in their
name, so send the log from the session that had the problem.
