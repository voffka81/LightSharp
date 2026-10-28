# C# Completion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add responsive project-aware C# completion to LightSharp.

**Architecture:** Load SDK-style project context with Roslyn `MSBuildWorkspace`, request completions from the active Roslyn document, and show them in AvaloniaEdit's completion popup. Keep the UI responsive with debouncing and cancellation; fall back to words from open documents if semantic context is unavailable.

**Tech Stack:** .NET 10, Avalonia 12, AvaloniaEdit 12, Microsoft.CodeAnalysis.CSharp.Workspaces, Microsoft.CodeAnalysis.Workspaces.MSBuild, Microsoft.Build.Locator.

---

## Task 1: Confirm AvaloniaEdit completion APIs and package compatibility

1. Inspect `CompletionWindow`, `ICompletionData`, and insertion APIs in the installed AvaloniaEdit 12 package.
2. Confirm Roslyn package versions compatible with the current target framework and restore them.
3. Build the solution.

## Task 2: Add project-context service

1. Register MSBuild with `Microsoft.Build.Locator` before creating a workspace.
2. Load the selected project asynchronously and index its documents by normalized file path.
3. Provide a cancellable operation to update an active document's source text and request Roslyn completions at a caret position.
4. Return a small neutral completion-result model so the UI does not depend on Roslyn types.

## Task 3: Add completion popup and fallback

1. Implement AvaloniaEdit completion items with display text, description, filtering, and insertion text changes.
2. Trigger asynchronous completion for `.` and identifier input; add `Ctrl+Space` invocation.
3. Cancel stale requests, preserve the caret, and apply the selected completion change.
4. When no project document is available, suggest distinct identifiers from currently open documents.

## Task 4: Wire lifecycle and verify

1. Load completion context when a workspace/project is selected and dispose it when replaced or the window closes.
2. Keep typing and editing functional if MSBuild or Roslyn project loading fails.
3. Build the solution and run focused smoke checks for semantic completion, fallback, acceptance, dismissal, and cancellation.
