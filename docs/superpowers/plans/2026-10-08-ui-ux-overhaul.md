# UI/UX Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every TorrentRuler page readable, responsive and self-explanatory in dark mode, with feedback, confirmation and empty states, without changing the stack or rule behavior.

**Architecture:** First, a shared front-end foundation: CSS tokens, toasts, a confirm modal, busy buttons, empty-state and table-wrap partials, and a reusable SQL editor module. Then one task per page that uses it, with each page committed separately. Where a page needs data the backend doesn't keep yet (last connection test, unique matches, dry-run torrent details, import diff), the backend change ships in that page's task with PageModel/service tests. Pure markup is verified by a Playwright screenshot run at 375/768/1280px.

**Tech Stack:** .NET 9 Razor Pages, Tabler (Bootstrap 5) CSS, htmx, Alpine.js, CodeMirror 5.65.16 (vendored under `wwwroot/lib`), EF Core 9 + SQLite, xUnit; Playwright (Node, `tools/screenshots`) for screenshots only.

**Spec:** `docs/superpowers/specs/2026-10-08-ui-ux-overhaul.md`

**Depends on:** `docs/superpowers/plans/2026-10-08-advanced-sql-full-query.md` must be done first. The spec's sandbox "Use as rule condition … prefilled with the WHERE clause" is superseded by that plan's Task 7 (full query, `/Rules/Edit?sql=`). This plan doesn't redo it.

## Global Constraints

- Base font size 14.5px (spec: 14–15px). Muted text and table headers ≥ 4.5:1 contrast on their actual background in **both** themes.
- Form pages are capped at `max-width: 1400px`; tables can stay full width.
- Breakpoints to verify: 375px, 768px, 1280px. The page never scrolls horizontally; wide tables scroll inside their card.
- Theme switching: one labelled dropdown (Light / Dark / Match system), replacing the three bare icon buttons.
- Every delete goes through the shared confirm modal, which names the item. Undo is offered only for **rule** delete; other deletes confirm only.
- Toasts appear for save, run, test and import, on both success and error.
- No new CDN or package dependency in the web app. New front-end code is vanilla JS + Alpine + htmx. The CodeMirror `show-hint` addon (5.65.16, matching the vendored core) is vendored into `wwwroot/lib/codemirror/addon/hint/`.
- Ctrl+Enter runs SQL in the sandbox (and still works once CodeMirror is mounted).
- Commit messages: one commit per task, `ui(<page>): …`.
- Final deliverable: `docs/ui-overhaul.md` listing every file touched with a one-line reason, plus `docs/screenshots/{before,after}/<page>-<width>.png` for 1280 and 375.

## Review Focus

1. **Submit buttons that carry a `name`/`asp-page-handler`/`formaction`.** Disabling the button before the browser collects form data drops the handler, and the post silently hits the wrong handler. The busy state must apply after submit dispatch. Test in Task 2.
2. **The unsaved-changes warning firing on the editor's own Save, Dry run or Validate,** or after an htmx swap re-renders fields. The warning should fire only on navigation away with real edits. Test in Task 5.
3. **375px width with CodeMirror, long hashes, file paths and the History details panel.** These are the content most likely to force page-level horizontal scroll. Test via the Task 1 screenshot run's overflow check, in Task 10.
4. **Undo after the toast is gone, a reload, or two deletes in a row.** Undo must restore the most recent deleted rule exactly once and fail politely ("Nothing to undo") otherwise. Test in Task 4.
5. **localStorage unavailable** (private window, blocked storage). The sandbox history and snippets must degrade to "not saved" without breaking the page. Test in Task 8.

---

## File Structure

| File | Responsibility |
|---|---|
| `wwwroot/css/site.css` | Tokens, typography, contrast, focus rings, `.qf-form-container`, `.qf-table-wrap`, sticky headers, empty state, toast/confirm styles. |
| `wwwroot/js/site.js` | `qfToast`, confirm modal wiring, busy-button handling, htmx `HX-Trigger` → toast bridge, theme dropdown. |
| Create `wwwroot/js/sql-editor.js` | `qfMountSqlEditor(textarea, {hints, onRun})`: CodeMirror mount + autosize + autocomplete + theme sync. Used by the rule editor and the sandbox. |
| Create `Pages/Shared/_EmptyState.cshtml` + `EmptyStateModel` | Icon, title, text, primary action. |
| Create `Pages/Shared/_ToastHost.cshtml`, `_ConfirmModal.cshtml` | Rendered once in the layout. |
| Create `Pages/Shared/GlobalModeBannerViewComponent.cs` + view | Persistent dry-run / kill-switch banner. |
| Create `src/TorrentRuler.Web/Toasts.cs` | `TempData` + `HX-Trigger` helpers so every handler raises toasts one way. |
| Page files per task | As listed in each task. |
| Create `tools/screenshots/{package.json,shoot.mjs,seed.mjs}` | Seeds a throwaway data dir, logs in, captures every page × width, fails on horizontal overflow. |

