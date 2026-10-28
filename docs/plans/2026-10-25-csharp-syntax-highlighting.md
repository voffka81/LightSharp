# C# Syntax Highlighting Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable C# syntax highlighting in LightSharp's existing AvaloniaEdit editor without adding a package.

**Architecture:** Use the C# definition already embedded in AvaloniaEdit. Select it when opening a `.cs` document and clear it for other document types; keep the existing editor instance and document/tab behavior.

**Tech Stack:** C# / .NET 10, Avalonia 12, AvaloniaEdit.

---

## Task 1: Select the C# definition per document

1. Import AvaloniaEdit's highlighting API in `MainWindow.axaml.cs`.
2. In document activation, select the bundled `C#` definition for `.cs` paths and clear syntax highlighting for all other paths.
3. Build `LightSharp.slnx` and confirm zero warnings and errors.
