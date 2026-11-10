# LightSharp architecture boundaries for coding agents

The [README](../README.md) lists current services and commands. Check the actual implementation before moving responsibilities: UI extraction is still evolving.

| Layer | Ownership | Rule |
| --- | --- | --- |
| `src/LightSharp/MainWindow.axaml*` | Application shell, layout and composition | Avoid adding editor, Roslyn or document recovery algorithms |
| Focused Views / Controllers / ViewModels | Editor workspace, panels, navigation, settings and interactions | Own their UI behavior and unsubscribe handlers on dispose |
| `src/LightSharp/Services/` | Workspace/project tree, Roslyn completion/navigation, .NET commands and Git integration | Avoid referencing direct UI controls; make long operations cancellable |
| `src/LightSharp/Styles/` | Shared appearance, editor themes and highlighting | Keep visual resources out of code-behind |
| Tests | Behavioral contracts for document safety, navigation and keymap | Reproduce failures with deterministic cases before fixes where possible |

## Invariants
- Dirty editor buffers must remain intact when navigating, reloading, recovering or saving.
- External edits must not be silently overwritten; conflict decisions require explicit user choice.
- Recovery backups survive cancellation and failed exports; never discard drafts without approval.
- Roslyn requests must reject stale results after document/workspace switches.
- Keymap configuration is authoritative; shortcut hints must stay synchronized.
- Avoid Electron/Node or heavyweight extension infrastructure; keep the IDE lean.
