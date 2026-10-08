# Rule condition: AdvancedSqlWhere → AdvancedSql (full query)

Replace the fixed prefix "SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash FROM qbittorrent t WHERE" with a full user-written query.

Contract:
- The query must return columns named exactly `instance_id` and `torrent_hash` (case-insensitive). Extra columns are allowed and ignored by the engine, but shown in dry-run output.
- Read-only and single-statement. SELECT or WITH…SELECT only. Reject INSERT/UPDATE/DELETE/DDL/PRAGMA/ATTACH and multiple statements (reuse the existing sandbox guard).
- The engine wraps it: SELECT DISTINCT instance_id, torrent_hash FROM ( <user sql> ). That dedupes the output and enforces the contract.
- The same helper functions (days_since, size_gb, path_matches, fuzzy_match, fuzzy_score, regexp) and the qbittorrent.*.<col> key syntax stay available.

Validation (Validate button + on save):
- Parse, then run with LIMIT 0 to check the columns. Error if instance_id/torrent_hash are missing: "Query must return columns instance_id and torrent_hash; got: <cols>".
- Warn (non-blocking) if a returned instance_id/hash pair doesn't exist in qbittorrent.
- Show the row count plus the first 20 rows inline.

Migration:
- Rename the field/column AdvancedSqlWhere → AdvancedSql. On migration, convert existing rules to:
  SELECT DISTINCT t.instance_id AS instance_id, t.hash AS torrent_hash
  FROM qbittorrent t
  WHERE <old where clause>
- Keep reading the old field from config imports (JSON/YAML) and auto-convert. Exports use the new field.

UI:
- Remove the gray read-only prefix bar. Prefill new rules with the template above (editable).
- The helper text states the contract: "Must return instance_id, torrent_hash".
- Add a "Test in SQL sandbox" button that opens /Sandbox with the query loaded. The sandbox gets a "Use as rule" button that prefills a new rule's AdvancedSql.
- Basic mode still generates SQL. "Switch to advanced" shows the full generated query.
