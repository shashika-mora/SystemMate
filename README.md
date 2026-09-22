# SystemMate

<div align="center">

![SystemMate Banner](docs/assets/banner.png)

**A professional Windows system utility. Clean. Fast. Honest.**

[![Build](https://github.com/shashika-mora/SystemMate/actions/workflows/build.yml/badge.svg)](https://github.com/shashika-mora/SystemMate/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue?logo=windows)](https://www.microsoft.com/windows)

</div>

---

> **SystemMate is not "delete temp files dot exe".**
>
> It shows you exactly what it's going to remove — sizes, file counts, categories — and waits for your approval before touching anything. Every operation is logged to a local SQLite database. Nothing runs silently.

---

## What it does

```
SystemMate
├── Dashboard          Live CPU · RAM · Disk · boot time · system info
├── Cleaner            Scan → preview → approve → clean
│   ├── Windows temp files
│   ├── Browser caches (Chrome, Edge, Firefox)
│   ├── App caches (VS Code, npm, pip)
│   ├── Recycle Bin
│   └── Thumbnail cache
├── History            Every operation logged — timestamp · path · size · status
└── Settings           Theme · behavior prefs · data folder
```

## How cleanup works

```
Cleaner.ScanAsync()          ← read-only, nothing touched
    │
    └── ScanResult           ← sizes, file counts, categories with checkboxes
            │
    User reviews + approves
            │
    Cleaner.CleanAsync()
    │
    ├── HistoryService.Log() ← written to SQLite BEFORE any deletion
    ├── File.Delete()
    └── catch → log error, continue
```

A record is written to the database **before** each file is removed. If the app crashes mid-clean, the history still reflects what was attempted.

## Stack

| Layer | Technology |
|---|---|
| Language | C# 12 |
| Runtime | .NET 8 LTS |
| UI Framework | WinUI 3 / Windows App SDK 1.6 |
| MVVM | CommunityToolkit.Mvvm 8 (source generators) |
| Database | SQLite via Microsoft.Data.Sqlite |
| Installer | MSIX |
| CI | GitHub Actions |
| Tests | xUnit |

## Roadmap

| Version | Feature |
|---|---|
| **v0.1** ✅ | Dashboard · Cleaner · History · Settings · MSIX · CI |
| v0.2 | Startup Manager (registry + startup folder + scheduled tasks) |
| v0.3 | Storage Analyzer (large files · duplicates · directory tree) |
| v0.4 | Network Tools · Windows Repair (SFC, DISM, DNS) |
| v0.5 | Advanced Diagnostics & Rule Engine (offline, deterministic) |

## Build & Run

```powershell
# Prerequisites (.NET 8 SDK)
winget install Microsoft.DotNet.SDK.8

# Clone
git clone https://github.com/shashika-mora/SystemMate.git
cd SystemMate

# Run tests
dotnet test tests/SystemMate.Tests/

# Build
dotnet build src/SystemMate/SystemMate.csproj -c Release

# Run
dotnet run --project src/SystemMate/SystemMate.csproj
```

> **MSIX packaging** requires Visual Studio 2022 with the *Windows App SDK / WinUI* workload.  
> Open `SystemMate.sln` → right-click `SystemMate.Packaging` → Publish → Create App Packages.

## Architecture

```
┌─────────────────────────────────────────────┐
│                   Views (XAML)               │
│  DashboardPage · CleanerPage · HistoryPage  │
└───────────────────┬─────────────────────────┘
                    │ x:Bind (compiled bindings)
┌───────────────────▼─────────────────────────┐
│              ViewModels (MVVM)               │
│   [ObservableProperty]  [RelayCommand]       │
└───────────────────┬─────────────────────────┘
                    │ direct calls
┌───────────────────▼─────────────────────────┐
│               Service Layer                  │
│  SystemInfoService  CleanerService           │
│  HistoryService     SettingsService          │
└──────────┬──────────────────┬───────────────┘
           │                  │
    Windows APIs           SQLite DB
 (WMI · P/Invoke ·      history.db lives in
  PerformanceCounter)   %LOCALAPPDATA%\SystemMate
```

## Project Structure

```
SystemMate/
├── src/
│   ├── SystemMate/               # WinUI 3 application
│   │   ├── Controls/             # Reusable XAML controls
│   │   ├── Models/               # Data contracts
│   │   ├── Services/             # Windows API wrappers
│   │   ├── Styles/               # Colors, typography, cards
│   │   ├── ViewModels/           # MVVM ViewModels
│   │   └── Views/                # XAML pages
│   └── SystemMate.Packaging/     # MSIX packaging project
├── tests/
│   └── SystemMate.Tests/         # xUnit unit tests
├── scripts/
│   └── New-PlaceholderAssets.ps1 # Asset generator
└── .github/
    └── workflows/build.yml       # CI pipeline
```

## License

MIT © 2026 SystemMate
