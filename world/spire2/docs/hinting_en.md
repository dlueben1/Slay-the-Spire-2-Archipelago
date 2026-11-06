# Hinting, Items and Locations

This guide explains what Slay the Spire II's item and location names mean, how to
earn each check, and how to hint for the things you're stuck on.

## Reading the names

Almost every item and location starts with a character's name, and only counts for
that character:

- **Items:** `Ironclad Relic` is a relic for Ironclad's runs. It does nothing for Silent.
- **Locations:** `Silent Combat Gold 20` is a check you can only earn while playing Silent.

Modded characters use their predefined name, such as `Watcher Relic`. Modded
characters without one use `Custom Character 1`, `Custom Character 2` and so on.
Buffs, traps, Bonus Items and Sight items have no character name and work for every
character.

**Numbered locations count within a single run.** `Combat Gold 20` means the gold
reward from your 20th combat *in one run*, not your 20th combat overall. Every new
run starts counting from 1 again, and checks you've already sent stay sent.

## How to hint

Use the Archipelago **Text Client** from the Launcher. Connect with your slot name,
then type:

- `!hint <item>` to find where one of your items is, for example
  `!hint Ironclad Progressive Rest`.
- `!hint_location <location>` to see what's at one of your checks, for example
  `!hint_location Ironclad Combat Gold 20`.
- `!hint` on its own to list your hints and how many points you have.

If you're in the game with the dev console, `ap !hint Ironclad Relic` sends the same
command through the mod.

Things to know:

- **Type the full name, including the character.** `!hint Relic` won't find
  `Ironclad Relic`. If the name is close but not exact, the server suggests what you
  might have meant.
- **One new location per hint.** Most items have several copies, such as 10 Relics or
  3 Progressive Rests. Each `!hint` shows one copy, usually the earliest one you can
  reach. Run it again to find another copy, which costs another hint.
- **Item groups** also work with `!hint`, for example `!hint Campfire Upgrades`. A group
  covers every character's items, so it's usually better to hint one character's item
  by name. Groups: Gold, Campfire Upgrades, Shop Slots, Character Unlocks, Potions,
  Card Rewards, Rare Card Rewards, Relics, Ancients, Ascension Downs, Starter Upgrades,
  Buffs, Traps, Bonus Items.
- The mod hints some checks for you. Entering a shop hints the items on its
  Archipelago page, and entering a campfire hints its campfire checks, at no cost.

## Locations

What you do in the game to send each check. Which ones exist depends on your YAML.

| Location | How you earn it | Exists when |
|---|---|---|
| `Card Reward 1`–`20` | Your Nth card reward from a non-boss combat in the run. With **Shuffle All Cards** off, only every other card reward is a check, so there are 10: `Card Reward N` is your (2N−1)th card reward. | Always |
| `Rare Card Reward 1`, `2` | The rare card reward from the Act 1 and Act 2 bosses. | Always |
| `Relic 1`–`10` | Your Nth relic from an elite or treasure chest in the run. Opening a chest sends its check straight away. Empty chests don't count. | Always |
| `Ancient Act 1`, `2`, `3` | Reaching the Ancient at the start of that act. Act 1 is Neow. | Act 1 with **Neow Sanity**; Acts 2 and 3 always |
| `Combat Gold 1`–`20` | The gold reward from your Nth non-boss combat of the run, hallway fights and elites alike. Gold an enemy stole and you got back doesn't count. | **Gold Sanity** |
| `Boss Gold 1`, `2` | The gold from the Act 1 and Act 2 bosses. | **Gold Sanity** |
| `Potion Drop 1`–`9` | Your Nth potion dropped as a combat reward in the run. | **Potion Sanity** |
| `Act 1 Campfire 1`, `2` (and Acts 2, 3) | An extra option at a campfire. Picking it uses that campfire visit, like resting or smithing. Every campfire offers the unchecked ones for its act and earlier acts, so you can still do Act 1's in Act 3. | **Campfire Sanity** |
| `Shop Slot 1`–`16` | Buying from the Archipelago page of a shop. Act 1 shops offer up to Shop Slot 5, Act 2 up to 10, Act 3 up to 16. How many slots exist depends on your shop slot options. | **Shop Sanity** |
| `Reached Floor 1`–`49` | Reaching that floor. Floor 17 is the Act 1 boss, 33 is the Act 2 boss, and 48 is the Act 3 boss (49 is the second boss with Double Boss). Reaching a floor also sends every floor below it. | **Include Floor Checks** |
| `Press Start` | Starting your first run with that character. | Characters that start locked |

