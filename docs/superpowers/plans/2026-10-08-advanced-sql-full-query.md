# AdvancedSql Full-Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace a rule's advanced-SQL WHERE clause with a complete user-written query returning `instance_id, torrent_hash`, migrating existing rules and keeping old config files importable.

**Architecture:** Most of the engine already exists. `AdvancedSqlExecutor` has a `FullQuery` mode with a `LIMIT 0` column probe, but rules only ever use `WhereClause` mode. This plan deletes the WHERE-clause mode and makes full-query the only path. The engine wraps every query in `SELECT DISTINCT instance_id, torrent_hash FROM (…)`. The shape guard moves to a shared `SqlGuard`, which the sandbox uses too. The column rename ships as an EF Core migration with a data `UPDATE`.

**Tech Stack:** .NET 9, EF Core 9 + SQLite, Microsoft.Data.Sqlite, xUnit, Razor Pages + htmx + CodeMirror 5.65.16.

**Spec:** `docs/superpowers/specs/2026-10-08-advanced-sql-full-query.md`

## Global Constraints

- Required output columns: `instance_id`, `torrent_hash`, matched case-insensitively. Extra columns are ignored by the engine.
- Engine wrapper, exact shape: `SELECT DISTINCT instance_id, torrent_hash FROM (\n<user sql>\n)`. The newlines are mandatory (see Review Focus 1).
- Missing-column error text, verbatim: `Query must return columns instance_id and torrent_hash; got: <cols>`. `<cols>` is a comma-plus-space list in query order.
- Template / migration text, verbatim (3 lines, `\n`-separated):
  `SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash` / `FROM qbittorrent t` / `WHERE <old where clause>`.
- Editor helper text, verbatim: `Must return instance_id, torrent_hash`.
- Only SELECT or WITH…SELECT. Reject INSERT/UPDATE/DELETE/DDL/PRAGMA/ATTACH and multiple statements.
- Field keys (`qbittorrent.*.size_gb`, `jellystat.*.play_count`, …) keep working. They expand against the torrent alias **`t`**, so a query using keys must have `qbittorrent t` in scope.
- Exports write `AdvancedSql` only. Imports accept `AdvancedSql` or the legacy `AdvancedSqlWhere`.
- Validation preview shows at most 20 rows.

## Review Focus

1. **User SQL ending in a `-- comment`.** It must still run. Without the newline before `)` in the wrapper, the comment swallows the closing paren. Test in Task 3.
2. **Keywords inside string literals or comments,** e.g. `t.name LIKE '%update%'`, `t.category = 'Delete Me'`, and the SQL function `replace(...)`. These must pass. Today's regex denylist rejects all three. Test in Task 1.
3. **Field keys without a `t` alias,** e.g. `SELECT … FROM qbittorrent WHERE qbittorrent.*.size_gb > 1`. This should fail with a message saying keys need `qbittorrent t`, not SQLite's bare `no such column: t.size_bytes`. Test in Task 3.
4. **NULL `torrent_hash` or a text `instance_id`** in the user's rows. Those rows are skipped and nothing crashes. `ExecuteAsync` currently calls `GetInt32`/`GetString` unconditionally. Test in Task 3.
5. **Rules with `UseAdvancedSql = false` and a blank or stale `AdvancedSqlWhere`.** The migration converts only non-blank text and never flips `UseAdvancedSql`, so basic rules behave identically after upgrade. Test in Task 4.

---

## File Structure

