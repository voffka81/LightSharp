# LightSharp Fluent IDE Shell Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Redesign the existing LightSharp Avalonia window as a compact, editor-first .NET IDE inspired by Fluent desktop tooling.

**Architecture:** Keep the current Avalonia application, `WorkspaceService`, and `DotNetService`. Build the shell in `MainWindow.axaml`, keep lightweight open-document state in the existing window/model layer, and wire menus only to capabilities the app already supports. Preserve ignored-directory filtering and avoid adding a new UI framework or product branding.

**Tech Stack:** C# / .NET 10, Avalonia 12, AvaloniaEdit.

---

## Approved Design

- Use a restrained Fluent palette, compact spacing, thin dividers, and the editor as the primary surface.
- Create a compact menu row for File, Edit, View, Git, Build, Debug, and Test; expose only existing behavior in functional commands, without inventing Git or debugger features.
- Add a short command row for Start, startup project selection, Build, Stop, and Test.
- Rename the file pane to Solution Explorer and distinguish solution/project files, folders, C# files, and other files while preserving the current ignored-directory list.
- Add document tabs with close controls and per-document text state; retain open/save behavior.
- Keep a lower Output/Tests/Problems strip and a dense status row for editor position, target framework, and existing memory diagnostics.

## Implementation Tasks

1. Update `src/LightSharp/MainWindow.axaml` with the compact menu, command, explorer, document-tab, editor, output, and status layout plus local Fluent styling.
2. Update `src/LightSharp/MainWindow.axaml.cs` to wire supported menu/toolbar actions, populate startup projects from the already-loaded tree, and manage multiple open documents and active selection.
3. Add only the minimal document state needed for tabs; preserve `WorkspaceService` ignored directories and use the existing `DotNetService` for commands.
4. Run `rtk dotnet build LightSharp.slnx`; correct any Avalonia XAML or C# diagnostics, then run the app if the environment permits.

## Validation

- The solution builds with zero errors.
- Existing workspace traversal exclusions remain `.git`, `bin`, `obj`, `node_modules`, `.idea`, and `.vs`.
- Opening two files keeps both tabs and switching tabs restores each document's text.
- Save, Build, Run, Test, and Stop remain wired to the existing supported operations.