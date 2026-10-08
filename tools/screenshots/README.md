# Screenshot harness

Dev-only tooling (not shipped with the app) that seeds a throwaway TorrentRuler and
captures every main page, failing if any page scrolls sideways.

```powershell
cd tools/screenshots
npm ci
npx playwright install chromium

# 1. Run the app against an EMPTY temp data dir -- never a real install.
$env:TORRENTRULER_DATA_DIR = "$env:TEMP\tr-shots"; Remove-Item -Recurse -Force $env:TORRENTRULER_DATA_DIR -ErrorAction SilentlyContinue
dotnet run --project ../../src/TorrentRuler.Web --urls http://localhost:5199

# 2. In another shell: first-run setup, example rules, fake instances, two runs, dark theme.
node seed.mjs --instances --runs

# 3. Capture. Checks 375/768/1280 for overflow, saves 375 and 1280.
node shoot.mjs --out ../../docs/screenshots/after
```

`--no-fail` reports overflow without failing. `seed.mjs` logs in as `admin` with
`TR_SEED_PASSWORD` (default `TorrentRuler!2026`).
