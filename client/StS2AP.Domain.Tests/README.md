# F# domain tests

Run from the repository root:

```powershell
dotnet test client/StS2AP.Domain.Tests/StS2AP.Domain.Tests.fsproj -c Release
```

These are discoverable xUnit tests written in F#, with FsCheck property tests. They reference
only `StS2AP.Domain`, not the client, MegaCrit, Godot, Harmony, or RitsuLib. No game installation,
AP server, local.props, or packaging/export step is needed. FsCheck.Xunit 3.3.2 uses xUnit v2;
the package choices deliberately keep those versions compatible.

The tests target net9.0, like the domain library. `RollForward=Major` lets the runner use
.NET 10 on development machines without .NET 9 installed; CI installs both SDKs/runtimes.
This is domain behavior verification, not proof of the game's embedded runtime integration.

Use `[<Fact>]` for examples and `[<Theory>]` for explicit boundary cases. Use
`[<Property(MaxTest = 500)>]` for invariants over generated inputs. FsCheck shrinks failing
inputs and reports replay information: retain that output and turn important counterexamples
into named regression tests. Reproduce a seed using the property's `Replay` setting when needed;
do not permanently pin every property to one seed.

Current coverage includes both supported assignment provenances, malformed/null wire
values, provenance identity, selective delegate evaluation,
round trips, and repeatability. `MirroredRewardTests.fs` adds complete reward shapes,
immutable snapshots, card recipes/reveal transitions,
effect overflow/duplicates, and idempotent effect application. Properties exercise
500 generated examples each. They do not
claim to test native RNG advancement, network ordering, reward grants, saves, or reconnection.
The C# adapter tests reject obsolete replica-generation requests; generation is no
longer a representable case in the F# model.

All emitted F# compiler warnings are errors, and unused-variable warning FS1182 is explicitly
enabled. Intentional unused inputs should be named `_` or `_description`. The domain library
already treats emitted warnings as errors; turning warnings into errors does not enable
warnings which are disabled by default.

The separate C# xUnit project, `StS2AP.RegressionTests`, checks adapter calls and packaging.
Keep language-boundary tests distinct from domain unit tests.
