# Client source layout

This is the C# game client/mod. The APWorld lives in `../../world/spire2`, and
validated F# domain values and decisions live in `../StS2AP.Domain`.

## Feature helpers

| Folder | Responsibility |
| --- | --- |
| `Utils/Rewards` | AP grants, native reward menus, stable relic pools, reward travel, and one-time buffs |
| `Utils/DeathLink` | DeathLink application, multiplayer delivery, and event deduplication |
| `Utils/Progression` | Ascension, progressive starters, and Ancient progression |
| `Utils/Actions` | Noncombat action admission and request scheduling |
| `Utils/Connection` | Session identity, reconnect/launch coordination, and pending location-check delivery |
| `Utils/Diagnostics` | Developer commands and bug-report export |

General helpers remain directly under `Utils`. The existing shop and rest-site
helpers also remain there; create further groups when several related files need
a shared home. Feature-specific multiplayer helpers stay with their feature.
Shared multiplayer coordination and messages remain under `Multiplayer`.

## Engine patches and models

| Folder | Responsibility |
| --- | --- |
| `Patches/Rewards` | Reward construction/presentation, received-item processing, and reward-related relic restrictions |
| `Patches/Progression` | Unlocks, ascension, floor/victory progression, and epoch restrictions |
| `Patches/Rooms` | Shops, rest sites, treasure rooms, and their diagnostics/compatibility fixes |
| `Patches/Lifecycle` | Run startup, saves, main/pause menus, death hooks, tutorial, and version presentation |
| `Models/Rewards/Grants` | Receipt identity and gold claims/offers |
| `Models/Rewards/Specs` | Serialized reward/menu specifications, reward kinds, and persistent effect data |
| `Models/Rewards/Presentation` | Game-facing AP reward objects and card/relic/potion item models |
| `Models/Custom` | Custom game content: Relic Coupons and the DeathLink curse card |
| `Models/Configuration` | Client settings, slot settings, and character configuration |

Other model types remain directly under `Models` or its existing `Singleton`
folder. `Persistence`, `Multiplayer`, `UI`, `Entities`, `Data`, `Extensions`, and
`DomainAdapters` retain their existing roles.

## Moving files

Folder moves and splitting grant types into individual files preserve namespaces,
type names, and implementations. Folder placement is a navigation aid, not a new
assembly boundary. Serialized specifications stay as C# transport/save data;
validated reward decisions remain in the F# domain project.
Harmony still discovers patches through `ModEntry` calling `PatchAll(assembly)`.
No JSON type/property names, message identifiers, or Godot resource paths change.

The client project includes C# files recursively. The regression project instead
links selected production files explicitly: update `Compile Include` and `Link`
paths in `../StS2AP.RegressionTests/StS2AP.RegressionTests.csproj` when moving them.
If the local-only admission harness is present, update its source links too;
it remains excluded from Git.

Keep file moves separate from namespace changes, type splitting, and behavioral
refactors so their effects remain easy to review.
