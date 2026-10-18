# Slay the Spire 2 Archipelago

Archipelago Client & APWorld for STS2

# Download the Latest Playable Release [Here](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest)

> [!IMPORTANT]
> We recommend the **beta branch**, where Terairk mainly develops and tests the mod.
> The main branch remains supported, but receives little, if any, gameplay testing;
> compatibility is assessed primarily through compile-time checks and player bug reports.
> Beta branch updates can temporarily break compatibility until the mod is updated.
>
> The client contains exact builds for the main branch **v0.107.1** and beta branch
> **v0.111.0** game versions.

## 1.0 Development onwards

This mod is currently in 2.0 stage and is practically feature-parity with the AP from the StS1. While there are some edge case bugs, most people have successfully played an Archipelago with this world and had no issues.

> [!WARNING]
> The mod only supports English at the moment, but as we understand localization better, we can work with the community to support more languages

# Installing the Mod

See the [setup guide](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/blob/setup-docs-do-not-delete/docs/multiplayer_setup.md) for installation, YAML configuration, generation and connection instructions.

There are two ways to install the mod, detailed below.

## Steam Workshop

Subscribe to the mod on the [steam workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296).
Ensure that you have [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) installed. The easiest way to obtain it is from Steam Workshop.

## Manual installation

1. Ensure that you have [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) installed. The easiest way to obtain it is from Steam Workshop.
2. Download `Archipelago.zip` from the [Releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest) section of the repo
3. Go to your Slay the Spire II directory (In Steam, click "Browse Local Files")
4. If a folder called `mods` does not exist, create it
5. Put `Archipelago.zip` in `mods`, then extract it into the archive program's default `Archipelago` directory

- If you've done this step correctly, `mods/Archipelago/` contains the loader DLL,
  common dependencies, the PCK, and `lib/0.107.1/` plus `lib/0.111.0/`. Keep that
  directory structure intact.
- The bundled `spire2.apworld` lets hosts use the **Install APWorld** button from the game's main menu.

6. Start the game

# Features

## What's Included

- Card/Relic Rewards shuffled in the multiworld
- Goldsanity
- Campfiresanity (Progressive Rest/Smith)
- Potionsanity
- Saving to the AP Server, and Saving when Connection is Lost
- Death Link
- Shopsanity (currently playable but may lack some polish)
- Interesting Filler Items
- Progressive Ancients
- Progressive Starter Card and Relic (ie None -> Bash -> Break)
- Multiplayer Support
- Traps (more will be added)
- Modded Character Support

> [!CAUTION]
> Mods such as modded character are supported but may or may not work. I do know that 'Downfall Classic' and 'IntoTheSpireVerse' and also 'Watcher by Lamali' all work well as of the 2.0.0 release.

# Special Thanks

- [Archipelago.MultiClient.Net](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
- Platano Bailando for creating the StS 1 Archipelago that inspires this one!
- [lamali292](https://github.com/lamali292) for their initial guide on creating mods for Slay the Spire 2!
- [alwaysintreble](https://github.com/alwaysintreble/ArchipelagoBepInExPluginTemplate/tree/master) for their example Archipelago C# Plugin Template
- [Spire Codex](https://spire-codex.com) for providing an awesome public API that helps us stay up-to-date with Slay the Spire 2 data for our item pools
- All of the wonderful contributors to the project!

  <a href="https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=dlueben1/Slay-the-Spire-2-Archipelago" />
  </a>

# Roadmap

You can view the current roadmap in detail [here](https://github.com/users/dlueben1/projects/1)

> [!IMPORTANT]
> Multiplayer is going to be **complex**, and will only be started on when **everything else is very stable**. Thanks for understanding!
