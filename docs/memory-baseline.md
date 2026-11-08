# Memory baseline (Windows)

LightSharp should remain responsive and memory-efficient as features are added.

## Initial observation

On 2026-11-08, a developer observed **177.1 MB** for the LightSharp process in Windows Task Manager, with a project open, in a Debug build. A VS Code process showed **505.3 MB** with the same project. These are informal, single-process observations, not controlled benchmarks. VS Code can run additional processes; its full footprint is not represented by the single process.

## Reproduce a measurement

1. Launch LightSharp, open the same solution and files, and wait for initial analysis to settle.
2. In PowerShell, find its process ID with `Get-Process LightSharp`.
3. Run `./tools/Measure-Memory.ps1 -ProcessId <PID> -DurationSeconds 60 -OutputPath lightsharp-memory.csv`.
4. Repeat with a Release build and with common tasks (idle, open project, edit, navigate).
5. Record OS, .NET runtime, configuration, solution size, number of open files, sample duration, average/peak working set and private bytes.

The sampler is **opt-in**, adds no runtime dependency to the IDE, and writes only process-level metrics. It does not capture other processes, GPU allocations, or memory shared with the operating system. Compare equivalent workloads and the same memory metric; avoid treating Debug and Release as directly interchangeable.

Do not enforce a fixed memory threshold in hosted CI: runner noise and platform differences make that misleading. Use repeated local measurements and investigate sustained regressions.
