# Finding a modded character ID

Predefined characters accept their display names, such as Watcher, Guardian or Tectonic, as YAML aliases. [Predefined modded characters](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/predefined_characters_en.md) lists each one with its internal ID, its mod and the mod's Steam Workshop page. Names are case-insensitive; full internal IDs still work. Use these aliases in `modded_characters`, as `advanced_characters` keys, or in `unlocked_character`.

> [!NOTE]
> Alchemist, Runesmith, Witch, Tailor and Engineer use the IDs from those mods' beta-branch Workshop editions. If you play the main-branch edition and connecting reports that the character is not loaded, use the internal ID shown in-game instead of the name.

```yaml
modded_characters:
  - Watcher
  - Guardian
  - Tectonic
```

Basic mode keeps the first occurrence when an alias and its full ID both appear. Advanced mode rejects duplicate identities so conflicting per-character settings are not silently discarded.

Other characters need their **internal ID**, which is **very likely to be different** from the name shown on their Steam Workshop page or in-game.

1. Start Slay the Spire II with the character mod and the Archipelago mod installed.
2. Open **Archipelago Settings** in the game.
3. Select **Archipelago**. Under **Installed Characters**, press **Copy**. This copies the ID of every installed modded character as a ready-made `modded_characters` list.
4. Open your finished YAML in a text editor and replace its `modded_characters` entry with what you copied. Delete any characters you don't want to play.

You can also type IDs into the modded character rows on the website.

> [!IMPORTANT]
> Character IDs are matched case-insensitively. Each row must use a unique ID, and the matching character mod must be installed when you play.

> [!CAUTION]
> While we strive to make modded character support as smooth as possible, some mods may be incompatible with the Slay the Spire II Archipelago mod - especially if the mod adds more than just a Character (like total conversion mods, characters with very advanced mechanics, etc.).
>
> Please use them at your own risk, and if you're playing with friends, run a test game with your modded characters before you get started to ensure the mod doesn't crash!

A generated roster can include every predefined character, but at most 20 characters without a predefined name. You can enter more candidates when using random character selection. Known characters use their display names in Archipelago items and hints. Other selected characters become Custom Character 1, 2, and so on in their YAML order; named characters do not consume these numbers.

## If a character mod is missing or broken

If a slot's character mod is missing or broken, you can still connect after a warning. Every character's checks and victories then also count for each missing character. **Defect** is always playable in that case: it uses its own items if your YAML includes Defect, and otherwise the first missing character's items. Checkpoints saved with a different set of mods may not load; start a new run if one fails. As a last resort, the dev console command `ap_release_missing <character> confirm` sends all of a missing character's remaining checks; it is disabled in race rooms.

It has come to my attention that if you already sent out all campfire/shop checks for all other characters then you might not be able to use them as backup to send out those checks for the missing modded character as they disappear. Due to multiplayer state divergences and the current multiplayer code, this is overall a very difficult problem to solve and the current stance is to use the dev console command `ap_release_missing <character> confirm`. Multiplayer is already built on sticks and have no plans to overhaul the complex architecture (which currently works) in order to fix this unlikely scenario in which there exists a backup command.

### A game update broke the mod

A game update on the main branch or the beta branch can break a character mod until its author updates it, even when the Archipelago mod itself already supports the new version. Try these in order:

1. **Update the character mod.** Check its Steam Workshop page for an update, or for a separate edition for your game branch.
2. **Try CVC.** [CVC — Cross-Version Compatibility Patch](https://steamcommunity.com/sharedfiles/filedetails/?id=3783150604) by Snowline lets mods made for another game version keep working on yours. See [Using CVC](#using-cvc).
3. **Play without that character.** Disable the broken mod on the mods screen in the game's Settings, restart the game and connect anyway, as described above.
4. **Release that character's checks.** See [Releasing a single character](#releasing-a-single-character).

### Using CVC

1. Subscribe to [CVC](https://steamcommunity.com/sharedfiles/filedetails/?id=3783150604) on Steam Workshop and enable it.
2. Make sure CVC loads before the mods it fixes. [Mod Launch Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=3788556097), also by Snowline, makes load orders easier: enable every mod in Steam's Workshop tab and let Mod Launch Manager take over from there.
3. In the game, open **Settings** > **General** > **Choose which mods get compatibility fixes** and select the character mod that broke.
4. Fully restart the game. Do this again whenever you change that selection, change the load order or update CVC.

CVC shows a compatibility score for each mod and can export a report from the bottom of the same settings page. It works on a best-effort basis and cannot fix every mod, so run a test game with the character before relying on it in a multiworld.

### Releasing a single character

The dev console command `ap_release_missing` sends all of a missing character's remaining checks, without releasing the rest of your slot. It only works for a character whose mod is not loaded, and it is disabled in race rooms. Sent checks cannot be taken back.

1. Connect to your slot with the character's mod disabled or uninstalled.
2. Press the backtick key (`` ` ``, usually under Esc) to open the dev console.
3. Run `ap_release_missing` on its own to list the missing characters and how many checks each has left.
4. Run `ap_release_missing <character>` to see how many checks would be sent, then `ap_release_missing <character> confirm` to send them.