| File | Responsibility |
|---|---|
| Create `src/TorrentRuler.Core/Domain/AdvancedSqlTemplate.cs` | The template text and the legacy-WHERE → full-query conversion. In Core because Infrastructure (config import) can't see Engine. |
| Create `src/TorrentRuler.Engine/Conditions/AdvancedSql/SqlGuard.cs` | Single-statement / SELECT-only / denylist check, ignoring literals and comments. Shared by rules and the sandbox. |
| Create `src/TorrentRuler.Engine/Conditions/AdvancedSql/AdvancedSqlPreview.cs` | Result of a validation run: columns, first 20 rows, pair count, unknown-pair count. |
| Modify `AdvancedSqlExecutor.cs`, `SourceFieldExpander.cs`, delete `AdvancedSqlMode.cs` | Full-query only, wrapper, preview, NULL-safe execution. |
| Modify `Rule.cs`, `RuleDraft` in `RulePreview.cs`, `RuleRunner.cs` | Rename to `AdvancedSql`; dry-run carries the query preview. |
| Create migration `RenameAdvancedSqlWhereToAdvancedSql` | Rename the column and convert the data. |
| Modify `ConfigDtos.cs`, `ConfigPortabilityService.cs` | Write the new field, read both. |
| Modify `CompiledQuery.cs` | `ToDisplaySql()` inlines parameters so basic mode can show its full query. |
| Modify `Pages/Rules/Edit.cshtml(.cs)`, `_DryRunResult.cshtml`, create `_AdvancedSqlPreview.cshtml`, `rule-editor.js` | Editor UI. |
| Modify `Pages/Sandbox/Index.cshtml(.cs)`, `Pages/Rules/Index.cshtml.cs` | Sandbox ↔ rule round-trip; duplicate copies the new field. |

---

### Task 1: SqlGuard

**Files:**
- Create: `src/TorrentRuler.Engine/Conditions/AdvancedSql/SqlGuard.cs`
- Modify: `src/TorrentRuler.Engine/Conditions/AdvancedSql/AdvancedSqlExecutor.cs` (delete `ValidateShape`, `ForbiddenKeywordPattern`)
- Modify: `src/TorrentRuler.Web/Pages/Sandbox/Index.cshtml.cs` (delete `AllowedStart`; call the guard)
- Test: `src/TorrentRuler.Tests/Engine/SqlGuardTests.cs`

**Interfaces:**
- Produces: `public static class SqlGuard { public static string? Check(string sql, bool allowExplain = false); }`. Returns null when OK, else a user-facing message. Tolerates one trailing `;`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("SELECT 1")]
[InlineData("  with x AS (SELECT 1) SELECT * FROM x")]
[InlineData("SELECT * FROM qbittorrent t WHERE t.name LIKE '%update%'")]
[InlineData("SELECT replace(t.name, '.', ' ') FROM qbittorrent t")]
[InlineData("SELECT 1 -- ; DROP TABLE x\n")]
[InlineData("SELECT 1 /* DELETE */;")]
public void Check_Allows(string sql) => Assert.Null(SqlGuard.Check(sql));

[Theory]
[InlineData("", "SQL cannot be empty.")]
[InlineData("SELECT 1; SELECT 2", "Only a single statement is allowed.")]
[InlineData("UPDATE qbittorrent SET name = 'x'", "Only SELECT or WITH … SELECT queries are allowed.")]
[InlineData("WITH x AS (SELECT 1) DELETE FROM qbittorrent", "'DELETE' is not allowed.")]
[InlineData("SELECT * FROM pragma_table_info('x') WHERE 1 AND (PRAGMA)", "'PRAGMA' is not allowed.")]
[InlineData("EXPLAIN SELECT 1", "Only SELECT or WITH … SELECT queries are allowed.")]
public void Check_Rejects(string sql, string message) => Assert.Equal(message, SqlGuard.Check(sql));

[Fact]
public void Check_AllowsExplain_WhenAsked() => Assert.Null(SqlGuard.Check("EXPLAIN QUERY PLAN SELECT 1", allowExplain: true));
```

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet test src/TorrentRuler.Tests --filter "FullyQualifiedName~SqlGuardTests"`
Expected: build error, `SqlGuard` does not exist.

- [ ] **Step 3: Implement `SqlGuard.Check`**