### Going for a late check

Late numbers such as `Combat Gold 20`, `Card Reward 20` or `Relic 10` need a long run
with lots of fights:

- **Combat Gold and Card Rewards:** pick map paths with many combat rooms and elites.
  `?` rooms sometimes turn into fights. Bosses don't count towards these.
- **Relics:** fight elites and open treasure chests. Black Star's extra elite relic counts
  as its own check.
- **Potion Drops:** these come from combat rewards, so more fights mean more chances.

If your YAML has **Release On Victory** (on by default), winning with a character sends
all of that character's remaining checks. Most players get the last few late checks
that way rather than hunting them.

## Items

What each item does once you receive it. Unless noted, an item stays yours for every
future run of that character.

### Run rewards

You claim these from the Archipelago reward menu during each run.

| Item | What it does |
|---|---|
| `Card Reward` | A card reward screen. |
| `Rare Card Reward` | A rare card reward screen, like a boss reward. |
| `Relic` | A relic. Your first few (set by **Relic Rewards Available Anytime**) can be claimed whenever you like. The rest are claimed by beating elites or opening treasure chests. |
| `Progressive Ancient` | Unlocks the next Ancient's relic choice: Act 2, then Act 3. With **Neow Sanity**, Neow's comes first. Depending on your options, the choice appears in the reward menu or at that act's Ancient. |
| `Potion` | A potion. |
| `One Gold`, `Five Gold` | 1 or 5 gold. Filler. |
| `Combat Gold` | 15 gold. |
| `Elite Gold` | 40 gold. |
| `Boss Gold` | 100 gold. |

### Unlocks

| Item | What it does |
|---|---|
| `Unlock` | Lets you play that character, for example `Defect Unlock`. |
| `Progressive Rest` | Lets you rest at campfires. Each copy unlocks one more act: you need 1 for Act 1, 2 for Act 2 and 3 for Act 3. |
| `Progressive Smith` | Lets you smith (upgrade a card) at campfires, unlocked by act like Progressive Rest. |
| `Shop Card Slot`, `Neutral Shop Card Slot`, `Shop Relic Slot`, `Shop Potion Slot` | Brings back one slot of that kind on the normal shop page. |
| `Progressive Shop Remove` | Lets you remove cards at shops, one act per copy like Progressive Rest. |
| `Progressive Starter Card` | The first copy gives back your special starter card (Bash, Neutralize, ...); the second upgrades it the way Archaic Tooth would. |
| `Progressive Starter Relic` | The first copy gives back your starter relic (Burning Blood, ...); the second refines it the way Touch of Orobas would. |
| `Disable <Ascension>` | Turns off that Ascension for this character, for example `Ironclad Disable Tough Enemies`. |

### Items for every character

These have no character name.

| Item | What it does |
|---|---|
| Buffs (`Strength`, `Vigor`, `Free Attack`, `Post-Combat Card Upgrade`, ...) | One-time help. Buffs wait in a queue and one applies per combat, to whichever character you're playing. |
| Traps (`Weak Trap`, `Vakuu Trap`, `Dazed Trap`, ...) | One-time trouble for a combat, such as Weak for a turn or Dazed in your draw pile. |
| `Bonus Wax Relic` | A Bonus Item: a wax relic you can claim from the reward menu in every run, on any character. |
| `Intent Sight`, `Health Sight`, `Pile Sight`, `Map Sight` | Only with the matching Hidden challenge option. Any one copy shows intents, health, your piles or the full map again for every character. |

## What to hint when you're stuck

Hint whatever is stopping you, for the character you're playing:

- **A character you can't play yet:** `<Character> Unlock`.
- **Can't rest or smith in an act:** `Progressive Rest` or `Progressive Smith`.
- **Runs are too weak:** `Relic`, `Card Reward` or `Rare Card Reward`. With Ancients
  locked, `Progressive Ancient` helps a lot.
- **Shop is bare:** the shop slot items and, with Gold Sanity, `Elite Gold` or `Boss Gold`
  to afford what's there.
- **Missing your starter card or relic:** `Progressive Starter Card` or `Progressive Starter Relic`.

To decide whether a check is worth chasing, `!hint_location` it first. If it holds
filler, you can let Release On Victory send it later.
