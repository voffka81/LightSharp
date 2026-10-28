# C# Completion Design

## Goal

Add project-aware C# completion to LightSharp while preserving its lightweight Avalonia desktop shell and existing editor.

## Approved Approach

Use Roslyn for semantic suggestions and AvaloniaEdit's existing completion-window UI. Load the selected `.csproj` lazily through `MSBuildWorkspace`; map the active file to its Roslyn `Document`, overlay the editor's current text, and request completions at the caret. Trigger suggestions on identifier input and `.`, and support `Ctrl+Space`. When project loading or semantic completion is unavailable, fall back to identifier words collected from open documents.

## Interaction

- Completion popup follows the caret and filters as the user types.
- Up/down changes selection; Enter or Tab accepts; Escape dismisses.
- Completion applies Roslyn's text change to the active document and updates the editor caret.
- Switching projects/documents cancels pending work and changes completion context.
- Completion runs asynchronously with cancellation so stale results do not replace newer results or block typing.

## Boundaries

- Scope is C# completion only; XML/XAML highlighting remains unchanged.
- No debugger, signature-help, diagnostics, or full language-server implementation.
- A project that cannot load must not prevent editing; show a concise status and use word fallback.
- No Microsoft branding or proprietary Visual Studio assets are used.

## Validation

- Build the solution with no warnings/errors.
- Verify project context loads for a supported SDK-style project.
- Verify manual invocation and member completion after `.`.
- Verify accept, dismiss, stale-request cancellation, and word fallback.