Blank out string literals, quoted identifiers and comments first, then check the remaining text: `;` count after trimming one trailing `;`, the leading keyword (`SELECT|WITH`, plus `EXPLAIN` when allowed), and the denylist `ATTACH|DETACH|PRAGMA|VACUUM|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|REINDEX`, plus `REPLACE` only when followed by `INTO`. `pragma_table_info(` is a function call, not the `PRAGMA` statement: match `\bPRAGMA\b` not followed by `_`. The literal/comment scan is the one `SourceFieldExpander.Expand` already does; extract it into `SqlGuard` as `internal static string MaskLiteralsAndComments(string sql)` and reuse it there if that keeps the expander simpler, but don't change the expander's output.

- [ ] **Step 4: Point both callers at the guard**

`AdvancedSqlExecutor.Validate` calls `SqlGuard.Check(rawSql)`. The sandbox calls `SqlGuard.Check(sql, allowExplain: true)` and shows the message as its `Error`.

- [ ] **Step 5: Run all tests**

Run: `dotnet test src/TorrentRuler.Tests --filter "Category!=Live"`
Expected: PASS. Existing `AdvancedSqlExecutorTests` that asserted the old messages are updated to the new text.

- [ ] **Step 6: Commit** — `feat(sql): shared SqlGuard that ignores keywords in literals and comments`

---

### Task 2: Template + legacy conversion in Core

**Files:**
- Create: `src/TorrentRuler.Core/Domain/AdvancedSqlTemplate.cs`
- Test: `src/TorrentRuler.Tests/Engine/AdvancedSqlTemplateTests.cs`

**Interfaces:**
- Produces: `public static class AdvancedSqlTemplate { public const string Header; public const string NewRule; public static string? FromLegacyWhere(string? where); }`
  - `Header` = `"SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE"`.
  - `NewRule` = `Header + " 1 = 1"`. The editable default for new rules has to be a valid query.
  - `FromLegacyWhere`: null/blank → returns its input unchanged; otherwise `Header + " " + where.Trim()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void FromLegacyWhere_PrependsHeader() =>
    Assert.Equal("SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE t.ratio > 2",
                 AdvancedSqlTemplate.FromLegacyWhere("  t.ratio > 2 "));

[Theory, InlineData(null), InlineData(""), InlineData("   ")]
public void FromLegacyWhere_LeavesBlankAlone(string? where) => Assert.Equal(where, AdvancedSqlTemplate.FromLegacyWhere(where));

[Fact] public void NewRule_EndsWithAnAlwaysTrueWhere() =>
    Assert.EndsWith("WHERE 1 = 1", AdvancedSqlTemplate.NewRule);
```

- [ ] **Step 2: Run, see the failure; implement; run, see it pass.**

Run: `dotnet test src/TorrentRuler.Tests --filter "FullyQualifiedName~AdvancedSqlTemplateTests"`

- [ ] **Step 3: Commit** — `feat(sql): AdvancedSqlTemplate for new rules and legacy conversion`

---

### Task 3: Full-query executor + preview

**Files:**
- Delete: `src/TorrentRuler.Engine/Conditions/AdvancedSql/AdvancedSqlMode.cs`
- Create: `src/TorrentRuler.Engine/Conditions/AdvancedSql/AdvancedSqlPreview.cs`
- Modify: `AdvancedSqlExecutor.cs`, `SourceFieldExpander.cs`
- Test: `src/TorrentRuler.Tests/Engine/AdvancedSqlExecutorTests.cs` (rewrite the WhereClause cases as full queries)

**Interfaces:**
- Consumes: `SqlGuard.Check` (Task 1), `AdvancedSqlTemplate` (Task 2).
- Produces:
  - `AdvancedSqlValidationResult Validate(SnapshotDatabase snapshot, string sql, FieldResolutionContext resolution)`. On success, `CompiledSql` is the wrapped query. A one-argument-fewer overload uses `FieldResolutionContext.Lenient`.
  - `Task<AdvancedSqlPreview> PreviewAsync(SnapshotDatabase snapshot, string sql, FieldResolutionContext resolution, CancellationToken ct = default)`.
  - `public sealed record AdvancedSqlPreview(bool IsValid, string? Error, IReadOnlyList<string> Columns, IReadOnlyList<object?[]> SampleRows, int PairCount, int UnknownPairCount) { public const int SampleSize = 20; }`.
  - `SourceFieldExpander.Expand(string rawSql, FieldResolutionContext resolution)`, with the mode parameter removed.

