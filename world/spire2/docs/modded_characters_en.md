# Finding a modded character ID

Predefined characters accept their display names, such as Watcher, Guardian or Tectonic, as YAML aliases. [Predefined modded characters](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/world/spire2/docs/predefined_characters_en.md) lists each one with its internal ID, its mod and the mod's Steam Workshop page. Names are case-insensitive; full internal IDs still work. Use these aliases in `modded_characters`, as `advanced_characters` keys, or in `unlocked_character`.

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

Up to 20 modded characters can be selected for a generated roster. You can enter more candidates when using random character selection. Known characters use their display names in Archipelago items and hints. Other selected characters become Custom Character 1, 2, and so on in their YAML order; named characters do not consume these numbers.

## If a character mod is missing or broken

If a slot's character mod is missing or broken, you can still connect after a warning. Every character's checks and victories then also count for each missing character. **Defect** is always playable in that case: it uses its own items if your YAML includes Defect, and otherwise the first missing character's items. Checkpoints saved with a different set of mods may not load; start a new run if one fails. As a last resort, the dev console command `ap_release_missing <character> confirm` sends all of a missing character's remaining checks; it is disabled in race rooms.
