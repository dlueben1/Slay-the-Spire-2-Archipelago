# Multiplayer Setup

Slay the Spire II Archipelago works with the game's built-in co-op. This guide
covers what changes when you play together. Install the mod and prepare your
YAML with the [setup guide](../world/spire2/docs/setup_en.md) first.

## Everyone needs the same mods

- **Same game version.** Everyone must be on the same game branch (all beta or
  all main) and the same update. The game refuses to connect players whose
  versions differ.
- **Same gameplay-changing mods, at the same versions.** This includes the
  Archipelago mod, RitsuLib and every character mod used by anyone in the lobby.
  If one person wants to play Watcher, everyone needs to install the Watcher
  mod, even players who will not pick it. The game checks this when someone
  joins and shows a mod mismatch error if the lists differ.
- **Other mods may differ.** Mods that only show information or change visuals,
  such as IntentGraph or MintySpire2, may work when only some players have
  them. If anything goes wrong, first make sure everyone has exactly the same
  mods installed and enabled.

Workshop subscriptions update automatically. If versions still differ, restart
Steam and let it finish downloading before starting the game.

## Two ways to join

Every player in the lobby takes part in one of two ways:

| | AP slot player | Vanilla guest |
| --- | --- | --- |
| Connects to Archipelago | Yes, before opening Multiplayer | No |
| Can host a lobby | Yes | No |
| Sends checks and receives items | Yes, for their slot | No |

**The host must be connected to an AP slot.** Hosting is blocked otherwise.

A vanilla guest plays the run alongside everyone else without an AP slot. To join
as a guest, do not connect to Archipelago. If you are still connected from an
earlier session, disconnect from the main menu first; otherwise you will join
with that slot.

### Sharing a slot

Most groups will want everyone to connect to the same AP slot. Every player on
that slot receives its items and sends its checks, and a check completed by one
player counts for everyone on the slot. Before the run starts, the host is asked
to confirm that several players are sharing the slot's progress.

Several players clearing the same slot's checks can make it progress very
quickly.

### One slot per player

If sharing feels too fast, give each player their own YAML and slot. Each player
connects to their own slot and is responsible for their own checks and goal.

AP slot players can only choose characters that are unlocked in their own slot.

### Slots are not tied to a mode

Nothing in the APWorld makes a slot singleplayer or multiplayer. The same YAML
works for both, and you can play a slot in singleplayer one session and in
multiplayer the next. Singleplayer checkpoints and multiplayer campaigns are
saved separately.

## Plan for a slower game

Multiplayer runs take considerably longer than singleplayer runs. The whole
group plays one run together, so you cannot hot-swap between characters the
way you can in singleplayer. When generating for multiplayer, consider a
smaller game:

- Fewer characters: lower `num_chars_goal`, set `pick_num_characters`, or list
  fewer `characters`.
- Fewer checks: turn off some of `gold_sanity`, `potion_sanity`,
  `campfire_sanity` and `shop_sanity`, or reduce the shop slot counts.

## Hosting a lobby

1. Connect to Archipelago with your slot from the main menu.
2. Select **Multiplayer**, then **Host**.
3. Start a new campaign or continue an existing one from the campaign picker.
4. Wait in the lobby until everyone has joined, then press **Embark**.

Players can only join while the host is in the lobby. Once the run starts,
the lobby closes. To continue a campaign later, the host loads it from the
campaign picker and the same players join again.

Ascension and other run settings come from the host's slot.

## Joining a lobby

1. AP slot players connect to Archipelago first. Vanilla guests stay
   disconnected.
2. Join the host's lobby using one of the methods below.
3. Choose your character and press **Embark**.

### If you are Steam friends

The game's lobbies are open to the host's Steam friends. Any of these work:

- In the game, select **Multiplayer**, then **Join**. Friends with open lobbies
  are listed there.
- In Steam's friends list, select **Join Game** on the host.
- Accept an invite the host sends through the Steam overlay.

Steam starts the game if it is not already running. If you are in the middle
of a run, the game asks before quitting it to join.

### If you are not Steam friends

Adding each other as Steam friends is the simplest option. Otherwise, the host
can share a join link:

1. Host the lobby in the game.
2. In the Steam client, open your own Steam profile. While you are in a lobby it
   shows a green banner with a **Join Game** button.
3. Right-click **Join Game** and copy the link. It looks like
   `steam://joinlobby/2868840/<lobby number>/<your Steam ID>`.
4. Send the link to the other players.
5. Each player opens the link, for example by pasting it into a web browser's
   address bar and allowing it to open Steam. Steam then starts the game and
   joins the lobby.

The link only works while the host is in that lobby. If the host leaves and
hosts again, copy a new link.

## Multiplayer limitations

- AP menu rewards, including potions, can only be claimed outside combat. Claim
  the potions you want before entering a fight.
- DeathLink works, but Death Fragments are disabled in multiplayer.
- **Anytime** Ancient rewards are recommended. With **Start of Act**, Ancient
  rewards missed before a checkpoint cannot be claimed later in that run.

## Troubleshooting

- **Mod mismatch or version mismatch when joining:** compare game branches and
  installed mods, including mod versions. See [Everyone needs the same mods](#everyone-needs-the-same-mods).
- **Host is blocked:** connect to an AP slot before selecting **Host**.
- **Joined with a slot instead of as a guest:** return to the main menu,
  disconnect from Archipelago and join again.
- **Players drift out of sync:** use **Export Bug Report** under Archipelago
  Settings and include the report when asking for help.
