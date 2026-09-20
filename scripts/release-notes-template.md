> [!WARNING]
> Both Slay the Spire II and this Archipelago Mod are in active development. As such, while this _is_ playable, expect there to be bugs and limited features. We appreciate your playtesting!

# Version Information

- **Archipelago Client:** {{CLIENT_VERSION}}
- **APWorld:** {{WORLD_VERSION}}
- **Slay the Spire II Public:** v{{STS2_PUBLIC_VERSION}}
- **Slay the Spire II Beta:** v{{STS2_BETA_VERSION}}

# Changelist

<Add the changelist here before publishing this draft release.>

# Mod Information

## Pre-Requisites

Supported on Windows (primary), Linux (confirmed), and macOS (expected to work, untested).

- **Your host MUST use Archipelago Client v0.6.7+**.
- The GitHub Release package is built for the **v{{STS2_PUBLIC_VERSION}} public version** of Slay the Spire II.
- The current beta compatibility target is **v{{STS2_BETA_VERSION}}**.
  - If your installation of Slay the Spire II has a different version, the mod _may_ not work.
  - We will do our best to keep up with game updates as they release, so please be patient.

## Installing the Mod

### Steam Workshop (Recommended)

1. If you previously installed the mod manually, remove the `Archipelago` folder from your game's `mods` folder, or move it outside `mods`, so only the Workshop copy loads.
2. Subscribe to [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295).
3. Subscribe to [Slay the Spire II Archipelago](https://steamcommunity.com/sharedfiles/filedetails/?id=3748826296).
4. Wait for Steam to finish downloading both mods, then start the game.

### Manual Installation

1. If you subscribed to the Archipelago mod on Steam Workshop, unsubscribe first so only the manual copy loads: in Steam, right-click **Slay the Spire 2 → Properties → Workshop**, then untick **Slay the Spire II Archipelago**. Keep RitsuLib subscribed.
2. Ensure that you have [RitsuLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) installed. The easiest way to obtain it is from Steam Workshop.
3. Download `Archipelago.zip` from this release's assets.
4. Open your game directory: in Steam, right-click **Slay the Spire 2 → Browse Local Files**.
5. If a folder called `mods` does not exist, create it.
6. Copy `Archipelago.zip` into `mods`, then right-click the ZIP and choose **Extract All**. Keep the default destination, which creates an `Archipelago` folder beside the ZIP.
7. Check that the files are in `/<slay-the-spire-2-local-files>/mods/Archipelago/`, with the `.dll`, `.pck`, and other packaged files directly inside that folder. If you use a different archive tool, extract into a folder named `Archipelago` inside `mods`.
8. Start the game.

### Additional Steps for **Hosts**

These steps apply whether you installed the mod through Steam Workshop or manually.

On Windows, if Archipelago Launcher is installed, you can also install the downloaded `.apworld` by double-clicking it in File Explorer.

1. Download `spire2-{{WORLD_VERSION}}.apworld` from this release's assets.
2. Open your Archipelago Launcher.
3. Click "Install APWorld".
4. Select `spire2-{{WORLD_VERSION}}.apworld` in the file dialog that pops up.
5. Now you should be able to properly host/generate an Archipelago Session with StS 2.

- If you want to use `archipelago.gg` to host the game, generate it locally first following the steps above, then upload the `.zip` file from the `output` folder in your Archipelago installation

> [!IMPORTANT]
> You need to use Archipelago Version 0.6.7+ and CANNOT use earlier versions of Archipelago with this mod!

## Known Issues/Limitations

- If you receive a Character Unlock while you're on the Character Select screen, they won't unlock until you revisit that screen after doing a run (likely an edge case for most)

## Common Q&A

### Will this mess with my unmodded Save File?

No.

### I installed the AP World but it's not working

Is your Archipelago Launcher v0.6.7 or later? If not it **won't work**.

### (Your-Feature-Here) looks really ugly or isn't polished

We probably know and are going to work on it, but if it's egregious or an edge case, please reach out and let us know more about it so we can look into it!
