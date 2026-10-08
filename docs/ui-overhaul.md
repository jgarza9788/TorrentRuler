# UI/UX overhaul — change list

Spec: `docs/superpowers/specs/2026-10-08-ui-ux-overhaul.md` · Plan: `docs/superpowers/plans/2026-10-08-ui-ux-overhaul.md`

Screenshots are in `docs/screenshots/before/` and `docs/screenshots/after/`, named `<page>-<width>.png`, at 1280px and 375px. Every page was also checked at 768px. The "after" set was captured with the overflow check in fail mode, and no page scrolls sideways at any of the three widths (`after/overflow.txt`). To recapture, see `tools/screenshots/README.md`.

| Page | Before | After |
|---|---|---|
| Dashboard | `before/dashboard-1280.png` · `before/dashboard-375.png` | `after/dashboard-1280.png` · `after/dashboard-375.png` |
| Rules | `before/rules-1280.png` · `before/rules-375.png` | `after/rules-1280.png` · `after/rules-375.png` |
| Rule editor | `before/rule-edit-1280.png` · `before/rule-edit-375.png` | `after/rule-edit-1280.png` · `after/rule-edit-375.png` |
| Instances | `before/instances-1280.png` · `before/instances-375.png` | `after/instances-1280.png` · `after/instances-375.png` |
| History | `before/history-1280.png` · `before/history-375.png` | `after/history-1280.png` · `after/history-375.png` |
| SQL sandbox | `before/sandbox-1280.png` · `before/sandbox-375.png` | `after/sandbox-1280.png` · `after/sandbox-375.png` |
| Settings | `before/settings-1280.png` · `before/settings-375.png` | `after/settings-1280.png` · `after/settings-375.png` |

The before set overflowed sideways in two places: the rule editor at 375px, and the sandbox at 375px and 768px.

## Files touched

### Shared (all pages)
- `wwwroot/css/site.css` — Raises body text to 14.5px and table headers to 12px, and lifts muted text to 8.7:1 contrast in dark mode and 7.2:1 in light. Adds focus rings and helpers for form width, table scrolling and sticky headers. Adds styles for empty states, toasts, busy buttons and every page's new parts. Removes the old 1320px cap on all content; only form pages are now capped (1400px, centred).
- `wwwroot/js/site.js` — Toasts (including ones sent by htmx responses), the confirm dialog, busy buttons, clickable rows and tooltips. Copy buttons, with a fallback when the browser blocks the clipboard. Sends the anti-forgery token with every htmx request.
- `Pages/Shared/_Layout.cshtml` — A labelled theme menu replaces the three icon buttons. Hosts the toasts, the confirm dialog and the global dry-run/kill-switch banner. Lets form pages opt into the width cap.
- `Pages/Shared/_ToastHost.cshtml`, `_ConfirmModal.cshtml` — The shared toast stack and the delete-confirmation dialog.
- `Pages/Shared/_EmptyState.cshtml`, `EmptyStateModel.cs` — A reusable empty state with an icon, a title, a line of help and a primary action.
- `Pages/Shared/GlobalModeBannerViewComponent.cs`, `Components/GlobalModeBanner/Default.cshtml` — A banner on every page while the global dry run or kill switch is on.
- `Toasts.cs` — The one way a page handler raises a toast, whether after a redirect or in an htmx response.
- `Format.cs` — Human-readable byte sizes and "in N min" times.
- `wwwroot/js/sql-editor.js` — The SQL editor shared by the rule editor and the sandbox. It grows with the query, autocompletes, runs on Ctrl+Enter and marks error positions.
- `wwwroot/lib/codemirror/addon/hint/show-hint.min.{js,css}` — The CodeMirror autocomplete add-on, version 5.65.16 to match the existing CodeMirror.

### Dashboard
- `Pages/Index.cshtml`, `Index.cshtml.cs` — Stat cards read "N of M … enabled". The matches card now shows unique torrents matched (total matches underneath). New "actions applied" card. Recent runs are highlighted when something was applied or failed, and link to their rules. Instances show a status dot. Adds the activity sparkline and the next scheduled runs.
- `Pages/DashboardStats.cs` — The last-24-hours figures and the hourly activity data.
- `Pages/Shared/_Sparkline.cshtml` — Matched and applied per hour, drawn as plain SVG.
- `Pages/Shared/_InstanceStatus.cshtml` — An instance's last connection test (dot, latency, when), shared with the Instances page.

### Rules list
- `Pages/Rules/Index.cshtml`, `Index.cshtml.cs` — Run now plus a "⋯" menu per row; clicking a row opens it. Sortable columns, filter chips and a bulk bar (enable, disable, run, delete). Inline priority editing, a next-run column, the last run's outcome, a dry-run badge, and undo after deleting a rule.
- `Pages/Rules/RuleListQuery.cs` — Sorting and filtering.
- `Pages/Rules/_LastRunCell.cshtml` — The last-run outcome badge and counts.
- `Pages/Rules/_EnabledTag.cshtml` — The Enabled/Disabled tag in the Status column (and on mobile cards). It is also the quick toggle; toggling keeps the current sort and filter.

