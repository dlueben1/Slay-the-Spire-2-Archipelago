# Step 1: Generating the Archipelago Session

To setup an Archipelago Randomizer game with your friends, you'll need the following:

## Archipelago Launcher

To create an Archipelago Multiworld session with your friends, you'll need to install the Archipelago Launcher locally. This tool allows you to setup new Archipelago games, connect many different games to an Archipelago session, and host games on your home network.

To install the Archipelago Launcher, [download the latest version from Archipelago's GitHub page](https://github.com/ArchipelagoMW/Archipelago/releases/latest).

> [!IMPORTANT]
> You _must_ be on **v0.6.7** or higher of the Archipelago Launcher. If you're familiar with Archipelago and have an existing installation, please check that you're on an appropriate version.

## Slay the Spire II's `.apworld`

An `.apworld` file provides support for games that are not officially supported by Archipelago yet.

There are two ways to get this file:

- If you have Slay the Spire II installed, and have already setup the Slay the Spire II Archipelago Mod, you can select the "Install APWorld" option on the game's main menu.
- Otherwise, you can [visit our GitHub](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest/) and download it from the latest release. It should be near the bottom of the page, in a link called `spire2.apworld`. Once downloaded, double click the `.apworld` to install it - and be patient, this method takes up to a minute to give you a confirmation dialog.

## YAML Files for each Participant

Archipelago expects one `.yaml` file for each player. This defines their player name, and more importantly the options they've configured for the randomizer to consider.

Each player is expected to provide their own `.yaml` file to the person generating/hosting the game.

For players that are playing Slay the Spire II, **it's recommended to use the [YAML Builder](/#/builder) found on our website**, as it provides a simple wizard-style walkthrough to configuring how you'd like your Slay the Spire II experience to go.

For everyone else, or for players that do not have access to the YAML Builder for some reason, you can use one of these two options:

- In the Archipelago Launcher, a player can use the "Options Creator", which is Archipelago's provided GUI for generating YAML files.
- A player can manually edit a YAML file in a program like "Notepad++". Each game they have installed provides a YAML template that can be found by going to the Archipelago Launcher and selecting "Generate Template Options"

> [!TIP]
> For more information on configuring YAML files, please visit the official [Archipelago website](https://archipelago.gg/tutorial/Archipelago/advanced_settings_en)

# Generating the Session

Now that you have everything you need to create your Archipelago session, let's begin!

1. Open the Archipelago Launcher
1. Select "Browse Files". This will open File Explorer to Archipelago's install directory.
1. In the new File Explorer window, select the `Players` folder
1. Move/Copy all of your participant's YAML files into this folder
1. Back in the Archipelago Launcher, click "Generate"

If everything succeeded, you should see a `.zip` file in the `output` folder inside of the Archipelago install directory.

This concludes generating the game, but we still need to host it so others can play!