---

### Task 1: Screenshot harness + "before" set

**Files:**
- Create: `tools/screenshots/package.json` (pin `playwright` to `1.48.2`), `tools/screenshots/seed.mjs`, `tools/screenshots/shoot.mjs`, `tools/screenshots/README.md`
- Create: `docs/screenshots/before/*.png`

**Interfaces:**
- Produces: `node tools/screenshots/shoot.mjs --out docs/screenshots/<set> [--widths 375,768,1280]`. It expects the app at `http://localhost:5199`, run with `TORRENTRULER_DATA_DIR` set to a temp dir. It writes `<page>-<width>.png` for pages `dashboard, rules, rule-edit, instances, history, sandbox, settings`. It exits non-zero and lists the page and width when `document.documentElement.scrollWidth > innerWidth`.
- `seed.mjs` completes `/Setup` (admin/admin-password-from-env), imports `examples/example-rules.json` through Settings, and adds one fake instance of each type plus one storage path. Some run history must exist, so it also triggers "Run now" on two rules.

- [ ] **Step 1:** Write the three scripts. In the README, document `npm ci && npx playwright install chromium`, the app launch command (`$env:TORRENTRULER_DATA_DIR=...; dotnet run --project src/TorrentRuler.Web --urls http://localhost:5199`), and the seed and shoot commands.
- [ ] **Step 2:** Run seed and shoot against the current code, `--out docs/screenshots/before`, with the overflow check in report-only mode (`--no-fail`). Expected: 14 PNGs at 375 and 1280 (768 is checked, not saved), plus a printed overflow report. Save the report to `docs/screenshots/before/overflow.txt`.
- [ ] **Step 3: Commit** — `chore(ui): screenshot harness and before screenshots`

---

### Task 2: Global foundation

**Files:**
- Modify: `wwwroot/css/site.css`, `wwwroot/js/site.js`, `Pages/Shared/_Layout.cshtml`
- Create: `Pages/Shared/_EmptyState.cshtml`, `Pages/Shared/EmptyStateModel.cs`, `Pages/Shared/_ToastHost.cshtml`, `Pages/Shared/_ConfirmModal.cshtml`, `src/TorrentRuler.Web/Toasts.cs`
- Test: `src/TorrentRuler.Tests/Web/ToastsTests.cs`

**Interfaces:**
- Produces (server): `public static class Toasts { public static void Add(ITempDataDictionary tempData, ToastKind kind, string message, string? undoHandlerUrl = null); public static void Trigger(HttpResponse response, ToastKind kind, string message); }` with `public enum ToastKind { Success, Error, Info }`.
  - `Add` stores a JSON list under TempData key `qf.toasts` (it appends, so two calls both show).
  - `Trigger` sets or merges the `HX-Trigger` header: `{"qf-toast":[{"kind":"success","message":"…"}]}`.
- Produces (client):
  - `window.qfToast(kind, message, opts?: {undoUrl?: string})`.
  - `data-confirm="<text naming the item>"` on any `<form>` or submit button opens the confirm modal and submits only on confirm.
  - `[data-busy]` / every submit button and every htmx trigger gets `disabled` + `.qf-busy` spinner while its request is in flight, and is restored on `htmx:afterRequest`, `pageshow` and error.
- Produces (markup): `<partial name="_EmptyState" model="new EmptyStateModel(icon, title, text, actionText, actionUrl)" />`; wrapper class `.qf-table-wrap` (`overflow-x:auto` inside the card); `.qf-form-container` (`max-width:1400px; margin-inline:auto`); `.qf-sticky-head thead th { position: sticky; top: 0 }`.

- [ ] **Step 1: Failing tests for `Toasts`**
  - `Add` twice → TempData JSON holds two toasts in order.
  - `Trigger` when `HX-Trigger` already holds `{"other":1}` → both keys survive.
  - `Trigger` twice → `qf-toast` array has two entries.