### Rule editor
- `Pages/Rules/Edit.cshtml`, `Edit.cshtml.cs` — A save bar that stays on screen, and a warning when leaving with unsaved changes. The three behaviour switches sit together. Live schedule preview of the next 5 runs. One "Field reference" button, and the drawer fits phone screens. Dry run opens in a dialog. "All qBittorrent instances" is the default target; saving no longer requires picking one. Actions are numbered, can be reordered, and have a quieter delete button.
- `Pages/Rules/_SchedulePreview.cshtml` — The next runs, or why the schedule is invalid.
- `Pages/Rules/_DryRunResult.cshtml` — Lists matched torrents by name, size and tags.
- `wwwroot/js/rule-editor.js` — Uses the shared SQL editor, marks Validate errors, reorders actions, and guards unsaved changes.
- `Engine/PreviewTorrentLookup.cs`, `Engine/RulePreview.cs`, `Engine/RuleRunner.cs` — Dry runs now return the matched torrents' details instead of bare hashes.

### Instances
- `Pages/Instances/Index.cshtml`, `Index.cshtml.cs` — Saved test results shown as status, latency and time. Test all. Base URLs in muted monospace with an "open in new tab" link. Disk usage per storage path, last scan time, and Scan now.
- `Pages/Instances/InstanceConnectionTester.cs` — Runs a connection test and saves the result.
- `Pages/Instances/_StorageUsage.cshtml`, `_FolderSize.cshtml` — The used/free bar and the folder-size cell.
- `Core/Domain/Instance.cs`, migration `AddInstanceLastTest` — Four new columns for the last test result.
- `Sources/Storage/IStorageUsageService.cs`, `StorageUsageService.cs` — A `force` option so Scan now ignores the cached size.
- `Program.cs` — Registers the connection tester.

### History
- `Pages/History/Index.cshtml`, `Index.cshtml.cs` — Click anywhere on a row (or press Enter or Space) to expand it, with a larger chevron. Applied-only and failures-only filters, 1h / 24h / 7d buttons, 50 runs per page with a total count, and a sticky header. Repeated quiet runs are grouped and can be expanded. Duration bars.
- `Pages/History/HistoryQuery.cs` — Filtering, paging and grouping.
- `Pages/History/_RunDetails.cshtml` — A run's torrents, actions, errors and raw details, with copy buttons.

### SQL sandbox
- `Pages/Sandbox/Index.cshtml`, `Index.cshtml.cs` — Fixes helper text spilling over the results. The tables tree shows column types; click a column to insert it, or a table to query it. History and Snippets menus, and the shared editor.
- `wwwroot/js/sandbox.js` — The results grid: row numbers, sticky header, resizable columns, long cells that expand on click, and formatting for sizes, timestamps, ratios and hashes. Export to CSV or JSON, show or hide columns, history and snippets saved in the browser, and inline error positions.

### Settings
- `Pages/Settings/Index.cshtml`, `Index.cshtml.cs` — A Danger zone card with clear ON/OFF badges, and a confirmation before turning the kill switch on. Parallelism figures moved into the dropdown options. Imports show a preview first, then Apply. Copy diagnostics.
- `Infrastructure/Config/ConfigPortabilityService.Preview.cs`, `IConfigPortabilityService.cs`, `ConfigPortabilityService.cs` — Works out what an import would add, update or leave alone, without writing anything.
- `Pages/Settings/EditPathMapping.cshtml(.cs)` — Form width cap and a save toast.

### Other forms
- `Pages/Instances/Edit.cshtml(.cs)`, `EditStoragePath.cshtml(.cs)`, `Account/ChangePassword.cshtml(.cs)` — Form width cap and save toasts.
- `Engine/Scheduling/RuleSchedulerService.cs` — `NextRunAt`, so the dashboard and Rules list compute next runs exactly as the scheduler does.

### Tests (new or extended)
- `Tests/Web/ToastsTests.cs`, `FormatTests.cs`, `DashboardStatsTests.cs`, `RuleListQueryTests.cs`, `RulesIndexHandlerTests.cs`, `RuleEditSaveTests.cs`, `InstanceConnectionTesterTests.cs`, `HistoryQueryTests.cs`, `SandboxModelTests.cs`
- `Tests/Engine/PreviewTorrentLookupTests.cs`, `RuleSchedulerServiceTests.cs`
- `Tests/Infrastructure/ImportPreviewTests.cs`
- `Tests/Sources/StorageUsageServiceTests.cs`

### Tooling (dev only, not shipped)
- `tools/screenshots/{package.json,package-lock.json,seed.mjs,shoot.mjs,README.md}`, `.gitignore` — Playwright 1.48.2. Seeds a throwaway instance, captures every page, and fails on sideways scroll.
