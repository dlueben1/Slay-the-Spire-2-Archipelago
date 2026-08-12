# AP Reward UI Navigation and Debugging

## Design

The AP reward menu is a normal `IOverlayScreen`. It must not be forced above the
map or a capstone screen with custom `ZIndex` changes. The base game chooses the
active screen in this order:

1. capstone screen, such as the deck or a combat pile;
2. map;
3. top entry in `NOverlayStack`;
4. current room.

Therefore, the AP menu first closes any map or capstone and opens from the room.
It remembers only one return destination:

- `Room`: normal room, shop, or unsupported capstone;
- `Map`: the map was active when AP opened;
- `Deck`: the normal deck was active when AP opened.

Closing AP restores that destination after AP has been removed from
`NOverlayStack`. A later map or deck request replaces the remembered destination,
so the last navigation request wins. Restoring the deck creates a fresh deck
screen; scroll and sorting state are deliberately not retained.

Existing native overlays, such as treasure/combat rewards, remain below AP in
`NOverlayStack` and reappear naturally when AP closes. Native card pickers remain
above AP. While a picker is active, map or deck may temporarily hide the complete
overlay chain; closing map/deck returns to the picker. Pressing the AP button or AP
hotkey while its own picker is active invokes that picker's native Skip action and
returns to AP. Exact picker-instance ownership prevents this from affecting a
normal combat or treasure card reward.

## Implementation map

- `UI/ArchipelagoRewardUI.cs`
  - `PrepareForOpen()` closes map/capstones and selects `Room`, `Map`, or `Deck`.
  - `Hide()` removes AP, then calls `RestoreDestination()`.
  - `CloseToMap()` and `CloseToDeck()` implement last-request-wins navigation.
  - `APRewardScreenNode.AfterOverlayHidden()` releases AP hotkeys/blocking so a
    native picker or temporary map/deck screen owns input normally.
  - `Toggle()` makes AP button/hotkey input a no-op when AP is not the active
    screen context, except for invoking native Skip on the exact AP-owned picker.
  - `GrantAPCardReward()` records and clears exact picker ownership around the
    existing `SelectUnsynchronized()` flow.
- `Patches/Patches_APRewardScreen.cs`
  - defers direct map/deck opening until an active AP menu has closed;
  - does not intercept map/deck while a native picker is above AP.
- `Utils/GameUtility.cs`
  - card rewards now call `SelectUnsynchronized()` directly; navigation is no
    longer repaired inside the individual reward action.
- `UI/ArchipelagoTopBarUI.cs` and `ModSettingsRegistration.cs`
  - both entry points use the same `ArchipelagoRewardUI.Toggle()` path.

## Why tooltips were darkened

The previous implementation raised the entire `NOverlayStack` above the map.
That also raised its shared dark backstop, while `NHoverTipSet` instances are
added to the global `NGame.Instance.HoverTipsContainer`. The raised backstop could
therefore draw over AP relic, potion, and Ancient relic tooltips. Do not restore
the overlay `ZIndex` manipulation or map mouse suppression without rechecking
this draw-order interaction.

## Focused runtime checks

1. Open AP from a shop and treasure room; relic, potion, and Ancient tooltips
   should render above the dimmed background.
2. Map -> AP -> close AP: map returns.
3. Deck -> AP -> close AP: a fresh deck returns over the room.
4. Draw/discard/exhaust pile -> AP -> close AP: return to the room.
5. Native reward overlay -> AP -> close AP: the original overlay reappears.
6. AP -> card picker -> map/deck -> close map/deck: the picker resumes.
7. Press the AP button/hotkey during its card picker: native Skip returns to AP
   and leaves the reward claimable.
8. Press the AP button/hotkey during a normal card reward: it must not skip it.
9. Open AP from map, then request deck: deck is the final destination.

Useful temporary logs are: active screen type, current capstone type, map
`IsOpen`, overlay count/top type, AP `IsOpen`/`IsActive`, return destination, and
the start/end of AP close restoration. Remove noisy diagnostics after confirming
the transition.

Static source inspection does not prove runtime behaviour. This UI flow requires
in-game verification on the supported public game branch.
