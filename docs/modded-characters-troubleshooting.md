# Troubleshooting modded characters

## A character mod broke after a game update

A game update on the main branch or the beta branch can break a character mod until its author updates it, even when the Archipelago mod itself already supports the new version. Try these in order:

1. **Update the character mod.** Check its Steam Workshop page for an update, or for a separate edition for your game branch. Alchemist, Runesmith, Witch, Tailor and Engineer have separate main-branch and beta-branch Workshop items.
2. **Try CVC.** [CVC — Cross-Version Compatibility Patch](https://steamcommunity.com/sharedfiles/filedetails/?id=3783150604) by Snowline lets mods made for another game version keep working on yours. See **Using CVC** below.
3. **Play without that character.** Disable the broken mod and connect anyway. See **Playing with a missing character** below.
4. **Release that character's checks.** As a last resort, one console command sends everything the character still has to find. See **Releasing a single character** below.

## Using CVC

1. Subscribe to [CVC](https://steamcommunity.com/sharedfiles/filedetails/?id=3783150604) on Steam Workshop and enable it.
2. Make sure CVC loads before the mods it fixes. [Mod Launch Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=3788556097), also by Snowline, makes load orders easier: enable every mod in Steam's Workshop tab and let Mod Launch Manager take over from there.
3. In the game, open **Settings** > **General** > **Choose which mods get compatibility fixes** and select the character mod that broke.
4. Fully restart the game. Do this again whenever you change that selection, change the load order or update CVC.

CVC shows a compatibility score for each mod and can export a report from the bottom of the same settings page.

> [!NOTE]
> CVC works on a best-effort basis and cannot fix every mod. Run a test game with the character before relying on it in a multiworld.

## Playing with a missing character

If a slot's character mod is missing or broken, you can still connect after a warning. Every character's checks and victories then also count for each missing character, so you can finish the missing character's checks while playing the others.

**Defect** is always playable in that case: it uses its own items if your YAML includes Defect, and otherwise the first missing character's items.

If the mod is installed but broken, disable it on the mods screen in the game's Settings and restart the game, so that the character counts as missing.

> [!NOTE]
> If your other characters have already sent all their campfire and shop checks, those checks no longer appear in their runs, so they may not be able to send the missing character's campfire and shop checks. Release the missing character instead, as described below.

## Releasing a single character

The dev console command `ap_release_missing` sends all of a missing character's remaining checks, without releasing the rest of your slot.

1. Connect to your slot with the character's mod disabled or uninstalled.
2. Press the backtick key (`` ` ``, usually under Esc) to open the dev console.
3. Run `ap_release_missing` on its own to list the missing characters and how many checks each has left.
4. Run `ap_release_missing <character>` to see how many checks would be sent, then `ap_release_missing <character> confirm` to send them.

> [!IMPORTANT]
> This only works for a character whose mod is not loaded, and it is disabled in race rooms. Sent checks cannot be taken back.

## Connecting says a character is not loaded

- Check that the character's mod is subscribed and enabled on the mods screen in the game's Settings, then restart the game.
- Alchemist, Runesmith, Witch, Tailor and Engineer use the IDs from their beta-branch Workshop editions. Their main-branch editions can use different IDs, so the name in your YAML does not match. For your next game, use the internal ID shown in **Archipelago Settings** instead of the name. For a game that is already generated, connect anyway and play with the character missing.

## The game crashes with a modded character

That mod may not be compatible with the Archipelago mod. Try a test game with only RitsuLib, the Archipelago mod and that character's mod enabled. If it still crashes, leave the character out of your next YAML, or disable its mod and play with the character missing.

## A checkpoint won't load

Checkpoints saved with a different set of mods may not load. Start a new run; your other checkpoints are kept.

## Generation fails with too many characters

`StS 2 only supports 20 characters without a predefined name` means the YAML selects more than 20 characters that are not on the predefined list. Select fewer modded characters, or use `pick_num_characters` to pick a smaller set from them.

## Multiplayer shows a mod mismatch

Everyone in the lobby needs every character mod that anyone uses, at the same version, even players who will not pick that character. See the [multiplayer setup guide](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/multiplayer_setup_en.md#everyone-needs-the-same-mods).

## Still stuck?

The [troubleshooting guide on GitHub](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/troubleshooting_en.md) covers problems that are not about modded characters and explains how to ask for help.
