# TempControl Project Report

> **Document status:** Source of truth for the project documentation. Generated against the current repository state on 2026-09-10. The companion HTML file represents this same report for browser reading and later PDF printing.

## 1. Project Brief and Index

TempControl is a WPF desktop application for operating and monitoring an oven chamber. It combines a PLC polling loop, an observable in-memory `PlcDataStore`, SQLite temperature/recipe storage, live and historical graphing, manual controls, alarm history, CSV reporting, and PDF trend export.

The application targets `net10.0-windows` and starts from `App.xaml.cs`. `MainWindow` presents a shared shell with navigation and data-template-driven screens.

### Contents

1. [Project File and Folder Structure](#2-project-file-and-folder-structure)
2. [Application Working and Flow](#3-application-working-and-flow)
3. [Database Schema](#4-database-schema)
4. [Database and Data Storage](#5-database-and-data-storage)
5. [Graph](#6-graph)
6. [UI Screens](#7-ui-screens)
7. [PLC Communication Protocol](#8-plc-communication-protocol)
8. [Libraries and NuGet Packages](#9-libraries-and-nuget-packages)
9. [Flowcharts](#10-flowcharts)
10. [Work Log](#11-work-log)
11. [Recommended Improvements](#12-recommended-improvements)
12. [File Relationship and AI Maintenance Map](#13-file-relationship-and-ai-maintenance-map)

### Current operational rules

| Area | Current behavior |
|---|---|
| Roles | Exactly `Operator` and `Supervisor` are implemented. |
| Authorization session | The runtime value is read from `Authorization.SessionTimeoutMinutes`; the shipped default is 2 minutes. |
| Process Parameters | The visible recipe table contains Step 1 and Step 2 plus safety and blower fields. The database/model still retain five-step columns for compatibility. |
| Recipe selection | Dropdown selection is a UI preview. The explicit `SELECT` command performs numerical process-register writes. |
| D329 | `D329` is not used for recipe-selection feedback in the current implementation. |
| Home safety values | Home binds to `PlcDataStore.ProcessZone1Safety` and `ProcessZone2Safety`; their PLC source addresses are configured in `appsettings.json`. |
| Graph Y-axis | Fixed from 0 to 250 with a 50-unit major step and four subdivisions, representing 10-unit minor divisions. |

## 2. Project File and Folder Structure

### Top-level structure

| Path | Purpose |
|---|---|
| `App.xaml`, `App.xaml.cs` | WPF application resources and startup/shutdown. Loads configuration and creates the main window. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Shared shell, header, navigation, data templates, and root `MainViewModel`. |
| `TempControl.csproj` | WPF project definition, target framework, package references, and output-copy rules. |
| `TempControl.slnx` | Solution entry point. |
| `appsettings.json` | Customer-editable runtime credentials, session timeout, and PLC safety-temperature register addresses. |
| `Models/` | Plain data models such as `Recipe`, `PlcIoPoint`, `SlaveDeviceConfig`, and alarm/temperature records. |
| `ViewModels/` | MVVM state and commands for the application shell and each screen. |
| `Views/` | Active WPF XAML screens and their code-behind. |
| `Services/` | PLC transport/polling, database, authorization, run-session, alarm, cache, logging, and PDF services. |
| `Converters/` | WPF value converters. |
| `Properties/PublishProfiles/` | Publish configuration. |
| `Assets/` | Images, logs, CSV exports, and historical project notes. C# files under `Assets/` are excluded from compilation by the project file. |
| `docs/` | Existing focused documentation notes. |
| `bin/`, `obj/` | Build output and intermediate files; not application source. |

### Active screen files

| Screen | View | ViewModel | Main responsibility |
|---|---|---|---|
| Home | `Views/HomeView.xaml` | `ViewModels/HomeViewModel.cs` | Live oven state, safety values, timer/process state, alarms, RUN/STOP. |
| Manual | `Views/ManualPage.xaml` | `ViewModels/ManualPageViewModel.cs` | Manual setpoints, safety temperatures, blower/heater controls. |
| Process Parameters | `Views/SettingsView.xaml` | `ViewModels/SettingsViewModel.cs` | Recipe preview, explicit recipe apply, editable process parameters. |
| PLC I/O | `Views/PlcIoView.xaml` | `ViewModels/PlcIoViewModel.cs` | Digital input/output state lists. |
| Graph | `Views/GraphView.xaml` | `ViewModels/GraphViewModel.cs` | Live/history temperature chart, ruler, values drawer, PDF export. |
| Alarm History | `Views/AlarmHistoryView.xaml` | `ViewModels/AlarmHistoryViewModel.cs` | Active and cleared alarms and acknowledgement. |
| Data Log | `Views/MenuView.xaml` | `ViewModels/MenuViewModel.cs` | CSV reporting, logging interval, communication status. |
| Readings | `Views/EnergyReadingsView.xaml` | `ViewModels/EnergyReadingsViewModel.cs` | Voltage, current, power, and zone-current readings. |

### Supporting dialogs

| Dialog | Purpose |
|---|---|
| `Views/AuthorizationDialog.xaml` | Existing username/password authorization dialog. |
| `Views/RunDialogWindow.xaml` | Run folder, CSV name, sample interval, and auto-start choice. |
| `Views/ExportHeadingDialog.xaml` | Heading requested only during graph PDF export. |

## 3. Application Working and Flow

### Startup sequence

1. `App.OnStartup` creates `AuthorizationService`.
2. `AuthorizationService` loads `appsettings.json` from `AppContext.BaseDirectory`, which is the directory containing `TempControl.exe`.
3. Invalid or missing configuration causes a clear startup error and prevents normal application startup.
4. `MainWindow` creates `MainViewModel`.
5. `MainViewModel` creates `PlcDataStore` using the loaded configuration.
6. `PlcStateCache` restores last-known observable values from `plc_state_cache.json`.
7. `DatabaseService.Initialize` opens/creates `ovendata.db`, creates schema, and ensures the five predefined recipes exist.
8. `ModbusPollingService` starts database logging and grouped PLC polling.
9. Screen ViewModels are created and Home becomes the initial view.

### Runtime data flow

```text
PLC transport
    -> ModbusPollingService
    -> PlcDataStore.UpdateRegisterValue / UpdateCoilValue
    -> ViewModels observe properties
    -> WPF Views update through bindings

ModbusPollingService / RunSessionService
    -> DatabaseService.InsertRecord
    -> SQLite TemperatureLog

SQLite DatabaseService queries
    -> GraphViewModel
    -> Menu CSV export / PdfTrendExporter
```

### Authorization and permissions

| Operation | Operator | Supervisor | Session required |
|---|---:|---:|---:|
| Open/interact with recipe dropdown | Yes | Yes | Yes; existing dialog if absent/expired |
| Preview a recipe | Yes | Yes | Yes |
| Explicit recipe `SELECT` apply | Yes | Yes | Yes |
| Enter Process Parameters edit mode | No | Yes | Yes |
| `Set Recipe` edited values | No | Yes | Yes |
| Manual controls | Restricted by existing manual authorization and mode checks | Yes, subject to checks | Yes |
| Run/Stop | Existing Home behavior | Existing Home behavior | Existing command rules |

A new successful authorization replaces the current session. The timer clears authorization after the configured duration.

### Recipe/process sequence behavior

The current predefined modes are seeded by `DatabaseService.EnsurePredefinedRecipes`:

| Mode | Zone 1 °C | Zone 2 °C | Safety °C | Soak source | Stored/UI representation | Ramp minutes |
|---|---:|---:|---:|---:|---:|---:|
| STATOR PREHEATING | 70 | 70 | 90 | 12 hours | 720 minutes | 90 |
| ROTOR VPI CURING | 165 | 165 | 180 | 12 hours | 720 minutes | 90 |
| STATOR VPI CURING | 150 | 150 | 175 | 32 hours | 1920 minutes | 90 |
| ROTOR BANDING CURING | 150 | 150 | 175 | 12 hours | 720 minutes | 90 |
| ROTOR PRE BANDING CURING | 150 | 150 | 175 | 8 hours | 480 minutes | 90 |

Dropdown selection calls `LoadRecipeValues` and updates the UI only. `SELECT` writes the actual numerical process values through the existing `ManualControlService`; it does not write a recipe feedback register. `D329` is not used for recipe-selection feedback in the current implementation.

## 4. Database Schema

The application uses SQLite through `Microsoft.Data.Sqlite`. The default database file is `ovendata.db` in the process working directory.

### `TemperatureLog`

| Table | Column | Type | Purpose |
|---|---|---|---|
| `TemperatureLog` | `Id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | Record identity. |
| `TemperatureLog` | `Timestamp` | `TEXT NOT NULL` | ISO/round-trip timestamp. |
| `TemperatureLog` | `Zone1Temp` | `REAL NOT NULL` | Zone 1 temperature. |
| `TemperatureLog` | `Zone2Temp` | `REAL NOT NULL` | Zone 2 temperature. |
| `TemperatureLog` | `Zone1SetPv` | `REAL NOT NULL DEFAULT 0` | Zone 1 set/PV-related value. |
| `TemperatureLog` | `Zone2SetPv` | `REAL NOT NULL DEFAULT 0` | Zone 2 set/PV-related value. |
| `TemperatureLog` | `Zone1Sv` | `REAL NOT NULL DEFAULT 0` | Zone 1 SV-related value. |
| `TemperatureLog` | `Zone2Sv` | `REAL NOT NULL DEFAULT 0` | Zone 2 SV-related value. |
| `TemperatureLog` | `Zone1JobPv` | `REAL NOT NULL DEFAULT 0` | Zone 1 job-PV-related value. |
| `TemperatureLog` | `Zone2JobPv` | `REAL NOT NULL DEFAULT 0` | Zone 2 job-PV-related value. |

Index: `IX_TemperatureLog_Timestamp` on `Timestamp`.

### `Recipes`

| Table | Column | Type | Purpose |
|---|---|---|---|
| `Recipes` | `Id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | Recipe identity. |
| `Recipes` | `RecipeName` | `TEXT NOT NULL` | Display name. |
| `Recipes` | `RecipeMode` | `INTEGER NOT NULL UNIQUE` | PLC/application mode identifier retained in storage. |
| `Recipes` | `Step1Zone1Temp`, `Step1Zone2Temp`, `Step1SoakTime`, `Step1RampRate` | `REAL NOT NULL` | Step 1 values. |
| `Recipes` | `Step2Zone1Temp`, `Step2Zone2Temp`, `Step2SoakTime`, `Step2RampRate` | `REAL NOT NULL` | Step 2 values. |
| `Recipes` | `Step3Zone1Temp`, `Step3Zone2Temp`, `Step3SoakTime`, `Step3RampRate` | `REAL NOT NULL` | Legacy/database compatibility fields. Not visible in the current Process Parameters table. |
| `Recipes` | `Step4Zone1Temp`, `Step4Zone2Temp`, `Step4SoakTime`, `Step4RampRate` | `REAL NOT NULL` | Legacy/database compatibility fields. Not visible in the current Process Parameters table. |
| `Recipes` | `Step5Zone1Temp`, `Step5Zone2Temp`, `Step5SoakTime`, `Step5RampRate` | `REAL NOT NULL` | Legacy/database compatibility fields. Not visible in the current Process Parameters table. |
| `Recipes` | `ProcessZone1Safety`, `ProcessZone2Safety` | `REAL NOT NULL` | Safety values for the two zones. |
| `Recipes` | `ProcessBlower1`, `ProcessBlower2` | `REAL NOT NULL` | Exhaust blower ON/OFF values. |

### Schema initialization and migration behavior

`DatabaseService.Initialize` creates tables if absent, renames/ensures historical temperature columns, then calls `EnsurePredefinedRecipes`. That method verifies the five expected modes and replaces the recipe rows if the database does not match the current predefined set.

## 5. Database and Data Storage

### PLC to database path

`ModbusPollingService.DbLoggingLoopAsync` reads current values from `PlcDataStore` and calls `DatabaseService.InsertRecord`. `RunSessionService` also writes immediate samples to SQLite during an active run and buffers corresponding CSV rows.

```text
PLC registers/coils
    -> OmronFinsTcpTransport
    -> ModbusPollingService
    -> PlcDataStore
    -> DatabaseService.InsertRecord
    -> TemperatureLog
```

### Database to graph/report path

```text
TemperatureLog
    -> DatabaseService.QueryRangeWithJobPv / QueryRangeForPdf / QueryRangeFull
    -> GraphViewModel or export service
    -> chart, CSV, or PDF
```

### Cache and logs

| File/path | Purpose |
|---|---|
| `plc_state_cache.json` | Last-known process/manual values restored at startup by `PlcStateCache`. |
| `ovendata.db` | SQLite temperature and recipe database. |
| `Logs/debug_yyyyMMdd.txt` | Diagnostic logger output. |
| `run_folder.txt` | Last run-dialog folder preference. |

The precise deployment location of these runtime files follows the current process working-directory/path logic. The `appsettings.json` path is explicitly the executable directory.

## 6. Graph

### Series

`GraphViewModel` defines three LiveChartsCore line series:

1. **Zone 1 Temperature**: red; live source `PlcDataStore.Zone1Temperature`; historical source `DatabaseService.QueryRangeWithJobPv`.
2. **Zone 2 Temperature**: green; live source `PlcDataStore.Zone2Temperature`; historical source `DatabaseService.QueryRangeWithJobPv`.
3. **Job PV**: blue; live source `PlcDataStore.Zone1Output`; historical source returned as `JobPv` by the database query.

The current PDF query selects `Zone1Sv` for its third value while the graph/export model labels that value `JobPv`. This source-label mismatch is present in the current implementation and should be treated as a known gap.

### Live and historical data

- Live mode initially loads the previous 24 hours from SQLite.
- A one-second dispatcher timer runs, but a chart point is appended every 60 seconds.
- History mode validates `HH:mm` start/end values and queries the selected range.
- Empty or invalid ranges are reported through the graph controls status area.

### Axes and navigation

| Feature | Current implementation |
|---|---|
| X-axis | `DateTimeAxis`, five-minute step. Labels are `HH:mm` for shorter windows and date plus time for history/24-hour views. |
| Y-axis | `Temperature (°C)`, minimum 0, maximum 250, major `MinStep` 50, four subdivisions producing 10-unit minor divisions. |
| Zoom | `5m`, `15m`, `30m`, `1h`, `All` (24 hours). `ZoomMode="X"` prevents vertical scaling changes. |
| Scroll | Horizontal scrollbar changes the X-axis visible window. |
| Ruler | `GraphView.xaml.cs` positions a movable vertical ruler on mouse movement. |
| Cursor values | Nearest loaded point for Zone 1, Zone 2, and Job PV; each has its own marker and label. |
| VALUES drawer | Slide-up statistics panel with selected/current value, minimum, maximum, and average. |
| PDF | History/live range is queried, a heading is requested at export time, and `PdfTrendExporter` creates the PDF. |

### PDF trend behavior

`GraphViewModel.ExportPdf` checks the range and data, opens `ExportHeadingDialog`, opens a save dialog, and calls `PdfTrendExporter.Export`. The exporter uses SkiaSharp, splits long ranges into pages up to six hours, and reduces samples to one-minute representatives using average/min/max logic as implemented.

## 7. UI Screens

### Home

- **Purpose:** Primary live operating screen.
- **View/ViewModel:** `Views/HomeView.xaml` / `ViewModels/HomeViewModel.cs`.
- **Data:** Zone temperatures, setpoints/output-related values, process state, timer values, safety values, and alarms from `PlcDataStore` and `AlarmHistoryService`.
- **Actions:** RUN, STOP, alarm drawer, clear alarms.
- **Special behavior:** Zone 1 and Zone 2 safety values appear beside their headings. The existing UI binds them to `DataStore.ProcessZone1Safety` and `DataStore.ProcessZone2Safety`; configured PLC addresses are resolved in `PlcDataStore`.

### Manual

- **Purpose:** Authorized manual setpoint and actuator operation.
- **View/ViewModel:** `Views/ManualPage.xaml` / `ViewModels/ManualPageViewModel.cs`.
- **Data/actions:** Zone setpoints, safety temperatures, blower/heater state, manual/auto checks, protected register and coil writes.
- **Authorization:** Existing Operator/Supervisor checks and process/manual-mode safety checks.

### Process Parameters

- **Purpose:** Preview and apply predefined process sequences, or edit values as Supervisor.
- **View/ViewModel:** `Views/SettingsView.xaml` / `ViewModels/SettingsViewModel.cs`.
- **Visible controls:** Recipe dropdown, explicit `SELECT`, Step 1 and Step 2 temperature/soak/ramp fields, combined safety field, blower ON/OFF fields, `Edit`, and `Set Recipe`.
- **Selection behavior:** Dropdown interaction uses the existing authorization service and loads values as a preview. It does not write to the PLC.
- **Apply behavior:** `SELECT` writes current numerical process values through existing mappings after PLC connection checks. `D329` is not used for recipe-selection feedback.
- **Edit behavior:** Supervisor-only edit mode; process-running checks remain active. The green inline status line was removed; errors and success use dialogs.

### PLC I/O

- **Purpose:** Display configured digital input/output points.
- **View/ViewModel:** `Views/PlcIoView.xaml` / `ViewModels/PlcIoViewModel.cs`.
- **Data source:** `PlcDataStore.IoPoints` and observable connection/state properties.

### Graph

- **Purpose:** Inspect live and historical temperature trends and export PDF.
- **View/ViewModel:** `Views/GraphView.xaml` / `ViewModels/GraphViewModel.cs`.
- **Actions:** Live/history mode, date/time range, zoom presets, horizontal scroll, ruler, VALUES drawer, PDF export.

### Alarm History

- **Purpose:** Review configured active and cleared alarms.
- **View/ViewModel:** `Views/AlarmHistoryView.xaml` / `ViewModels/AlarmHistoryViewModel.cs`.
- **Service:** `AlarmHistoryService` tracks coils, durations, active/cleared state, and acknowledgement.

### Data Log

- **Purpose:** Export CSV reports and inspect logging/communication settings.
- **View/ViewModel:** `Views/MenuView.xaml` / `ViewModels/MenuViewModel.cs`.
- **Export:** `DatabaseService.ExportToCsv` creates a date-ranged CSV with serial number, timestamp, temperatures, selected-zone setpoint, and job-PV fields.

### Readings

- **Purpose:** Show electrical readings.
- **View/ViewModel:** `Views/EnergyReadingsView.xaml` / `ViewModels/EnergyReadingsViewModel.cs`.
- **Data:** Voltage, current, total power, and zone-current values assembled by `PlcDataStore`.

### Authorization Dialog

- **Purpose:** Existing modal username/password authorization.
- **Files:** `Views/AuthorizationDialog.xaml`, `Views/AuthorizationDialog.xaml.cs`.
- **Behavior:** Validates against runtime configuration through `AuthorizationService`, then starts/replaces the configured session.

### Run Dialog

- **Purpose:** Configure run CSV folder/name and sample interval.
- **Files:** `Views/RunDialogWindow.xaml`, `Views/RunDialogWindow.xaml.cs`.
- **Behavior:** Supports a 15-second auto-start selection; default folder is `C:\ovencycle_runs`, with last folder persisted in `run_folder.txt`.

### Export Heading Dialog

- **Purpose:** Request a report heading only while exporting a graph PDF.
- **Files:** `Views/ExportHeadingDialog.xaml`, `Views/ExportHeadingDialog.xaml.cs`.
- **Behavior:** Blank headings fall back to `Temperature Trend Report`.

## 8. PLC Communication Protocol

### Transport and connection

- PLC device: configured as Slave ID `4`.
- Active connection type: Omron FINS/TCP.
- Current endpoint in source: `192.168.250.1:9600`.
- PLC transport implementation: `OmronFinsTcpTransport` using `HslCommunication`.
- Other implementations exist for raw Modbus TCP/RS485 and a null simulation transport.
- `MainViewModel` currently uses `OmronTransportFactory` with `OfflineMode = false`.

### Polling

`ModbusPollingService` groups slaves by connection endpoint, connects with retry/backoff, reads configured holding registers and coils, and dispatches updates through the WPF dispatcher. A shared semaphore protects the active transport so polling and writes do not overlap.

`IsConnected` and `LastPollStatus` are maintained per `SlaveDeviceConfig`. The shared `_activeTransport` is reused by `ManualControlService` for writes; no second PLC connection is opened for manual or recipe operations.

### Important holding registers

| Address | Current meaning | Data path |
|---:|---|---|
| D100 | Zone 1 setpoint, divided by 10 | `PlcDataStore.Zone1Setpoint` |
| D103 | Zone 2 setpoint | `PlcDataStore.Zone2Setpoint` |
| D120 | Zone 1 actual temperature, divided by 10 | `PlcDataStore.Zone1Temperature` |
| D121 | Zone 2 actual temperature, divided by 10 | `PlcDataStore.Zone2Temperature` |
| D123, D125 | Output/job-temperature-related values | `PlcDataStore` output fields |
| D301-D308 | Visible Step 1 and Step 2 process parameters | Settings recipe writes and polling |
| Configured Zone 1/2 safety addresses | Safety temperature values; defaults are D321/D322 | `ProcessZone1Safety`, `ProcessZone2Safety` |
| D323-D324 | Blower values | Process parameters |
| D325-D326 | Manual setpoints | Manual page |
| D327-D328 | Manual safety-temperature fields | Manual page |
| D400 | Current process step | `CurrentProcessStep` |
| D401 | Elapsed time | `ElapsedTime` |
| D402 | Set/soak time | `SoakTime` |
| D138-D171, D200-D210, D250-D260 | Energy/zone-current blocks | Float assembly in `UpdateEnergyRegisterValue` |

The database/model retain D309-D320 legacy five-step fields, but the current `SELECT` apply workflow writes only visible Step 1/2 process values plus safety and blower values. `Set Recipe` retains its existing Supervisor editing path in the current source.

### Important coils

| Coil/address | Current meaning |
|---:|---|
| 10000-10024 | Digital inputs, including manual/auto state at 10024. |
| 1600-1608, 1617 | Digital outputs and tower indicators. |
| 10704 | W44.0 process run/stop command path. |
| 11760 | W110.0 process-running feedback. |
| 10640-10643 | Manual blower/heater actuator write coils. |
| 10644 | W40.4 run/stop write path used by `RunSessionService`. |

### Writes and error handling

`ManualControlService` converts numeric values to bounded `ushort` values and delegates register writes to `ModbusPollingService.WriteRegisterAsync`. A false result indicates no active transport or a write exception. Recipe `SELECT` checks the existing PLC connection state before attempting writes and distinguishes a disconnected PLC popup from a connected-but-failed write popup.

No raw PLC address list is shown to operators for current recipe-apply failures. `D329` is not used for recipe-selection feedback in the current implementation.

## 9. Libraries and NuGet Packages

| Package | Version | Current use |
|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.0 | Observable properties, commands, and MVVM source generation. |
| `HslCommunication` | 12.8.1 | Omron FINS/TCP transport and PLC communication support. |
| `LiveChartsCore.SkiaSharpView.WPF` | 2.0.0-rc5.4 | WPF charting, axes, series, and chart rendering. |
| `Microsoft.Data.Sqlite` | 9.0.5 | SQLite database access. |
| `System.IO.Ports` | 10.0.7 | Serial/RS485 transport support. |
| SkiaSharp APIs used by source | Via chart/PDF dependencies | PDF rendering and chart paints. |

Target framework: `net10.0-windows`; output type: `WinExe`; WPF enabled; nullable and implicit usings enabled.

## 10. Flowcharts

The companion HTML contains inline SVG versions of these same flows. The Markdown descriptions below are the source representation.

### A. Overall application flow

```text
App.OnStartup
  -> load appsettings.json from executable directory
  -> validate authorization and PLC register configuration
  -> create MainWindow
  -> create MainViewModel
  -> create PlcDataStore and restore cache
  -> initialize SQLite
  -> start polling/logging
  -> create screen ViewModels
  -> show Home
```

### B. PLC polling flow

```text
Start ModbusPollingService
  -> group configured slaves
  -> connect transport with retry/backoff
  -> read holding registers and coils
  -> dispatch values on WPF dispatcher
  -> PlcDataStore maps values
  -> ViewModels and Views update
  -> poll again
```

### C. Process parameter/sequence flow

```text
Open recipe dropdown
  -> existing authorization dialog/session
  -> preview selected Recipe values in UI
  -> no PLC write

Press SELECT
  -> verify authorized role/session and process not running
  -> check existing PLC connection state
  -> if disconnected: PLC Disconnected dialog
  -> otherwise write visible process values and configured safety values
  -> if any write fails: Recipe Set Failed dialog
  -> if all writes succeed: Recipe Set Successfully dialog
```

### D. Run/Stop flow

```text
Home RUN
  -> RunDialogWindow
  -> create run folder/path
  -> write coil 10644 = true
  -> start interval logging and immediate sample
  -> write SQLite rows and buffer CSV rows

Home STOP or PLC running feedback turns false
  -> stop timer
  -> write coil 10644 = false when operator stops
  -> finish CSV
  -> mark session inactive
```

### E. Graph data flow

```text
Live mode
  -> query previous 24 hours from SQLite
  -> start 1-second timer
  -> append current DataStore values every 60 seconds
  -> update X window, ruler, markers, statistics

History mode
  -> validate date/time range
  -> query SQLite
  -> load three DateTimePoint collections
  -> show chart, values drawer, ruler, statistics
```

### F. Reporting flow

```text
Data Log export
  -> select dates/zone
  -> DatabaseService.ExportToCsv
  -> write CSV

Graph PDF export
  -> choose range/data
  -> ExportHeadingDialog
  -> SaveFileDialog
  -> DatabaseService.QueryRangeForPdf
  -> PdfTrendExporter
  -> write PDF
```

## 11. Work Log

The following entries are limited to changes established from the current conversation and source state. Dates are intentionally omitted because no reliable change dates were established.

| Change | Description | Area |
|---|---|---|
| Graph changes | Added/retained live/history chart behavior, three series, X-axis scrolling, cursor/ruler markers, and VALUES statistics drawer. | Graph |
| Fixed graph scale | Fixed Y-axis to 0-250 with 50-unit major step and 10-unit minor divisions; X-only zoom prevents vertical rescaling. | Graph |
| Ruler/statistics requirements | Restored movable ruler and independent Zone 1, Zone 2, and Job PV nearest-point values. | Graph |
| PDF changes | Heading is requested during PDF export; PDF generation remains handled by `PdfTrendExporter`. | Graph/reporting |
| Home safety display | Added Zone 1/Zone 2 safety values beside the Home zone headings while preserving card layout. | Home |
| Process Parameters reduction | Removed visible Step 3, Step 4, and Step 5 rows; retained bottom safety and blower boxes. | Process Parameters |
| Five predefined modes | Added the five named modes with specified temperatures, safety values, soak meanings, and ramp time. | Recipes/database |
| Ramp time | Predefined modes use 90 minutes. | Recipes |
| Recipe-selection behavior | Dropdown preview is distinct from explicit `SELECT` apply and uses existing authorization. | Authorization/recipes |
| D329 removal | Removed recipe-selection feedback use of D329; no replacement feedback register was introduced. | PLC/recipes |
| Authorization roles | Preserved Operator and Supervisor roles and protected operation checks. | Authorization |
| Two-minute session | Session timeout became configurable with a shipped default of 2 minutes. | Authorization |
| Configuration file | Added executable-directory `appsettings.json` for credentials, timeout, and PLC safety register addresses. | Configuration |
| Safety register configuration | Home/DataStore safety display and process safety writes use configured addresses, defaulting to D321/D322. | PLC/Home |
| PLC apply error handling | Added existing connection-state check and distinguished disconnected from connected write failure. | Process Parameters |
| Green inline feedback removal | Removed the Process Parameters card’s green status line; dialogs remain for errors and success. | Process Parameters |

## 12. Recommended Improvements

The following are **FUTURE recommendations only**. They are not described as implemented features.

1. **Secure credentials:** Avoid storing plaintext passwords in a customer-editable JSON file; consider a protected credential store or deployment-specific secret mechanism.
2. **Protect `appsettings.json`:** Restrict file permissions and document who may edit production configuration.
3. **Configuration validation tooling:** Add a small customer-facing validator or startup diagnostic that identifies the exact invalid property and accepted register format.
4. **Database backup:** Add scheduled/one-click backup and restore for `ovendata.db`.
5. **Application logging controls:** Add retention, severity filtering, and a customer-facing log export workflow.
6. **Version information:** Display application, schema, and configuration versions in an About/Diagnostics view.
7. **Digital signing:** Sign the delivered executable and installer/package.
8. **Automated PLC tests:** Add transport fakes and integration tests for polling, write serialization, connection loss, safety register remapping, and recipe apply behavior.
9. **Schema/API cleanup:** Remove or explicitly quarantine legacy five-step fields after confirming compatibility requirements.
10. **Graph data contract review:** Resolve the current naming/source mismatch between PDF `JobPv` labels and the `Zone1Sv` column selected by `QueryRangeForPdf`.

## 13. File Relationship and AI Maintenance Map

### PLC communication

```text
Entry point: MainViewModel constructor
Main files: Services/ModbusPollingService.cs, Services/OmronFinsTcpTransport.cs,
            Services/BasicModbusTransport.cs, Services/NullModbusTransport.cs
Main service: ModbusPollingService
Main data source: PLC registers/coils through the active transport
Main output: PlcDataStore observable properties and connection state
```

### PLC state and UI binding

```text
Entry point: PlcDataStore constructor / UpdateRegisterValue / UpdateCoilValue
Main files: Services/PlcDataStore.cs, Models/SlaveDeviceConfig.cs
Main service: PlcDataStore
Main data source: ModbusPollingService dispatches
Main output: ViewModel bindings, I/O collections, graph/live values
```

### Process Parameters and recipes

```text
Entry point: SettingsViewModel SelectRecipeCommand / SetRecipeCommand
Main files: ViewModels/SettingsViewModel.cs, Views/SettingsView.xaml,
            Models/Recipe.cs, Services/DatabaseService.cs
Main service: ManualControlService for writes; DatabaseService for persistence
Main data source: Recipes table and selected recipe
Main output: UI preview, process-register writes, success/error dialogs
```

### Authorization

```text
Entry point: AuthorizationService.RequestAuthorization
Main files: Services/AuthorizationService.cs,
            Services/ApplicationConfiguration.cs,
            Views/AuthorizationDialog.xaml(.cs)
Main service: AuthorizationService
Main data source: appsettings.json in executable directory
Main output: CurrentRole, active session, protected command decisions
```

### Configuration

```text
Entry point: App.OnStartup -> AuthorizationService -> ApplicationConfiguration.Load
Main files: appsettings.json, Services/ApplicationConfiguration.cs,
            Services/AuthorizationService.cs, App.xaml.cs
Main service: ApplicationConfiguration
Main data source: appsettings.json beside TempControl.exe
Main output: credentials/session duration and configured safety register addresses
```

### Database and reporting

```text
Entry point: MainViewModel DatabaseService.Initialize
Main files: Services/DatabaseService.cs, Services/RunSessionService.cs,
            Services/PdfTrendExporter.cs, ViewModels/GraphViewModel.cs,
            ViewModels/MenuViewModel.cs
Main service: DatabaseService
Main data source: SQLite TemperatureLog and Recipes tables
Main output: graph points, CSV files, PDF trend reports
```

### UI navigation

```text
Entry point: MainWindow.xaml data templates and MainViewModel navigation commands
Main files: MainWindow.xaml, MainWindow.xaml.cs,
            ViewModels/MainViewModel.cs, Views/*.xaml
Main service: MainViewModel as screen coordinator
Main data source: screen ViewModels and shared services
Main output: active WPF screen in the shell
```

### Confirmed limitations

- `D329` is absent from active source and is not used for recipe-selection feedback.
- The current database/model retain five-step fields although the visible Process Parameters table has two steps.
- The PDF Job PV source-label mismatch described above is not resolved by this documentation task.
- Customer credential security beyond file validation is not implemented; plaintext values remain a deployment concern.
