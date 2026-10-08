You are improving the UI/UX of TorrentRuler, an ASP.NET (.NET 9) + Bootstrap web app that automates qBittorrent/Jellyfin/Jellystat via scheduled SQL-based rules. Pages: Dashboard (/), Rules (/Rules, /Rules/Edit), Instances (/Instances), History (/History), SQL sandbox (/Sandbox), Settings (/Settings). Dark theme is the main one. Keep the existing stack (Bootstrap, no SPA rewrite), keep all current features, and don't change backend behavior unless a fix below needs it. Work page by page and commit each page separately.

## Global
1. Typography/density: body text and table text are too small and low-contrast in dark mode (gray-on-navy). Raise base font to 14–15px, lift muted text contrast to WCAG AA (≥4.5:1), and make table header labels readable.
2. Destructive actions: every "Delete" opens a confirm modal that names the item. Add a toast with an undo option where it's feasible.
3. Feedback: add toasts for save, run, test, and import results (success and error). Put spinner/disabled states on buttons while a request is in flight.
4. Layout: on wide screens content stretches to the edges with big empty areas. Use a sensible max-width container (~1400px) for forms; tables can stay fluid.
5. Responsive: check at 375px, 768px, and 1280px. Tables collapse to cards or scroll horizontally inside their card, never the page. The nav bar collapses into a hamburger.
6. Theme toggle: the three icon buttons (light/dark/system) have no visible labels. Add tooltips, or replace them with one dropdown.
7. Accessibility: every icon button gets an aria-label, focus rings are visible, and expand toggles use real <button> elements.
8. Empty states: every list or table gets a helpful empty state with a primary action.

## Dashboard
- The stat cards ("6/6", "22/28") are cryptic. Show them as "6 of 6 instances enabled" and make each card link to its page.
- "Matched, last 24h: 61328" is a cumulative count across repeated runs and reads as misleading. Add "unique torrents matched" or clarify the label. Add "applied, last 24h".
- Recent runs: highlight rows where Applied > 0 or Failed > 0, and make rule names link to the rule editor.
- Instances panel: show the last connection status/latency per instance (green/red dot) in place of the static "on" badge.
- Fill the empty lower half of the page with something useful: a sparkline of matched/applied per hour, and the next scheduled runs.

## Rules list
- Each row has 4 colored buttons (Run now/Edit/Duplicate/Delete), which is too much noise. Keep "Run now" plus a "⋯" dropdown (Edit, Duplicate, Delete). Clicking the row or the name opens Edit.
- Add sortable columns (priority, name, last run, next run) and a "Next run" column.
- Add a last-run outcome column (badge plus matched/applied counts).
- The schedule column shows the cron string twice-ish. Show the human text, with cron in a tooltip.
- Add bulk select: enable/disable/run/delete.
- Show a dry-run badge on rules that are in per-rule dry-run mode.
- Allow priority drag-to-reorder, or at least an inline number edit.
- Add filter chips: Enabled / Disabled / Dry-run / Failed last run.

## Rule editor
- Make it a sticky footer bar with Save / Cancel / Dry run, so the user doesn't scroll to the bottom. Warn on leaving with unsaved changes.
- Schedule: there's an empty gray bar under the cron field (preview placeholder?). Hide it until Preview is clicked, or auto-preview the next 5 run times live as the user types.
- Condition editor: the SQL editor is a fixed, tall, mostly empty box. Auto-size it. Add autocomplete for field-reference keys (qbittorrent.*.<col>, storage.<name>.*, etc.) and the helper functions. The "Field reference" button appears twice; keep one, and open it as a searchable side drawer.
- "Validate" result: show it inline under the editor (green check / red error with line/col).
- Dry run: show results in a modal or panel listing matched torrents (name, size, tags) and the actions that would apply, with a count.
- Actions: show the "Apply actions to" instance checkboxes as "All instances" (default) plus explicit selection; the current "leave every box unchecked = all" is unintuitive. Action rows need labels, a way to reorder, and a less jarring delete icon (outline, not solid red).
- Group the Basics fields better: the Dry-run checkbox floats awkwardly to the right. Put Enabled, Dry-run, and Stop-processing together as toggles.

## Instances
- "Test" result: show inline status (✓ 42ms / ✗ error message) and persist the last result with a timestamp.
- Mask the base URLs or show them in muted monospace. Add an "open in new tab" icon.
- Add a "Test all" button.
- Storage paths: show used/free space and the last scan time, and add a "Scan now" action.

## History
- Rows expand with a tiny chevron. Make the whole row clickable and the chevron bigger.
- Show expanded details as: matched torrents list, actions applied, errors, and the full log, with copy buttons.
- Add filters: "Only runs with applied > 0" and "Only failures". Quick date ranges (1h / 24h / 7d) next to the date pickers.
- Add pagination or infinite scroll with a total count. Make the table header sticky.
- Group repetitive identical runs (same rule, 0 applied), or let the user collapse them.
- Show Duration as a mini bar so slow runs stand out.

## SQL sandbox
- BUG: helper-function descriptions overflow the left card and render over the results table. Fix the wrapping (overflow-wrap:anywhere, and constrain the code samples).
- Tables tree: expanding a table shows its columns with types. Clicking a column inserts it. Clicking a table inserts "SELECT * FROM <t> LIMIT 50".
- Editor: add syntax highlighting plus autocomplete (same component as the rule editor), query history (last 20, localStorage), and saved snippets.
- Results: make the header sticky, columns resizable, and the grid scroll horizontally inside its card. Truncate long cells with an expand-on-click. Format byte columns (human size), ISO timestamps (local, relative), ratios (2 decimals), and hashes (short plus a copy button). Add row numbers, export to CSV/JSON, and a column hide/show menu.
- Show errors inline with the line/col highlighted.
- Add a "Use as rule condition" button that opens a new rule prefilled with the WHERE clause.

## Settings
- Put Global dry-run and Global kill switch in a "Danger zone" card with a clear on/off state. When either is on, show a persistent banner across all pages.
- Parallelism helper text ("Low=2, Medium=4…") belongs inside the dropdown options.
- Import: show a preview/diff of what will change before applying, then confirm.
- The Info card is fine. Add a "copy diagnostics" button.

## Deliverables
- Implement the changes, and list each file touched with a one-line why.
- Before/after screenshots for each page at 1280px and 375px.
- No regressions: all existing forms still submit, and keyboard shortcut Ctrl+Enter still runs SQL.
