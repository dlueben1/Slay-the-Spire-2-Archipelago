> [!WARNING]
> Both Slay the Spire II and this Archipelago Mod are in active development. As such, while this _is_ playable, expect there to be bugs and limited features. We appreciate your playtesting!

# Changelist

<This will be manually updated by me when the script is done running>

# Mod Information

## Pre-Requisites

- **Your Slay the Spire II client must be running on Windows**. The debug terminal we use uses Win32 APIs.
  - This will not be a requirement long-term, but for early Alpha development it's necessary.
- **Your host MUST use Archipelago Client v0.6.7+**.
- This version of the mod is intended to be used for **v0.103.2** of Slay the Spire II
  - **You must be on the "Default Public Version" of the game, _NOT_ the public beta branch**
  - If your installation of Slay the Spire II is higher or lower than this, the mod _may_ not work.
  - We will do our best to keep up with game updates as they release, so please be patient.

## Installing the Mod

1. Download the "sts2-client.zip" from the [Releases](https://github.com/dlueben1/Slay-the-Spire-2-Archipelago/releases/latest) section of the Repo
2. Go to your Slay the Spire II directory (In Steam, click "Browse Local Files")
3. If a folder called `mods` does not exist, create it
4. Unzip the **contents** of `sts2-client.zip` into `mods`

- If you've done this step correctly, your directory structure should look like this: `/<slay-the-spire-2-local-files>/mods/Archipelago/` and the contents of that folder should be a bunch of `.dll` files and a `.pck` file (there may be more files too, please don't touch anything in this folder)

5. Start the game

### Additional Steps for **Hosts**

6. Download `spire2.apworld`
7. Open your Archipelago Launcher
8. Click "Install APWorld"
9. Select `spire2.apworld` in the file dialog that pops up
10. **Restart the Archipelago Launcher**
11. Now you should be able to properly host/generate an Archipelago Session with StS 2

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
