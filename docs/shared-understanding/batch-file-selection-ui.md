<batch-file-selection-ui>

<goal>
The BATCH palette's file selection shows clearly what the folder and mask select,
it remembers the folders that the user used, and it never freezes AutoCAD on a
network folder that does not answer (for example a VPN share while the VPN is down).
</goal>

<decisions-done>
These are built and verified live in Civil 3D.

- The "N files matched." text has a box. Green = the mask selects files. Red = it
  selects none, or the folder/mask is not valid. Gray = no folder entered.
- Hover on the box shows the file list at once: matched files in green above,
  unmatched files in red below. Red = the drawing files in the folder that
  the mask does not match.
- The tool sees only drawing files (.dwg, .dwt, .dws): they are the only
  files that can be matched, and the only files in the red list. A .bak or any
  other file is never shown. *.* selects all drawings; *.bak selects nothing.
  Reason: a run opens each file with Database.ReadDwgFile, which reads only these.
  (.dws = a drawing standards file, a DWG-format drawing for CAD Standards.)
- Natural sort (File Explorer order, 3 before 10) everywhere: the tooltip, the
  run order and the agent tools. Implemented with Windows StrCmpLogicalW.
- With Recurse on, the list shows each path relative to the folder.
</decisions-done>

<decisions-new>
From the answers of 2026-09-27.

<hover-list>
Replace the ToolTip with a standard WPF Popup that takes mouse input.
Reason: WPF makes a StaysOpen ToolTip click-through (WS_EX_TRANSPARENT,
ToolTip.HookupParentPopup: HitTestable = !StaysOpen). So on the tooltip the
wheel goes to the element below it. StaysOpen=false was tried; then the
tooltip does not open at all (reverted).
- Opens at once when the mouse enters the box (not for the gray/error states).
- Closes when the mouse is on neither the box nor the popup.
- The wheel and the scrollbar work natively on the list, also when the mouse is
  on the popup. The wheel handler on the box stays for when the mouse is on the box.
</hover-list>

<recent-folders>
- The Folder text box becomes an editable ComboBox (a standard control). The
  dropdown shows up to 20 recent folders, newest first, with no duplicates
  (case-insensitive).
- Each row has a small minus button on the left. It removes that folder from
  the list. It does not select the row.
- A folder goes into the list after each successful scan: Refresh, Browse, a pick
  from the list, or the agent's autocad_batch_set_selection. "Successful" = the
  folder exists, also with 0 matches. A folder with an error is not added.
- A pick from the list sets the folder and refreshes at once (current mask and recurse).
- Typing a folder that is in the list does not select it (IsTextSearchEnabled=False),
  so typing never starts a scan.
- A saved folder that no longer exists stays in the list. A pick gives the
  normal red "does not exist" box. The list never checks its folders on load
  (that check can freeze on a network folder).
- Stored locally: %LOCALAPPDATA%\Acd.Mcp\recent-folders.json, next to
  buffer-batch.csx and batch-runs\.
</recent-folders>

<no-freeze-on-network-folders>
Today Find runs Directory.Exists and EnumerateFiles on the UI thread (Refresh)
and on AutoCAD's main thread (the agent's set_selection). On a share that does
not answer, Directory.Exists blocks in Windows for up to minutes, so AutoCAD freezes.
Browse also calls Directory.Exists(Folder) on the UI thread.

New rules:
- A scan never runs on the UI thread or on AutoCAD's main thread. It runs on
  its own thread. The palette stays usable.
- The folder check (Directory.Exists) has a time limit of 5 s. If Windows does
  not answer in 5 s, the scan fails with: "Folder '...' did not answer within
  5 s. A network location may not be reachable." The box goes red. The blocked
  thread is left to end by itself (Windows cannot cancel that call).
- The file listing after a good folder check has no time limit, but it also
  runs off the UI thread.
- While a scan runs, the box shows "Scanning..." in the gray style, and the
  Run button is disabled.
- A new scan replaces a scan that is still running: the older result is
  discarded when it arrives.
- The agent's autocad_batch_set_selection does the scan first (on the pipe
  thread, same 5 s limit), then puts the result into the palette on the main
  thread. The main thread never waits for the network.
- Browse gives the dialog the current folder as start folder only when the
  last scan of that folder succeeded. It never checks the folder on the UI thread.
</no-freeze-on-network-folders>
</decisions-new>

<modules>
- BatchFileSelection (Acd.Mcp/Batch): Find stays synchronous and testable. New
  FindAsync(folder, mask, recurse, folderTimeout, ct) runs Find on its own
  thread, with the time limit on the folder check. The thread and the time
  limit are hidden inside this module.
- RecentFolderList (new, Acd.Mcp/Batch, AutoCAD-free): Add(path), Remove(path),
  Items, with the cap of 20 and the dedupe rule. Load and Save to a JSON file
  whose path the caller gives (tests use a temp folder).
- BatchViewModel: Refresh becomes async (scan off the UI thread, generation
  counter for older results). New MatchState value Scanning. RecentFolders
  collection, PickRecentFolder and RemoveRecentFolder commands. ApplySelection
  takes an already-found BatchFileScan instead of doing the scan itself.
- IBatchUiState.ApplySelection(folder, mask, recurse, scan): the interface
  changes, because the scan moves out of the main-thread work.
- BatchRpcHandler.HandleSetSelectionAsync: awaits FindAsync first, then
  OnBatchPaletteAsync(ui => ui.ApplySelection(...)).
- BatchControl.xaml / .xaml.cs: ComboBox for the folder; Popup instead of
  ToolTip, with open/close code-behind.
- Theme.xaml: editable ComboBox template (PART_EditableTextBox; the current
  template has none), the recent-folder row template with the minus button,
  and the Popup style for the file list. No local styles in the view.
</modules>

<tests>
- BatchFileSelection.FindAsync: a folder check that does not answer ends in
  TimeoutException with the message above. The test gives a fake folder check
  that blocks, because a real dead share cannot be made in a unit test.
- RecentFolderList: newest first, dedupe case-insensitive, cap 20, Remove,
  Save + Load round trip, a missing file loads as an empty list.
- Live in Civil 3D: a UNC path to a host that does not exist
  (\\no-such-host\share) must give the red timeout box within about 5 s, and
  AutoCAD must stay usable during those 5 s.
</tests>

<answered-questions>
- A recent-folders file that cannot be read (bad JSON): the user's decision is
  to rebuild it from nothing, with no message. Load gives an empty list; the
  next Add overwrites the file.
- autocad_batch_set_selection (and every agent call that goes through
  OnBatchPaletteAsync) changes the palette to the BATCH tab, so that the user
  sees the new selection. After a reload the palette opened on SCRIPT.
</answered-questions>

</batch-file-selection-ui>
