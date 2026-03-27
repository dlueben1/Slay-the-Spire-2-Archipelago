> [!WARNING]
> Both Slay the Spire II and this Archipelago Mod are in active development. As such, while this _is_ playable, expect there to be bugs and limited features. We appreciate your playtesting!

# Changelist

<This will be manually updated by me when the script is done running>

# Mod Information

## Pre-Requisites

- **Your Slay the Spire II client must be running on Windows**. The debug terminal we use uses Win32 APIs.
  - This will not be a requirement long-term, but for early Alpha development it's necessary.
- **Your host MUST use Archipelago Client v0.6.7+**.
  - The current official release is v0.6.6, so you need to be able to build the `main` branch of Archipelago [from source](https://github.com/ArchipelagoMW/Archipelago) or download and use the [0.6.7 RC](https://github.com/ArchipelagoMW/Archipelago/releases/tag/0.6.7-rc1).
  - **However**, once the game has been generated, it can still be hosted on [www.archipelago.gg](www.archipelago.gg)
- If your installation of Slay the Spire II is **higher than v0.99** this mod _may_ not work. We will do our best to keep up with game updates as they release.
- Friends that are forgiving and understanding so that if your multi-hour run breaks due to a bug, they won't mind :smile:

## Installing the Mod

1. Download the "sts2-client.zip" from the [Releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest) section of the Repo
2. Go to your Slay the Spire II directory (In Steam, click "Browse Local Files")
3. If a folder called `mods` does not exist, create it
4. Unzip the **contents** of `sts2-client.zip` into `mods`

- If you've done this step correctly, your directory structure should look like this: `/<slay-the-spire-2-local-files>/mods/Archipelago/` and the contents of that folder should be a bunch of `.dll` files and a `.pck` file (there may be more files too, please don't touch anything in this folder)

5. Start the game

## Known Issues/Limitations

- There is no saving.
  - If you exit the game then reboot it, you might have problems.
  - If you end a run early and find a way to resume it, you WILL have problems.
- If you receive a Character Unlock while you're on the Character Select screen, they won't unlock until you revisit that screen after doing a run (likely an edge case for most)
- **Card Rewards, if skipped, are not returned until the next run. This is different than Slay the Spire 1's implementation. It will likely change in the future.**

## Common Q&A

### Will this mess with my Save File?

My understanding is that when StS runs modded, it uses a different set of Save Files. So your _vanilla_ save files should be totally unbothered by this mod.

### I installed the AP World but it's not working

Is your Archipelago Launcher v0.6.7 or later? If not it **won't work**. You either need an official build, or if none is out yet, you need to build it from the source code!

### I closed out of the game and it's behaving weird when I re-open it

This is very early development, we expect it not to work right and not to save progress when you close the game.

### (Your-Feature-Here) looks really ugly or isn't polished

We probably know and are going to work on it, but if it's egregious or an edge case, please reach out and let us know more about it so we can look into it!
