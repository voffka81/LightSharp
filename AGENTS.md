# LightSharp agent instructions

LightSharp is a .NET 10 / Avalonia 12 desktop C# IDE using Roslyn. Start with [README.md](README.md), [architecture](docs/AGENT_ARCHITECTURE.md) and [validation](docs/AGENT_VALIDATION.md).

## Boundaries
- Keep `MainWindow` as the shell / composition root rather than accumulating feature logic.
- Place editor, navigation, document recovery, Roslyn and Git behavior in focused services/controllers and UI views.
- Keep key bindings configurable through the existing keymap service; don't hardcode new shortcuts in views.
- Preserve dirty buffers and recovery backups; never silently overwrite external file edits.
- Keep UI operations responsive, cancellable and asynchronous; don't add polling for workspace changes without explicit justification.
## Agent workflow
- Work only on the explicitly assigned GitHub Issue; discuss unclear requirements rather than inventing a new feature.
- Do not initiate, delegate to, or spawn another coding agent without explicit owner approval.
- Keep changes in a dedicated branch and submit a PR; never merge, deploy, publish releases, or modify repository settings without explicit approval.
- Do not silently alter unrelated files. Preserve existing architecture; document necessary deviations in the PR.
- Run the smallest relevant checks first, then required CI. Do not repeatedly rerun identical failing commands without changing the underlying cause.
- After two unsuccessful repair attempts, stop and report the failing command, relevant log excerpt, files changed, and likely root cause.
- Report which checks actually ran and which did not. A passing build does not prove visual or physical-device behavior.

## Verification
```sh
dotnet restore LightSharp.slnx
dotnet build LightSharp.slnx --configuration Release --no-restore
dotnet test LightSharp.slnx --configuration Release --no-build
```
- Existing CI builds and tests on Windows, Linux and macOS. UI runtime testing must be reported separately from builds.
