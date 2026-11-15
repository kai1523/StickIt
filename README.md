# StickIt

StickIt is a small, always-on-top Windows app that turns lines in nearby text files into copy buttons.

## Behavior

- Lists top-level `.txt` files in a left sidebar and shows the selected file's contents on the right.
- Drag `.txt` files onto the sidebar to copy them into the app folder; duplicate names receive `_2`, `_3`, and higher suffixes. The imported file becomes selected.
- Muted amber accents mark the StickIt bubble control and the currently selected file immediately after import.
- Right-click a file in the sidebar to open a file actions popup with **Open in editor** (the Windows default `.txt` app) and **Show in Explorer**. It remains open until an action is chosen or the next click occurs elsewhere.
- Turns ordinary non-blank lines into copy buttons. If a line contains a tab, text before the first tab is copied and the rest is shown as the button label; without a tab, the full line is shown and copied.
- Search works across all top-level `.txt` files in the app folder, case-insensitively matching comment text, visible button labels, and copied text. Results are grouped by filename. A matching comment shows its whole section up to the next comment; a matching button shows its comment heading as context.
- Search is toggled from a compact magnifier button in the top bar. The magnifier hides or reopens the field while retaining the query; its × button clears the query and restores the selected list. Settings is represented by a gear icon.
- Search matches are highlighted in comment headings and button labels. When only a button's copied value matches, its visible label receives a full-label accent.
- The magnifier icon uses a blue accent whenever a search query is active, even while the search field is hidden.
- Hovering a button previews the text that will be copied, including when the visible button label comes after a tab.
- Displays `##` to `#####` comment lines as non-clickable notes, with stronger color and type styling at higher levels.
- Watches the app folder for `.txt` additions, edits, removals, and renames. A Refresh button is also available.
- Compact dark theme with a default window size of **300 × 440** WPF units (approximately pixels at 100% display scaling).
- Resizable file sidebar; long button labels and filenames truncate to fit their panels.
- Resize the window from any edge or corner.
- Comment text has no background or indentation.
- Spacing reference: **2/10** (1 = tightest, 10 = roomiest); 8-unit outer margin, 6–8-unit panel padding, 6-unit panel gap, and 4-unit button gaps.
- Top bar groups Search, a compact manual refresh icon, and the gear-shaped Settings dropdown, followed by Minimize and Close. Text-file changes also refresh automatically.
- Minimize highlights in muted blue and Close in muted red on hover; both stay neutral otherwise.
- Comment marker hashes are hidden in the display.
- The resizable left panel starts at 67 WPF units. Panel divider and corners are minimal/squared.
- Settings dropdown contains Wrap and Fade controls. It moves with the window, stays open when its options are clicked, and closes when Settings or anywhere outside the dropdown is clicked. Active toggles turn green. Wrap grows buttons vertically to show their full text. Square −/+ buttons adjust the unfocused surface opacity from 10% to 90% in 10% steps (50% by default); borders and the top bar remain 25 percentage points more opaque. When Fade is enabled, StickIt stays opaque while focused; while unfocused, it fades its surfaces and hides text so only the window silhouette remains. The window stays clickable, and focusing it restores the text. Fade is off by default.
- Remembers the selected text file, Wrap and Fade states, Fade opacity, window size and position, and left-panel width between launches. Settings are saved under the current user's Local AppData folder; an off-screen saved position is ignored.
- Long file and content lists remain scrollable without displaying scrollbars.
- Copy feedback shows the copied text in the status bar.
- The left panel includes a compact, centered StickIt button that collapses the app into a draggable 68 × 68 square pastel sticky-note bubble. Its draggable top half is centered over the StickIt button; click its lower half to restore the main window at its original size, with the empty center of the top bar aligned to the restore button.
- Copy confirmation appears in a borderless status bar that matches the top-bar background; successful copy text is highlighted in pale green.
- The top and bottom bars are slightly thicker, with no gaps between the bars and the main panels.
- The sticky-note icon is used by the app window and Windows executable.
- Version: **1.07.14** (Major.Minor.Step).

## Development

Requires the .NET 10 SDK on Windows. Run the app from this folder with:

```powershell
dotnet run --project .\StickIt.csproj
```

Development runs load text files directly from the project folder, so Refresh picks up edits immediately. Published builds load text files from beside the executable.

## Optional standalone executable

To build the self-contained Windows x64 executable and a ZIP with the sample text lists, run:

```powershell
.\publish.ps1
```

The script reads the version from `StickIt.csproj` and writes the EXE and its sample lists under `dist\local\<version>`, with a ZIP beside that folder. It replaces only that version's generated output. The project is configured for a self-contained, single-file Windows executable.

## GitHub releases

Push a version tag such as `v1.07.14` to build a Windows x64 release. GitHub Actions runs the same `publish.ps1` script used for local builds, then attaches both `StickIt.exe` and a ZIP containing the EXE and sample `.txt` files. The workflow is in `.github/workflows/release.yml`.
