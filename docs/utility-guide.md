# SystemMate utility guide

Use the dashboard to choose a task. Inventories and scans show what is present; changing a startup entry or power plan requires a separate confirmation. Windows repair tools request administrator approval and open a visible command window. Review the final result in that window: launching it is not a successful-repair result.

## Available tools

| Area | What it does | Scope |
| --- | --- | --- |
| Cleaner | Preview and remove selected allowlisted caches; cancel work; view per-file outcomes | Current-user/Windows temp, cache-only browser locations, VS Code/npm/pip caches, top-level thumbnail databases. Running owners, inaccessible paths, and linked paths are skipped. Recycle Bin opens in Windows. |
| Storage explorer | Measure a chosen folder, see large files and folder/type totals, compare contents with SHA-256, export CSV | Read-only. Logical size differs from disk allocation. Hard links can affect duplicate-space estimates. Hashing cloud-only files may download them. Limits: 100,000 files, 200,000 entries, depth 128, 500 displayed duplicate groups. Partial results are labelled. |
| Apps | Search reported desktop installations and export their metadata | Installer registry entries, not a complete Store/portable inventory. Dates and sizes are reported values. Removal and repair continue in Windows Settings. |
| Startup | Inspect Run/RunOnce and startup folders; disable/restore supported current-user Run values | Verified backup before removal; restore refuses an existing value. Scheduled tasks, services, and packaged-app startup are outside this list. Unknown status is not evidence of enablement. |
| Browser extensions | Inspect local manifests, versions, profiles, listed/optional permissions; export metadata and copy the browser manager address | Chrome, Edge, Brave, Vivaldi, Firefox standard profiles. Installed files are not proof of current enablement or granted permissions. No extension changes, cookie/history/password reads, or browser launches. |
| Performance | CPU usage graph, sampled process CPU/memory, Windows-reported clock, available power plans | Accessible processes only. CPU is normalized to the whole device. Plan changes can affect heat and battery life; the prior plan can be restored while on the page. No voltage or hardware overclocking controls. |
| Windows tools | Inspect drivers/services/restore points; run selected diagnostics; open system settings | Inventory access varies by Windows edition and permissions. Driver dates do not establish whether an update is needed. No generic registry cleaning, service disabling, or restore-point deletion. |

CSV exports neutralize spreadsheet formulas in untrusted names and metadata. Unexpected application errors are recorded locally under the application's data folder, in `Logs/errors.log`.

## Reviewed Windows commands

Commands are selected from a fixed catalog; the app does not accept arbitrary shell text or execute downloaded scripts.

| Task | Command | Effect |
| --- | --- | --- |
| Network configuration | `ipconfig /all` | Read adapter/DNS/DHCP configuration. |
| Inspect DNS cache | `ipconfig /displaydns` | Read cached name resolutions. |
| Clear DNS cache | `ipconfig /flushdns` | Discard cached answers after confirmation. |
| Sleep support | `powercfg /a` | Read supported sleep states. |
| Keep-awake requests | `powercfg /requests` | Read requests in an administrator window. |
| Component-store checks | `DISM /Online /Cleanup-Image /CheckHealth` or `/ScanHealth` | Check or scan the Windows image. |
| Component-store repair | `DISM /Online /Cleanup-Image /RestoreHealth` | Repair after confirmation; Windows Update may download replacement files. |
| Protected-file verification | `sfc /verifyonly` | Verify without repairing. |
| Protected-file repair | `sfc /scannow` | Repair after confirmation. |
| App update availability | `winget upgrade --disable-interactivity` | List matching available updates; does not install upgrades or automatically accept source agreements. |

## Sources used

The supplied CCleaner screenshots were feature references. Product discovery also used [CCleaner Health Check](https://support.ccleaner.com/articles/en_US/Master_Article/what-is-health-check), the [Wintoys developer's overview](https://bogdan-patraucean.github.io/about/wintoys/), and [Quick CPU's documentation](https://coderbag.com/product/quickcpu/docs). SystemMate uses its own screens and implementations.

Windows behavior and command definitions were checked against [powercfg](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options), [SFC](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sfc), [DISM repair guidance](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/repair-a-windows-image), [WinGet upgrade](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade), [Run/RunOnce keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), [installer metadata](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key), and [Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).

Extension metadata handling follows [Chrome's manifest reference](https://developer.chrome.com/docs/extensions/reference/manifest), [permission declarations](https://developer.chrome.com/docs/extensions/develop/concepts/declare-permissions), and [Mozilla's extension database implementation](https://raw.githubusercontent.com/mozilla-firefox/firefox/main/toolkit/mozapps/extensions/internal/XPIDatabase.sys.mjs). No third-party product source code or script collection was imported.
