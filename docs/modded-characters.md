# Finding a modded character ID

Predefined characters accept their display names as YAML aliases: **Tectonic, Captain, Acolyte, Slime Boss, Guardian, Hexaghost, Champ, Automaton, Snecko, Hermit, Awakened One, and Watcher**. Names are case-insensitive; full internal IDs still work. Use these aliases in `modded_characters`, as `advanced_characters` keys, or in `unlocked_character`.

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
3. Select **Archipelago** to view the installed modded character IDs.
4. Copy the exact ID into the matching wizard row.

> [!IMPORTANT]
> Character IDs are matched case-insensitively. Each row must use a unique ID, and the matching character mod must be installed when you play.

> [!CAUTION]
> While we strive to make modded character support as smooth as possible, some mods may be incompatible with the Slay the Spire II Archipelago mod - especially if the mod adds more than just a Character (like total conversion mods, characters with very advanced mechanics, etc.).
>
> Please use them at your own risk, and if you're playing with friends, run a test game with your modded characters before you get started to ensure the mod doesn't crash!

Up to 20 modded characters can be selected for a generated roster. You can enter more candidates when using random character selection. Known characters use their display names in Archipelago items and hints. Other selected characters become Custom Character 1, 2, and so on in their YAML order; named characters do not consume these numbers.
