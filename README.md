# SystemMate

> A professional Windows system utility — dashboard, cleaner, history, settings.  
> Built with C# · .NET 10 · WinUI 3 · Windows App SDK. 100% offline.

![SystemMate Dashboard](docs/assets/screenshot-dashboard.png)

## Features (v0.1)

| Feature | Status |
|---|---|
| Live CPU / RAM / Disk monitoring | ✅ |
| Boot time & system info | ✅ |
| Junk file scanner (temp, browser caches) | ✅ |
| Scan preview → approve → clean | ✅ |
| Full operation history (SQLite) | ✅ |
| Dark UI matching Windows 11 aesthetic | ✅ |
| MSIX installer (Start Menu + uninstall) | ✅ |

## Roadmap

- **v0.2** — Startup Manager (registry + startup folder)
- **v0.3** — Storage Analyzer (large files, duplicates, directory tree)
- **v0.4** — Network Tools & Windows Repair
- **v0.5** — SystemMate Doctor (AI diagnostics layer)

## Prerequisites

- Windows 10 (build 19041+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 with workloads:
  - **Windows App SDK / WinUI**
  - **.NET desktop development**

## Build

```powershell
# Clone
git clone https://github.com/yourusername/SystemMate.git
cd SystemMate

# Restore & build
dotnet restore SystemMate.sln
dotnet build SystemMate.sln -c Release

# Run tests
dotnet test tests/SystemMate.Tests/SystemMate.Tests.csproj

# Run the app (Debug)
dotnet run --project src/SystemMate/SystemMate.csproj
```

## MSIX Packaging

Open `SystemMate.sln` in Visual Studio 2022, right-click `SystemMate.Packaging` → **Publish** → **Create App Packages**.

## Architecture

```
View (XAML) ──binds──▶ ViewModel (CommunityToolkit.Mvvm)
                               │
                        Service Layer
                          │       │
                    Windows   SQLite
                      APIs    History
```

## License

MIT
