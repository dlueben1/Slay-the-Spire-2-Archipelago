# Exporting a multiplayer bug report

After a state divergence, open **Archipelago Settings → Bug Reports → Export Bug Report**,
or type **`ap report`** in the developer console. `ap re` supports completion to `ap report`;
the command's usage hint also describes the export. No AP connection or active run is required.

The exporter opens the file manager at a timestamped ZIP in
`<game user-data>/logs/ArchipelagoReports/`. Send that ZIP to the developer manually.
The Settings status card and console show the output path and divergence file timestamp.
On Windows this uses Explorer; other platforms use Godot's platform file-manager API,
with opening the containing folder as a fallback.

The archive contains:

- `divergence/`: every entry from the newest `ritsulib_state_divergence_*.zip`, unchanged.
  RitsuLib includes the state comparison, capture metadata, local structured logs, and
  remote structured logs when available. These are retained logs, not necessarily an entire run.
- `warnings-errors.txt`: warning/error records from both embedded replica logs, with
  replica filenames, timestamps, and complete message bodies/stack traces.
- `game-logs/`: up to five newest available `godot*.log` files, copied even if still open.
- `README.txt`: source filename, export/source/log timestamps, and any optional-file failures.

Selection is by the divergence ZIP's last-write time, with filename as a tie-breaker.
An older report is still exported and clearly dated. Godot logs may belong to later sessions;
they are additional context, not evidence that those sessions produced the selected divergence.
No report means a visible error rather than an empty export. An unreadable/broken report
also fails visibly; wait for RitsuLib to finish writing it and retry.

The original files are preserved. This does not read RitsuLib's private live buffer, require
the browser viewer, collect saves/replays/console history, or upload anything.

## Implementation and validation

`APDevCommand.Process` and `ModSettingsRegistration.ConfigureBugReportsSection` both call
`ApBugReport.TryStart`. `ApBugReportFiles.Create` performs file/ZIP work on a background task;
`ApBugReport` defers file-manager/UI feedback to Godot's main thread. Repeated clicks while
exporting are ignored. No gameplay, grant, network, or save state is changed.

Run the archive regression cases with:

```powershell
dotnet test client/StS2AP.RegressionTests/StS2AP.RegressionTests.csproj -c Release --filter FullyQualifiedName~BugReportTests
```

In-game checks (separate from compilation/archive tests):

| Scenario | Expected result |
| --- | --- |
| After a divergence, use `ap re`/Tab and execute `ap report` | Completion/usage hint works; Explorer selects the completed ZIP; `[AP report] Bug report ready:` includes the path/date |
| Use the Settings button; click again or close Settings during export | One export at a time; no UI freeze; completion is logged and status is available when Settings is reopened |
| Return to the menu/restart and export an older report | Its original timestamp is shown; newer game logs are identified separately in README |
| No divergence ZIP, or a report still being written | Visible `Bug report export failed:` message; no completed/partial export is left behind; retry after publication |
| Linux, including a missing file-manager integration | Folder opens when supported; otherwise a successful export retains its path and reports the folder-opening failure |

Windows/Linux file-manager behavior and the live Settings/console flow require in-game testing.