- [ ] **Step 1: Write the failing tests** (snapshot seeded with two torrents `(1,'aaa')`, `(1,'bbb')` using the file's existing fixture helper)

```csharp
[Fact] public void Validate_WrapsAndDedupes()
// "SELECT t.instance_id, t.hash AS torrent_hash, t.name FROM qbittorrent t UNION ALL SELECT t.instance_id, t.hash, t.name FROM qbittorrent t"
// → ExecuteAsync returns exactly 2 matches.

[Fact] public void Validate_TrailingLineComment_StillRuns()
// "SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t -- all of them" → IsValid, 2 matches.

[Fact] public void Validate_WithCte_Runs()
// "WITH big AS (SELECT * FROM qbittorrent) SELECT instance_id, hash AS TORRENT_HASH FROM big" → IsValid (case-insensitive columns).

[Fact] public void Validate_MissingColumns_ListsWhatItGot()
// "SELECT t.hash, t.name FROM qbittorrent t"
// → ErrorMessage == "Query must return columns instance_id and torrent_hash; got: hash, name"

[Fact] public void Validate_FieldKeyWithoutAliasT_ExplainsAlias()
// "SELECT instance_id, hash AS torrent_hash FROM qbittorrent WHERE qbittorrent.*.size_gb > 0"
// → ErrorMessage contains "qbittorrent t"

[Fact] public void Validate_FieldKeysExpandAgainstT()
// "SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t WHERE qbittorrent.*.size_gb >= 0" → 2 matches.

[Fact] public async Task Execute_SkipsRowsWithNullHashOrNonIntegerInstance()
// "SELECT t.instance_id, t.hash AS torrent_hash FROM qbittorrent t UNION ALL SELECT 1, NULL UNION ALL SELECT 'x', 'ccc'" → 2 matches, no throw.

[Fact] public void Validate_NewRuleTemplate_IsValid()
// AdvancedSqlTemplate.NewRule → IsValid on an empty SnapshotDatabase.

[Fact] public async Task Preview_ReportsColumnsSampleCountsAndUnknownPairs()
// "SELECT t.instance_id, t.hash AS torrent_hash, t.name FROM qbittorrent t UNION ALL SELECT 9, 'zzz', 'ghost'"
// → Columns == ["instance_id","torrent_hash","name"], SampleRows.Count == 3, PairCount == 3, UnknownPairCount == 1.
```

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet test src/TorrentRuler.Tests --filter "FullyQualifiedName~AdvancedSqlExecutorTests"`
Expected: FAIL / compile errors on the removed mode parameter.

- [ ] **Step 3: Implement the executor**

- Delete `WhereClausePrefix` and the mode branches.
- `Validate`: run the guard, expand, then wrap as `SELECT DISTINCT instance_id, torrent_hash FROM (\n{sql}\n)`. Probe the **unwrapped** query with `SELECT * FROM (\n{sql}\n) LIMIT 0` for the column check and its `got:` list, then `EXPLAIN QUERY PLAN` the wrapped query.
- When SQLite reports `no such column: t.` and the original SQL contained a field key (the expander reports whether it expanded any key; return that from `Expand` via an `out bool` or a tuple), append: ` Field keys refer to the torrent as alias t, so the query needs FROM qbittorrent t.`
- `ExecuteAsync`: skip a row when `torrent_hash` is NULL or `instance_id` isn't an integer (`reader.GetFieldType` / `long` check).
- `PreviewAsync`: validate. Read up to 20 rows of the unwrapped query (all columns, NULLs as null). `PairCount` = `COUNT(*)` over the wrapped query. `UnknownPairCount` = count of wrapped pairs with no `qbittorrent` row (`LEFT JOIN qbittorrent q ON q.instance_id = w.instance_id AND q.hash = w.torrent_hash WHERE q.hash IS NULL`). Same timeout as `ExecuteAsync`.
- Expander: drop the mode parameter and the `mode != WhereClause` early return. Every key expands against alias `t`.

- [ ] **Step 4: Keep the callers compiling (interim)**

`RuleRunner.EvaluateAsync` and `Edit.cshtml.cs` (save + validate handlers) still hold a WHERE clause until Task 4. Pass `AdvancedSqlTemplate.FromLegacyWhere(where)!` to the new `Validate`, so behavior is unchanged. Task 4 removes this conversion.

- [ ] **Step 5: Run all tests**

Run: `dotnet test src/TorrentRuler.Tests --filter "Category!=Live"`
Expected: PASS.

- [ ] **Step 6: Commit** — `feat(sql): advanced conditions are full queries wrapped in SELECT DISTINCT`

---

### Task 4: Rename the field + migration

**Files:**
- Modify: `src/TorrentRuler.Core/Domain/Rule.cs` (`AdvancedSqlWhere` → `AdvancedSql`, doc comment says full query)
- Modify: `src/TorrentRuler.Engine/RulePreview.cs` (`RuleDraft.AdvancedSqlWhere` → `AdvancedSql`)
- Modify: `src/TorrentRuler.Engine/RuleRunner.cs` (`EvaluateAsync` uses `Validate(snapshot, sql, resolution)`)
- Modify: `src/TorrentRuler.Web/Pages/Rules/Index.cshtml.cs:65` (duplicate copies `AdvancedSql`)
- Create: `src/TorrentRuler.Infrastructure/Persistence/Migrations/<timestamp>_RenameAdvancedSqlWhereToAdvancedSql.cs` (generated with `dotnet ef migrations add RenameAdvancedSqlWhereToAdvancedSql -p src/TorrentRuler.Infrastructure -s src/TorrentRuler.Web`)
- Test: `src/TorrentRuler.Tests/Infrastructure/AdvancedSqlMigrationTests.cs`

**Interfaces:**
- Consumes: `AdvancedSqlTemplate.Header` text (Task 2). Copy it into the migration as a literal: a migration must not change if the constant later does. Remove Task 3's interim `FromLegacyWhere` calls in `RuleRunner` and `Edit.cshtml.cs`; the stored value is now the full query.
- Produces: `Rule.AdvancedSql` (string?), `RuleDraft.AdvancedSql`.

- [ ] **Step 1: Write the failing test**

Create a temp-file SQLite `AppDbContext`. Migrate to `RemoveStreamystatsInstances` with `context.GetService<IMigrator>().Migrate("…_RemoveStreamystatsInstances")`. Insert three rows with raw SQL: (A) `UseAdvancedSql=1, AdvancedSqlWhere='t.ratio > 2'`, (B) `UseAdvancedSql=0, AdvancedSqlWhere='   '`, (C) `UseAdvancedSql=0, AdvancedSqlWhere=NULL`. Migrate to latest, then assert:
- A: `AdvancedSql == "SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash\nFROM qbittorrent t\nWHERE t.ratio > 2"`, `UseAdvancedSql` true.
- B: `AdvancedSql == "   "`, `UseAdvancedSql` false.
- C: null, false.

- [ ] **Step 2: Run, see it fail**

Run: `dotnet test src/TorrentRuler.Tests --filter "FullyQualifiedName~AdvancedSqlMigrationTests"`

- [ ] **Step 3: Rename the property, generate the migration, then hand-edit it**

Keep the generated `RenameColumn`. After it, add `migrationBuilder.Sql("UPDATE Rules SET AdvancedSql = 'SELECT DISTINCT … ' || char(10) || 'FROM qbittorrent t' || char(10) || 'WHERE ' || trim(AdvancedSql) WHERE AdvancedSql IS NOT NULL AND trim(AdvancedSql) <> '';")`. `Down` strips that exact header with `substr` when it's present, then renames back. Fix every compile error the rename causes.

- [ ] **Step 4: Run all tests**

Run: `dotnet test src/TorrentRuler.Tests --filter "Category!=Live"`
Expected: PASS.

- [ ] **Step 5: Commit** — `feat(sql): rename AdvancedSqlWhere to AdvancedSql and migrate existing rules`

---

### Task 5: Config import/export back-compat

**Files:**
- Modify: `src/TorrentRuler.Infrastructure/Config/ConfigDtos.cs` (`RuleDto`)
- Modify: `src/TorrentRuler.Infrastructure/Config/ConfigPortabilityService.cs` (export ~136, import ~167/185)
- Modify: `src/TorrentRuler.Infrastructure/Config/ConfigSerializer.cs` if needed to omit nulls
- Test: `src/TorrentRuler.Tests/Infrastructure/RuleConfigCompatTests.cs`

**Interfaces:**
- Produces: `RuleDto.AdvancedSql` (string?) and `RuleDto.AdvancedSqlWhere` (string?, legacy, read on import, never written). Add `public string? EffectiveAdvancedSql => AdvancedSql ?? AdvancedSqlTemplate.FromLegacyWhere(AdvancedSqlWhere);`, ignored by both serializers.

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
public async Task Import_LegacyWhere_IsConvertedToFullQuery(ConfigFormat format)
// content with one rule carrying only AdvancedSqlWhere = "t.ratio > 2" (JSON key / YAML key as each serializer names it)
// → stored Rule.AdvancedSql == AdvancedSqlTemplate.FromLegacyWhere("t.ratio > 2")

[Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
public async Task Import_NewFieldWins_WhenBothPresent(ConfigFormat format)

[Theory, InlineData(ConfigFormat.Json), InlineData(ConfigFormat.Yaml)]
public async Task Export_WritesAdvancedSql_AndNeverTheLegacyKey(ConfigFormat format)
// exported text contains the AdvancedSql key and does not contain "AdvancedSqlWhere" (case-insensitive)
```

Use an in-memory SQLite `AppDbContext` (`DataSource=:memory:`, open connection, `EnsureCreated`) and construct `ConfigPortabilityService` directly.

- [ ] **Step 2: Run, see the failure; implement; run all tests; see them pass.**

On export, set `AdvancedSqlWhere = null`. Make sure both serializers omit null for that property (JSON: `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`; YAML: `[YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]`). `ExampleRulesTests` must still pass unchanged.

- [ ] **Step 3: Commit** — `feat(config): export AdvancedSql, import legacy AdvancedSqlWhere`

---

### Task 6: Show basic mode's full query

**Files:**
- Modify: `src/TorrentRuler.Engine/Conditions/CompiledQuery.cs`
- Test: `src/TorrentRuler.Tests/Engine/ConditionSqlCompilerTests.cs` (add cases)

**Interfaces:**
- Produces: `public string ToDisplaySql()`. Returns `Sql` with every parameter placeholder replaced by a SQL literal: strings single-quoted with `'` doubled, numbers in invariant culture, bools as `1`/`0`, null as `NULL`. Replace longest names first, so `$p1` doesn't clobber `$p10`.

- [ ] **Step 1: Failing tests:**
  - A compiled tree with 11+ parameters, including a string containing `'`, gives `ToDisplaySql()` that passes `AdvancedSqlExecutor.Validate` and returns the same matches as `ConditionSqlCompiler.ExecuteAsync` on the same snapshot.
  - `$p1` / `$p10` replacement is correct.
- [ ] **Step 2: Implement; run tests; PASS.**
- [ ] **Step 3: Commit** — `feat(sql): CompiledQuery.ToDisplaySql inlines parameters`

---

### Task 7: Rule editor + sandbox UI

**Files:**
- Modify: `src/TorrentRuler.Web/Pages/Rules/Edit.cshtml:287-300`, `Edit.cshtml.cs` (OnGet, OnPostSave, OnPostValidateAdvancedSql, OnPostDryRun, OnPostPreviewCondition, `InputModel.AdvancedSql`)
- Create: `src/TorrentRuler.Web/Pages/Rules/_AdvancedSqlPreview.cshtml` (model `AdvancedSqlPreview`)
- Modify: `src/TorrentRuler.Web/Pages/Rules/_DryRunResult.cshtml`, `src/TorrentRuler.Engine/RulePreview.cs`, `RuleRunner.DryRunAsync`
- Modify: `src/TorrentRuler.Web/wwwroot/js/rule-editor.js` (`initAdvancedSqlEditor` textarea id → `Input_AdvancedSql`; mode toggle)
- Modify: `src/TorrentRuler.Web/Pages/Sandbox/Index.cshtml(.cs)`

**Interfaces:**
- Consumes: `AdvancedSqlTemplate.NewRule` (Task 2); `PreviewAsync`, `AdvancedSqlPreview` (Task 3); `ToDisplaySql` (Task 6).
- Produces:
  - `RulePreview.Query` (`AdvancedSqlPreview?`), set only for advanced drafts.
  - `GET /Rules/Edit?sql=<text>`: a new rule with `UseAdvancedSql=true`, `AdvancedSql=sql`.
  - `GET /Sandbox?sql=<text>`: prefills the box without running it.

Steps (UI; verify each by running the app, see the Verify step):

- [ ] **Step 1: Editor markup.** Delete the `<pre class="qf-sql-preview">` prefix bar. Reword the existing `#advancedSqlColumnsHint` (keep the id; the textarea's `aria-describedby` points at it) so it starts with `Must return instance_id, torrent_hash`, followed by "Extra columns are allowed and shown in dry runs." Drop the "fixed start" sentence. New rules (OnGet with no id and no `sql`) get `AdvancedSql = AdvancedSqlTemplate.NewRule`.
- [ ] **Step 2: Validate.** `OnPostValidateAdvancedSqlAsync` builds the real snapshot (`ruleRunner.BuildSandboxSnapshotAsync`), not `new SnapshotDatabase()`, so the preview has data. It returns `Partial("_AdvancedSqlPreview", preview)`. The partial shows:
  - a green check with "`{PairCount}` torrent(s) match"
  - a yellow non-blocking warning when `UnknownPairCount > 0`: "`{n}` returned pair(s) don't match any torrent in qBittorrent"
  - a scrollable table of `SampleRows` with `Columns` headers
  - on error, a red alert with the message, with a 400 status kept so htmx error styling still applies

  Save keeps using `Validate` (blocking errors only).
- [ ] **Step 3: Dry run.** `DryRunAsync` fills `RulePreview.Query` for advanced drafts. `_DryRunResult` renders the same sample table, so extra columns show in dry-run output.
- [ ] **Step 4: Switch basic → SQL.** The toggle in `rule-editor.js` posts to `?handler=PreviewCondition`, which now returns `compiled.ToDisplaySql()`. If the SQL box is empty or still equals `NewRule`, the response replaces its content (via `editor.setValue`). Otherwise it asks with `confirm("Replace your SQL with the query generated from the basic builder?")`. Switching SQL → basic never touches the tree.
- [ ] **Step 5: Round-trip buttons.** The editor gets a "Test in SQL sandbox" link, opened in a new tab to `/Sandbox?sql=` + `encodeURIComponent(current SQL)` at click time. The sandbox gets a "Use as rule" link to `/Rules/Edit?sql=` + `encodeURIComponent(box value)`. Both are built in JS at click time from the live editor value.
- [ ] **Step 6: Verify.** `dotnet run --project src/TorrentRuler.Web`, then:
  - A new rule shows the template and no prefix bar.
  - Validate shows count, warning and rows.
  - Save works, and Dry run shows extra columns.
  - Basic → SQL shows the generated query.
  - Test in sandbox opens the query; Use as rule opens a new rule with it.
  - A pre-existing advanced rule (from before the migration) loads, validates and saves.
  - Ctrl+Enter still runs the sandbox.
- [ ] **Step 7: Run all tests; PASS. Commit** — `feat(rules): full-query advanced SQL editor with preview and sandbox round-trip`
