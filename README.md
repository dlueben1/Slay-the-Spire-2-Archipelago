# Slay the Spire 2 Archipelago

Archipelago Client & APWorld for STS2

## Alpha Development

This mod is currently in Alpha, and is unfinished. It's not feature-complete to consider it a v1, and it's not ready for playing casually. It's purely in the experimental phase right now.

Currently it's a simple proof-of-concept where you can only get Gold Rewards and you only send checks as you climb the Spire. Everything else will be coming soon!

> [!WARNING]
> The mod only supports English at the moment, but as we understand localization better, we can work with the community to support more languages

# Installing the Mod

1. Download the "sts2-client.zip" from the [Releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest) section of the Repo
2. Go to your Slay the Spire II directory (In Steam, click "Browse Local Files")
3. If a folder called `mods` does not exist, create it
4. Unzip the **contents** of `sts2-client.zip` into `mods`

- If you've done this step correctly, your directory structure should look like this: `/<slay-the-spire-2-local-files>/mods/Archipelago/` and the contents of that folder should be a bunch of `.dll` files and a `.pck` file (there may be more files too, please don't touch anything in this folder)

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

Copy the folder inside `world` into your local Archipelago Installation's `world` folder, then open the launcher and select "Build APWorlds", and you'll find it in the `build` folder

> [!WARNING]
> If you need to update `ItemTable.cs` because you've changed the items in the APWorld, run `./scripts/generate_item_enums.ps1`. This will cause many errors but can be helpful if a large change was made.

## Building the Game Client

Use `Ctrl+Shift+B` or `Build > Build Solution`. This will automatically place the mod in the `mods` folder of Slay the Spire

> [!CAUTION]
> Note that while this is a Godot game, and Godot is needed for _building_ the mod, I don't know how to use the Godot Engine itself to modify/view any game files. All of this is handled entirely in code in Visual Studio.

> [!TIP]
> While developing the mod, use `Ctrl+Alt+J` to view the "Object Browser", this will let you view the `sts2` namespace and all of the source code for the game

# Special Thanks

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
- Platano Bailando for creating the StS 1 Archipelago that inspires this one!
- [lamali292](https://github.com/lamali292) for their initial guide on creating mods for Slay the Spire 2!
- [alwaysintreble](https://github.com/alwaysintreble/ArchipelagoBepInExPluginTemplate/tree/master) for their example Archipelago C# Plugin Template

# Roadmap

You can view the current roadmap in detail [here](https://github.com/users/dlueben1/projects/1)

## MVP

- [ ] Fix bug where clicking the debug terminal freezes the game
- [x] Very Simple Proof-of-Concept Gameplay loop (Receive Gold as Items, Act 1 Boss is clear, reaching each floor is a check)
- [x] Implement Goal: Complete Act 3
- [ ] Implement Core Features: Card, Relic Rewards, Defeating Bosses
- [ ] UI: Archipelago Rewards Menu, Notifications
- [ ] Code Quality
  - [x] Improve APWorld logic
  - [ ] Handle Disconnection / Connection Instability / Sync missed Checks and Items

## Future

> [!TIP]
> If there's a feature you're looking for, please reach out on Discord or add a request to the repo!

- [ ] Support other Mods
- [ ] Multiplayer Support

> [!IMPORTANT]
> Multiplayer is going to be **complex**, and will only be started on when **everything else is very stable**. Thanks for understanding!
