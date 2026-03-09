# Slay the Spire 2 Archipelago

Archipelago Client & APWorld for STS2

## Alpha Development

This mod is currently in Alpha, and is unfinished. It's not feature-complete to consider it a v1, and it's not ready for playing casually. It's purely in the experimental phase right now.

Currently it's a simple proof-of-concept where you can only get Gold Rewards and you only send checks as you climb the Spire. Everything else will be coming soon!

# Installing the Mod

1. Download the "sts2-client.zip" from the [Releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest) section of the Repo
2. Go to your Slay the Spire II directory (In Steam, click "Browse Local Files")
3. If a folder called `mods` does not exist, create it
4. Unzip the **contents** of `sts2-client.zip` into `mods`
5. Start the game

> [!CAUTION]
> I don't recommend having any other mods installed whatsoever, especially while this is in Alpha development. Do it at your own risk.

# Building the Projects

## Prerequisites

You need the following installed:

- Slay the Spire 2
- Godot v4.5.1 (the .NET version, _NOT_ the standalone version)
- Visual Studio 2022 (for the Game Client / C# part)
- Visual Studio Code (for the AP World)
- .NET 9

You also need to:

- Create a clone of Archipelago's repo should live in `../Archipelago` or Python will complain about the APWorld's `.py` files
- Copy `client/StS2AP/local.props.template` to `client/StS2AP/local.props` and update `<STS2GamePath>` and `<GodotExePath>` to match your local installations
  - `<STS2GamePath>` should point to the directory for the game in Steam
  - `<GodotExePath>` should point to the Godot Directory that has `Godot_v4.5.1-stable_mono_win64.exe`

> [!CAUTION]
> For the moment this mod only supports Windows, primarily because of the way I'm handling real-time logging for the purpose of debugging the app. This should not be the case in the future.

## Building the APWorld

Run `./scripts/build-world.ps1` to generate the `.apworld` file. It should be in `dist/slaythespire2.apworld`

## Building the Game Client

Use `Ctrl+Shift+B` or `Build > Build Solution`. This will automatically place the mod in the `mods` folder of Slay the Spire

# Special Thanks

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
- Platano Bailando for creating the StS 1 Archipelago that inspires this one!
- [lamali292](https://github.com/lamali292) for their initial guide on creating mods for Slay the Spire 2!
- [alwaysintreble](https://github.com/alwaysintreble/ArchipelagoBepInExPluginTemplate/tree/master) for their example Archipelago C# Plugin Template

# To-Do List

## v1

- [] Fix bug where clicking the debug terminal freezes the game
- [x] Very Simple Proof-of-Concept Gameplay loop (Receive Gold as Items, Act 1 Boss is clear, reaching each floor is a check)
- [] Implement Goal: Complete Act 3
- [] Implement Features
  - [] Card Rewards
  - [] Gold Rewards
  - [] Potions
  - [] Progressive Campfires
  - [] "Trap Relics" (Relics that can hurt you when you receive certain traps)
  - [] "Filler Relic" (Relic that heals you a small amount upon filler item received)
- [] UI
  - [] AP Log
  - [] Archipelago "Rewards" Window/Menu Item
  - [] Mod Info Details
  - [] Icon for Mod
  - [] Icons for Archipelago Window, AP-specific Relics
- [] Quality-of-Life
  - [] Unlock all characters, epochs, relics, cards, etc. on Mod Entry
  - [] Log for Archipelago Items Sent/Received
- [] Code Quality
  - [] Handle Errors properly
  - [] Improve APWorld logic
  - [] Handle Disconnection / Connection Instability / Sync missed Checks and Items

## Future

> [!TIP]
> If there's a feature you're looking for, please reach out on Discord or add a request to the repo!

- [] Per-Character Checks, Unlocks, Items (like how StS 1 handles it)
- [] Support other Mods
- [] Multiplayer Support

> [!IMPORTANT]
> Multiplayer is going to be complex, and will only be started on when everything else is very stable.
