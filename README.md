# LightSharp

A performance-focused proof-of-concept C#/.NET editor for macOS and other desktop platforms. It deliberately avoids Electron, Node.js, and an extension host.

## Current milestone

- Open a workspace folder
- Lazy UI with ignored build/cache directories
- File tree
- Responsive text editor with line numbers
- Open and save files
- Run `dotnet build`, `dotnet run`, and `dotnet test`
- Stop an active command
- Asynchronous stdout/stderr capture
- Built-in process and managed-memory diagnostics
- Avalonia 12 desktop UI and AvaloniaEdit

## Prerequisites

- .NET 10 SDK
- macOS, Windows, or Linux

## Run

```bash
dotnet restore
dotnet run --project src/LightSharp/LightSharp.csproj
```

## Build

```bash
dotnet build LightSharp.slnx
```

## Architecture

- `WorkspaceService`: asynchronous workspace traversal with common build/cache folders excluded
- `DotNetService`: cancellable external `dotnet` execution
- `MainWindow`: minimal shell for file browsing, editing, command output, and diagnostics
- `AvaloniaEdit`: virtualized editor surface and line numbers

## Known limitations

This first milestone intentionally does not yet include Roslyn semantic services, tabs, compiler diagnostic navigation, debugger/DAP, Git UI, or AI. The next milestone should add Roslyn diagnostics incrementally, without analyzing the entire repository after every keystroke.

## Performance principles

- no Electron or embedded browser
- no extension host
- no Node.js
- asynchronous filesystem and process work
- no polling of the workspace
- expensive language analysis will be cancellable and incremental