- [ ] **Step 2:** Run `dotnet test src/TorrentRuler.Tests --filter "FullyQualifiedName~ToastsTests"`. Expected: FAIL. Implement `Toasts.cs`. Run again. Expected: PASS.
- [ ] **Step 3: CSS.**
  - Base `font-size: 14.5px`. Table text matches body size, not `small`.
  - Override `--tblr-secondary-color` / `--tblr-muted` (and the `text-muted`/`text-secondary` utility colors) in `[data-bs-theme=dark]` and light, using values that measure ≥ 4.5:1 against `--tblr-body-bg` and the card bg. Record the measured ratios in a CSS comment.
  - `thead th`: normal case, 0.8125rem minimum, weight 600, readable color.
  - Visible `:focus-visible` outline (2px, the theme's primary color) on links, buttons and inputs.
- [ ] **Step 4: Layout.**
  - Navbar becomes `navbar-expand-lg` with a toggler `<button aria-label="Toggle navigation">`.
  - The theme picker becomes a dropdown with icon + text items "Light", "Dark", "Match system", the current one checked. It keeps the existing theme persistence code path in `site.js`.
  - Render `_ToastHost` (reads `qf.toasts` from TempData into a `<script type="application/json">`, shown on load) and `_ConfirmModal`.
  - Wrap `@RenderBody()` in a container that pages opt into `.qf-form-container` via `ViewData["FormPage"] = true`.
- [ ] **Step 5: site.js.**
  - `qfToast` using Bootstrap's Toast. Toasts stack bottom-right (bottom-center under 576px). The Undo button POSTs `undoUrl` with the antiforgery token from the layout's hidden form, then reloads.
  - An `htmx:afterRequest` listener reads `qf-toast` events. An `htmx:responseError` listener raises an error toast with the response text, capped at 300 chars.
  - Confirm modal: intercept `submit` (capture phase) for forms/submitters with `data-confirm`, then re-submit via `form.requestSubmit(submitter)` after confirm.
  - Busy buttons: listen for `submit` (bubble phase) and apply `disabled` in `setTimeout(…, 0)`, so the submitter's name/value is already in the form data set. htmx: `htmx:beforeRequest` / `htmx:afterRequest` on `evt.detail.elt`.
- [ ] **Step 6: Accessibility sweep of the layout.** Every icon-only button/link in `_Layout.cshtml` gets an `aria-label`.
- [ ] **Step 7: Verify.** Run the app.
  - Submit the Settings form: it saves, and a toast appears. Proves Review Focus 1: the handler name survived the busy state.
  - Confirm modal on an existing delete, cancel and confirm.
  - Navbar collapses at 375px.
  - Run `shoot.mjs --out docs/screenshots/wip`: no overflow on the layout itself.
- [ ] **Step 8: Commit** — `ui(global): typography, contrast, toasts, confirm modal, busy buttons, responsive nav`

---

### Task 3: Dashboard

**Files:**
- Modify: `Pages/Index.cshtml`, `Pages/Index.cshtml.cs`
- Create: `src/TorrentRuler.Web/Pages/DashboardStats.cs` (pure computation)
- Test: `src/TorrentRuler.Tests/Web/DashboardStatsTests.cs`

**Interfaces:**
- Consumes: `Instance.LastTest*` fields from Task 6. If Task 6 isn't done yet, show the dot as grey "never tested". Don't block on it.
- Produces: `public static DashboardStats Compute(IReadOnlyList<RunRecord> runsLast24h, DateTimeOffset now)`, which returns:
  - `MatchesTotal`
  - `UniqueTorrentsMatched`: distinct `(InstanceId, TorrentHash)` across the runs' `DetailsJson` (`List<ActionResult>`). Bad or null JSON is skipped.
  - `AppliedTotal`: sum of `ActionsExecutedCount`.
  - `IReadOnlyList<HourBucket> Hourly`, with `record HourBucket(DateTimeOffset Hour, int Matched, int Applied)`: 24 buckets, oldest first, zero-filled.

- [ ] **Step 1: Failing tests.**
  - Two runs whose details share one hash plus one unique → `UniqueTorrentsMatched == 2`.
  - A run with null `DetailsJson` doesn't throw.
  - A run at `now - 30min` lands in the last bucket.
  - The buckets count is 24.
- [ ] **Step 2:** Implement; tests PASS.
- [ ] **Step 3: Markup.**
  - Stat cards read "`{enabled}` of `{total}` instances enabled" and "`{enabled}` of `{total}` rules enabled", each wrapped in a link to its page.
  - The "Matched, last 24h" card becomes three figures: "Matches (24h, across `{n}` runs)", "Unique torrents matched", "Actions applied (24h)".
  - Recent runs: the row gets `.table-success`-tinted class when Applied > 0, `.table-danger` when Failed > 0. The rule name links to `/Rules/Edit?id=`.
  - Instances panel: a dot (green ok / red failed / grey never) + latency ms + relative time, replacing the "on" badge. `aria-label` carries the status text.
  - Lower half: an inline SVG sparkline (two polylines, matched and applied, with a legend and per-point `<title>` tooltips; no chart library) and a "Next scheduled runs" list. That list shows the next 5 enabled rules by next occurrence, computed with `CronValidator.GetNextOccurrences(cron, tz, LastRunAt ?? now, 1)`.
  - Empty states: no runs → `_EmptyState` with "Create a rule" → `/Rules/Edit`.
- [ ] **Step 4: Verify** at 375/1280 via `shoot.mjs`.
- [ ] **Step 5: Commit** — `ui(dashboard): readable stats, unique matches, sparkline, next runs`

---

### Task 4: Rules list

**Files:**
- Modify: `Pages/Rules/Index.cshtml`, `Pages/Rules/Index.cshtml.cs`
- Create: `src/TorrentRuler.Web/Pages/Rules/RuleListQuery.cs`
- Test: `src/TorrentRuler.Tests/Web/RuleListQueryTests.cs`, `src/TorrentRuler.Tests/Web/RulesIndexHandlerTests.cs`

**Interfaces:**
- Produces:
  - `record RuleRow(Rule Rule, string ScheduleText, DateTimeOffset? NextRun, RunRecord? LastRun)`.
  - `static IReadOnlyList<RuleRow> RuleListQuery.Apply(IEnumerable<RuleRow> rows, string? sort, bool desc, string? filter)`:
    - `sort` ∈ `priority` (default, then Id) | `name` | `lastRun` | `nextRun`. Nulls sort last in both directions.
    - `filter` ∈ `enabled` | `disabled` | `dryrun` | `failed` (last run outcome is Failure or PartialFailure) | null.
  - Handlers:
    - `OnPostBulkAsync(string op, int[] ids)`, with `op` ∈ `enable|disable|run|delete`.
    - `OnPostSetPriorityAsync(int id, int priority)`: htmx, returns 204 + toast.
    - `OnPostRestoreDeletedAsync()`.
  - Delete stores the deleted rule's JSON in TempData key `qf.undo.rule`, and its toast has `undoUrl = ?handler=RestoreDeleted`.

- [ ] **Step 1: Failing tests.**
  - Sort by `nextRun` desc puts nulls last.
  - `filter=failed` keeps only rules whose last run failed.
  - Bulk `disable` with 2 ids disables exactly those.
  - Bulk `delete` removes them.
  - Restore after delete re-creates the rule with the same Name/Cron/Condition/Actions and clears `qf.undo.rule`.
  - A second restore returns the "Nothing to undo" toast.
  - Delete A then delete B, then restore → B is restored.

  Use in-memory SQLite `AppDbContext` and a `TempDataDictionary` with a dictionary-backed provider.
- [ ] **Step 2:** Implement; tests PASS.
- [ ] **Step 3: Markup.**
  - Columns: checkbox, Priority (inline `<input type=number>` that htmx-posts on change), Name (link; a row click opens Edit except on interactive children), Schedule (human text, with cron in `title` + `data-bs-toggle="tooltip"`), Next run, Last run (outcome badge + "`{matched}` matched · `{applied}` applied"), and badges for Disabled / Dry run.
  - Actions: a "Run now" button + a `⋯` dropdown (`aria-label="More actions for {name}"`) with Edit / Duplicate / Delete (`data-confirm="Delete rule '{name}'?"`).
  - Sortable headers are links with `aria-sort`.
  - Filter chips are links toggling `?filter=`.
  - A bulk bar appears when any box is checked. It holds Enable / Disable / Run / Delete; Delete uses confirm naming the count.
  - Empty state.
  - Under 768px the table becomes stacked cards via CSS (`.qf-stack-sm`: `display:block` rows with `data-label` captions).
- [ ] **Step 4: Verify** at 375/1280, including bulk + undo flow by hand.
- [ ] **Step 5: Commit** — `ui(rules): row actions menu, sorting, filters, bulk ops, undo delete`

---

### Task 5: Rule editor

**Files:**
- Modify: `Pages/Rules/Edit.cshtml`, `Pages/Rules/Edit.cshtml.cs`, `Pages/Rules/_DryRunResult.cshtml`, `wwwroot/js/rule-editor.js`
- Create: `wwwroot/js/sql-editor.js`, `wwwroot/lib/codemirror/addon/hint/show-hint.min.js`, `show-hint.min.css`
- Modify: `src/TorrentRuler.Engine/RulePreview.cs`, `src/TorrentRuler.Engine/RuleRunner.cs` (DryRunAsync)
- Test: `src/TorrentRuler.Tests/Engine/RuleDryRunTests.cs` (existing dry-run tests, or extend `ConditionEvaluationIntegrationTests`)

**Interfaces:**
- Produces:
  - `RulePreview.SampleMatches`: `IReadOnlyList<PreviewTorrent>` (first 50), with `record PreviewTorrent(string InstanceName, string Hash, string Name, long SizeBytes, IReadOnlyList<string> Tags)`. It replaces `SampleMatchedHashes`; update its callers.
  - `qfMountSqlEditor(textarea: HTMLTextAreaElement, opts: { hints: string[], onRun?: () => void, minLines?: number }) → CodeMirror.Editor`. It autosizes between `minLines` (default 6) and 30 lines, autocompletes on `Ctrl-Space` and after typing `.` or 2+ identifier chars, binds `Ctrl-Enter`/`Cmd-Enter` to `onRun`, and syncs the theme.
  - Hint list = every `<type>.*.<field>` and `<type>.<instance>.<field>` key + helper signatures. Generate it from the page's existing `qfSourceCatalog` / `qfUdfHelpers` globals.
  - `qfMarkSqlError(editor, message: string) → {line, col} | null`, also in `sql-editor.js`. If `message` contains `near "<token>"`, it marks the first occurrence of `<token>` (`markText`, class `.qf-sql-error`) and returns its 1-based line/col. Otherwise it clears marks and returns null.

- [ ] **Step 1: Failing test.** A dry run of a draft matching one torrent returns `SampleMatches[0]` with that torrent's name, size and tags from the snapshot.
- [ ] **Step 2:** Implement; test PASS.
- [ ] **Step 3: Editor markup.**
  - A sticky footer bar (`position: sticky; bottom: 0`) with Save (primary), Dry run, Cancel (link back to `/Rules`). Remove the bottom buttons it replaces.
  - Dirty tracking: snapshot `new FormData(form)` serialized on load, and compare on `beforeunload`. Skip the comparison when the unload is caused by the Save submit (a flag set in the submit handler). htmx Validate / Dry run requests don't navigate, so they never trigger it.
  - Basics: Enabled / Dry run / Stop processing lower-priority rules as three `form-switch` toggles in one row, with help text under each.
  - Schedule: delete the empty preview bar. The cron and timezone inputs `hx-post="?handler=PreviewSchedule"`, `hx-trigger="input changed delay:400ms"`, into a target that's hidden until the first response. `OnPostPreviewSchedule` returns 5 next runs (was 3).
- [ ] **Step 4: Condition area.**
  - Mount the SQL box through `qfMountSqlEditor`.
  - Keep exactly one "Field reference" button. It opens the existing reference panel as a Bootstrap `offcanvas-end` drawer with a search input that filters rows by key/description (Alpine `x-model` filter over the existing data).
  - Validate result renders inline under the editor (plan 1's `_AdvancedSqlPreview`).
  - On error, call `qfMarkSqlError` and show "line L, col C" when it returns a position.
- [ ] **Step 5: Dry run** renders `_DryRunResult` inside a Bootstrap modal. The modal shows a count heading, a table of `SampleMatches` (instance, name, human size, tag badges), the action lines with would-change/already/failed counts, and "showing 50 of N" when truncated.
- [ ] **Step 6: Actions.**
  - "Apply actions to": radio **All qBittorrent instances** (default; posts an empty list exactly as today) / **Selected instances** (reveals the checkboxes; at least one is required client-side). This needs no backend change: the empty list still means all.
  - Each action row gets a visible label ("Action 1 · Add tags"), ↑/↓ buttons (`aria-label="Move action up"`) that reorder the JSON array, and an outline `btn-outline-danger` trash icon with `aria-label="Remove action"`.
- [ ] **Step 7: Verify.**
  - Edit, then click a nav link → warned. Edit + Save → not warned.
  - Autocomplete offers `jellystat.*.play_count` and `days_since(`.
  - Ctrl+Enter in the rule editor doesn't submit the form (only the sandbox binds `onRun`).
  - 375px has no overflow.
- [ ] **Step 8: Commit** — `ui(rule-editor): sticky actions, live schedule preview, SQL autocomplete, dry-run modal`

---

### Task 6: Instances

**Files:**
- Modify: `src/TorrentRuler.Core/Domain/Instance.cs`, `Pages/Instances/Index.cshtml(.cs)`, `Pages/Instances/_ConnectionTestResult.cshtml`, `Pages/Instances/Edit.cshtml.cs` (its test handler persists too)
- Create: migration `AddInstanceLastTest`
- Test: `src/TorrentRuler.Tests/Web/InstancesIndexHandlerTests.cs`

**Interfaces:**
- Produces:
  - `Instance` gets four nullable properties: `LastTestedAt` (`DateTimeOffset?`), `LastTestSucceeded` (`bool?`), `LastTestLatencyMs` (`int?`), `LastTestMessage` (`string?`, truncated to 500 chars).
  - `OnPostTestAllAsync()` tests enabled instances in parallel (max 4), persists each, and returns the refreshed instances table partial + a summary toast "`{ok}` of `{n}` connected".
  - `OnPostScanStorageAsync(int id)` calls `IStorageUsageService.GetUsage` + `GetOrComputeFolderSizeAsync`, and returns the row partial.

- [ ] **Step 1: Failing tests.**
  - Test-connection on a stubbed adapter (success, 42ms) persists `LastTestSucceeded=true` and `LastTestLatencyMs=42`.
  - A failing adapter persists the message.
  - TestAll touches only enabled instances.
- [ ] **Step 2:** Add properties + migration (`dotnet ef migrations add AddInstanceLastTest -p src/TorrentRuler.Infrastructure -s src/TorrentRuler.Web`); implement; tests PASS.
- [ ] **Step 3: Markup.**
  - Inline status: "✓ 42 ms · 3 min ago" in green, or "✗ <message>" in red with the full text in a tooltip.
  - Base URL in `.font-monospace.text-secondary`, plus an `<a target="_blank" rel="noopener" aria-label="Open {name} in a new tab">` external-link icon.
  - A "Test all" button in the header.
  - Storage paths: a used/free bar (`progress`) with "`{free}` free of `{total}`", the folder size + "scanned `{relative}`", and a "Scan now" button.
  - Deletes use `data-confirm` naming the instance or path.
  - Empty states for both lists.
- [ ] **Step 4: Verify; Step 5: Commit** — `ui(instances): persisted test results, test all, storage usage and scan`

---

### Task 7: History

**Files:**
- Modify: `Pages/History/Index.cshtml`, `Pages/History/Index.cshtml.cs`
- Create: `src/TorrentRuler.Web/Pages/History/HistoryQuery.cs`
- Test: `src/TorrentRuler.Tests/Web/HistoryQueryTests.cs`

**Interfaces:**
- Produces:
  - `record HistoryFilter(int? RuleId, DateTimeOffset? From, DateTimeOffset? To, string? Range /* 1h|24h|7d */, bool AppliedOnly, bool FailuresOnly, int Page = 1, int PageSize = 50)`.
  - `IQueryable<RunRecord> HistoryQuery.Apply(IQueryable<RunRecord> q, HistoryFilter f, DateTimeOffset now)`. `Range` overrides From/To. `FailuresOnly` = Outcome is Failure/PartialFailure, or `ActionsFailedCount > 0`.
  - `IReadOnlyList<HistoryGroup> HistoryQuery.Group(IReadOnlyList<RunRecord> page)`: consecutive runs of the same rule with `ActionsExecutedCount == 0`, `ActionsFailedCount == 0`, the same Outcome and no error collapse into one group (`Count`, first/last time). Anything else is its own group of 1.

- [ ] **Step 1: Failing tests.**
  - `Range="1h"` excludes a run 2h old.
  - `AppliedOnly` and `FailuresOnly` filter correctly.
  - Paging returns page 2 of 120 rows = rows 51–100 with total 120.
  - `Group` collapses 3 identical zero-applied runs into one group of 3, but splits on a different rule or on an applied run.
- [ ] **Step 2:** Implement; tests PASS.
- [ ] **Step 3: Markup.**
  - The whole row is a `<button>`-driven disclosure: the row has `role="button"`, `tabindex=0`, Enter/Space toggle, and `aria-expanded`. The chevron is ~1.25rem.
  - Details sections:
    - Matched torrents: distinct hashes from `DetailsJson`, shown short + copy.
    - Actions applied: grouped by ActionType with outcome counts.
    - Errors: `ErrorMessage` + per-result errors.
    - Raw log: the pretty-printed `DetailsJson` in `<pre>` with a copy button. There is no separate per-run log; say so in a muted note.
  - Filters bar: checkboxes "Only runs with applied > 0" and "Only failures", plus 1h / 24h / 7d buttons next to the date pickers.
  - Sticky header; "Showing a–b of N" + pager.
  - Grouped rows show "×N similar runs", expandable.
  - Duration cell holds a mini bar whose width is relative to the slowest run on the page, with the ms text beside it.
- [ ] **Step 4: Verify; Step 5: Commit** — `ui(history): clickable rows, rich details, filters, paging, grouping, duration bars`

---

### Task 8: SQL sandbox

**Files:**
- Modify: `Pages/Sandbox/Index.cshtml`, `Pages/Sandbox/Index.cshtml.cs`
- Create: `wwwroot/js/sandbox.js`
- Test: `src/TorrentRuler.Tests/Web/SandboxModelTests.cs`

**Interfaces:**
- Consumes: `qfMountSqlEditor` (Task 5); plan 1's `SqlGuard`, `?sql=` prefill and "Use as rule".
- Produces:
  - `TableInfo` gains column types: `record ColumnInfo(string Name, string Type)`.
  - The page embeds results as `<script type="application/json" id="sandbox-results">{columns, rows}</script>`, and `sandbox.js` renders the grid from it.
  - localStorage keys `qf.sandbox.history` (array, max 20, newest first, de-duplicated) and `qf.sandbox.snippets` (array of `{name, sql}`).

- [ ] **Step 1: Failing test.** `TableInfo` for `qbittorrent` includes `("size_bytes","INTEGER")`. Run it against a real `SnapshotDatabase` through a test `IRuleRunner` stub that returns it.
- [ ] **Step 2:** Implement; test PASS.
- [ ] **Step 3: Overflow bug.** The helper list gets `overflow-wrap:anywhere`. `code` gets `white-space: normal; word-break: break-word`. The left column's card gets `min-width:0`. Verify the 1280 screenshot shows no overlap with the results.
- [ ] **Step 4: Tables tree.**
  - Each column renders as `name` + muted type, as a button inserting the column name.
  - The table name is a button inserting `SELECT * FROM <t> LIMIT 50` (replacing the editor contents when empty, else inserting at the cursor).
- [ ] **Step 5: Editor.**
  - `qfMountSqlEditor` with `onRun` = submit, so Ctrl+Enter is preserved.
  - History dropdown (last 20, pushed on submit).
  - Snippets dropdown with "Save current as…" (`prompt` for name) and delete.
  - All storage access is wrapped in try/catch. When storage throws, the dropdowns show "History isn't available in this browser" and nothing else breaks.
- [ ] **Step 6: Results grid (sandbox.js).**
  - Card-scoped horizontal scroll; sticky header.
  - Resizable columns (a drag handle on the `th` right edge).
  - A row-number column.
  - Cells over 120 chars are truncated with "…"; a click expands them in place.
  - Formatting by column name:
    - `*_bytes`, `size*` → human size (decimal GB/MB, like `size_gb`)
    - ISO-8601 strings → local time + relative in `title`
    - `ratio`, `*_percent`, `availability` → 2 decimals
    - `hash`, `torrent_hash` → first 8 chars + copy button
  - Formatting is display-only; export uses raw values.
  - Toolbar: Export CSV, Export JSON (Blob downloads), Columns menu (checkbox per column, hiding the `th` + cells).
- [ ] **Step 7: Errors inline.** Call `qfMarkSqlError` (Task 5) on the error message and show "line L, col C" next to the error.
- [ ] **Step 8: Verify.**
  - Ctrl+Enter runs.
  - Export files open correctly.
  - Private window: history shows the fallback text.
  - No overflow at 375.
- [ ] **Step 9: Commit** — `ui(sandbox): fix helper overflow, typed table tree, editor history/snippets, rich results grid`

---

### Task 9: Settings + global mode banner

**Files:**
- Modify: `Pages/Settings/Index.cshtml`, `Pages/Settings/Index.cshtml.cs`, `Pages/Shared/_Layout.cshtml`
- Create: `Pages/Shared/Components/GlobalModeBanner/Default.cshtml`, `src/TorrentRuler.Web/Pages/Shared/GlobalModeBannerViewComponent.cs`
- Modify: `src/TorrentRuler.Infrastructure/Config/IConfigPortabilityService.cs`, `ConfigPortabilityService.cs`
- Test: `src/TorrentRuler.Tests/Infrastructure/ImportPreviewTests.cs`

**Interfaces:**
- Produces:
  - `Task<ImportPreview> PreviewImportAsync(string content, ConfigFormat format, string kind /* "config" | "rules" */, CancellationToken ct)`.
  - `record ImportPreview(IReadOnlyList<ImportChange> Changes, string? Error)`.
  - `record ImportChange(string Section /* Rule, Instance, StoragePath, PathMapping, Settings */, string Name, ImportChangeKind Kind /* Add, Update, Unchanged */, IReadOnlyList<string> ChangedFields)`.
  - It never writes. The existing `Import*Async` stay as the apply step.

- [ ] **Step 1: Failing tests.**
  - Previewing an export of the current DB → all `Unchanged`.
  - A new rule name → `Add`.
  - A changed cron on an existing rule → `Update` with `ChangedFields == ["CronExpression"]`.
  - Malformed YAML → `Error` set, no throw.
  - The DB is untouched after preview: row counts and `UpdatedAt` are equal.
- [ ] **Step 2:** Implement; tests PASS.
- [ ] **Step 3: Import flow.**
  - Step one posts the file to `OnPostPreviewImportAsync`. It renders the diff table, with the uploaded text in a hidden `<textarea name="importContent">` (no server-side state).
  - "Apply import" posts `importContent` + format + kind to the existing import handler, changed to accept text as well as a file.
  - Toast on success or error.
- [ ] **Step 4: Danger zone.**
  - Global dry-run and kill switch move into a red-bordered "Danger zone" card. Each is a `form-switch` with an ON/OFF badge, a one-line consequence, and a confirm when turning the kill switch on.
  - The view component renders a persistent banner under the navbar on every page when either is on ("Global dry run is ON: no actions are applied" / "Kill switch is ON: no rules run"), linking to Settings.
- [ ] **Step 5: Parallelism and diagnostics.**
  - Parallelism `<option>` text becomes "Low (2 parallel requests)", "Medium (4…)" and so on, with the values taken from the existing helper text. Remove that helper text.
  - "Copy diagnostics" button: copies the Info card's key/value text + app version + counts of instances, rules and storage paths + the browser user agent.
- [ ] **Step 6: Verify; Step 7: Commit** — `ui(settings): danger zone, global banner, import preview, copy diagnostics`

---

### Task 10: Empty-state/a11y sweep, after screenshots, file list

**Files:**
- Modify: any page still missing `_EmptyState`, `aria-label`, or `.qf-table-wrap` (Login, Setup, ChangePassword, EditStoragePath, EditPathMapping, Instances/Edit)
- Create: `docs/screenshots/after/*.png`, `docs/ui-overhaul.md`

- [ ] **Step 1: Sweep.**
  - `rg -n '<button[^>]*>\s*<i class="ti' src/TorrentRuler.Web/Pages` → every hit has `aria-label`.
  - Every `<table` is inside `.qf-table-wrap`.
  - Every list page renders `_EmptyState` when empty. Check by visiting with a fresh data dir.
- [ ] **Step 2:** Run `shoot.mjs --out docs/screenshots/after` (overflow check in **fail** mode at 375/768/1280). Expected: exit 0, 14 PNGs.
- [ ] **Step 3: Regression pass.** Submit every form once by hand (Setup, Login, ChangePassword, instance add/edit/test/delete, storage path add/edit, rule save/dry-run/duplicate/run/delete/undo, path mapping add/delete, settings save/export/import), and press Ctrl+Enter in the sandbox. Run `dotnet test src/TorrentRuler.Tests --filter "Category!=Live"` → PASS.
- [ ] **Step 4:** Write `docs/ui-overhaul.md`: one line per touched file (from `git diff --stat master...HEAD`) with its reason, and before/after screenshot pairs per page.
- [ ] **Step 5: Commit** — `docs(ui): after screenshots and change list`
