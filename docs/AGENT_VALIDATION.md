# Agent validation and PR evidence

## General
1. Read the linked Issue and acceptance criteria before modifying code.
2. Capture the baseline (branch, relevant failing test or reproduction).
3. Run focused tests/checks for touched code first.
4. Run the repository's normal build/test CI checks before claiming readiness; link the workflow run.
5. In the PR record: changed areas, test commands and results, manual checks with PASS/FAIL/NOT TESTED, limitations, and remaining risks.
6. If environment, SDK, assets or device is unavailable, state NOT TESTED rather than claiming success.
7. If a command fails twice for the same reason, stop and report diagnostics instead of looping.
8. Never include access tokens, signed stream URLs, secrets or personal information in logs or PR descriptions.
9. Require owner review before merge, release, deployment or cross-project changes.

## LightSharp-specific commands
```sh
dotnet restore LightSharp.slnx
dotnet build LightSharp.slnx --configuration Release --no-restore
dotnet test LightSharp.slnx --configuration Release --no-build
```
- CI runs these on Windows, Ubuntu and macOS. Record each platform result from the PR checks.
- For Roslyn work include multi-file navigation, stale/cancelled requests and dirty-buffer preservation cases where applicable.
- For file save/recovery work include changed/deleted files, conflicting external edits, cancelled dialogs and restart flows.
- For keyboard/UI work include keymap overrides and a manual Avalonia check; build success does not imply functioning UI.
- If a desktop runtime or OS is unavailable, mark that manual run NOT TESTED.
