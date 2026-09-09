# TempControl Project Quick Start

## What this project is
`TempControl` is a WPF `MVVM` desktop application for oven chamber monitoring.

It is intended to:
- read values from Modbus RTU and Modbus TCP devices
- keep the latest values in shared runtime memory
- log temperatures into SQLite
- display live values and historical graphs
- export CSV reports

---

## Main project flow

1. app starts
2. startup check runs in `App.xaml.cs`
3. if allowed, `MainWindow` opens
4. `MainViewModel` creates services and page view models
5. `DatabaseService` initializes SQLite
6. `ModbusPollingService` starts background work
7. UI pages bind to `PlcDataStore`
8. graph page reads historical DB data
9. menu page exports CSV and shows communication status

---

## Most important files

### Startup
- `App.xaml`
- `App.xaml.cs`

### Main shell
- `MainWindow.xaml`
- `ViewModels\MainViewModel.cs`

### Shared runtime state
- `Services\PlcDataStore.cs`

### Background logic
- `Services\ModbusPollingService.cs`

### Database
- `Services\DatabaseService.cs`

### Modbus abstraction
- `Services\IModbusService.cs`

### Main screens
- `Views\HomeView.xaml`
- `Views\SettingsView.xaml`
- `Views\PlcIoView.xaml`
- `Views\GraphView.xaml`
- `Views\MenuView.xaml`

---

## Inputs

### Device / communication inputs
- `COM1`
- `COM6`
- TCP `192.168.1.100:502`

### Configured devices
- MFM meter
- Zone 1 controller
- Zone 2 controller
- PLC

### User inputs
- setpoints
- ramp rate
- soak time
- report dates
- report save path
- logging interval

### Startup authorization input
- allowed MAC address list in `App.xaml.cs`

---

## Outputs

### UI outputs
- temperatures
- outputs
- I/O point status
- communication status
- graphs
- status messages

### Files created
- `ovendata.db`
- CSV export files

---

## Important current status

### What is working
- app startup
- page navigation
- shared data binding
- SQLite initialization
- DB logging loop
- graph reading from DB
- CSV export

### What is not fully implemented yet
- real Modbus RTU polling
- real Modbus TCP polling
- writing settings back to field devices
- real communication success status

Current reason:
- `ModbusPollingService` still marks devices as `No transport`
- transport implementation/factory is not wired yet

---

## Important things to know before editing

1. `PlcDataStore` is central and affects many screens.
2. `MainViewModel` is the main runtime coordinator.
3. `GraphViewModel` reads from the DB, not directly from live polling.
4. Startup logic is now controlled from `App.xaml.cs` because of MAC-based machine check.
5. Real Modbus transport implementation is still incomplete.

---

## Short explanation for a teammate
`TempControl` is a WPF MVVM app built around a shared data store, a background polling/logging service, and SQLite-based history. The UI and data flow are mostly in place, DB logging works, but actual Modbus transport is still incomplete. The app currently also includes a MAC-based startup restriction in `App.xaml.cs`.

---

# Change summary for this edit

## What changed
- created `Assets\ProjectQuickStart.md`
- added a shorter quick-start note for a teammate
- summarized purpose, flow, important files, inputs, outputs, and current project status

## What did not change
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `Services\ModbusPollingService.cs`
- `Services\DatabaseService.cs`
- `Services\PlcDataStore.cs`
- any `Models`
- any `Views`
- any `ViewModels`
- `Assets\DeploymentNotes.md`
- `Assets\MachineLockApproaches.md`
- `Assets\ProjectOnboardingSummary.md`

## Type of change
- documentation only
