# TempControl Project Onboarding Summary

## Purpose
`TempControl` is a WPF `MVVM` desktop application intended for oven chamber monitoring.

It is designed to:
- read live values from Modbus RTU and Modbus TCP devices
- store the latest values in shared runtime memory
- log temperature data into SQLite
- display live and historical data on multiple screens
- export CSV reports

This document is intended for a developer or teammate who is new to the project and wants to quickly understand what the project does, how it flows, and which files matter.

---

# 1. High-level project summary

At a high level, the project has these layers:

1. **WPF UI layer**
   - `MainWindow.xaml`
   - page views in `Views\`

2. **ViewModel layer**
   - page-specific view models in `ViewModels\`
   - `MainViewModel` coordinates navigation and startup wiring

3. **Runtime data layer**
   - `Services\PlcDataStore.cs`
   - holds current PLC/controller/meter values

4. **Background service layer**
   - `Services\ModbusPollingService.cs`
   - intended to poll Modbus devices and log temperatures

5. **Persistence layer**
   - `Services\DatabaseService.cs`
   - stores and queries temperature history from SQLite

6. **Model layer**
   - `Models\`
   - defines I/O points, slave devices, and data records

---

# 2. What the application is supposed to do

The intended workflow is:

- connect to RTU/TCP Modbus devices
- read registers and coils from those devices
- map those raw values into typed application properties
- display those values in the UI
- periodically log selected temperatures to SQLite
- allow graphing and CSV export from the stored data

In short:

`Field devices -> Polling service -> Data store -> ViewModels -> UI`

and also:

`Data store -> Database logging -> SQLite -> Graph / CSV export`

---

# 3. Current runtime flow

## Startup flow
The app starts through `App.xaml` and `App.xaml.cs`.

Current startup behavior includes:
- a MAC-based startup check in `App.xaml.cs`
- if the current machine is allowed, the app opens `MainWindow`
- if not allowed, the app shuts down immediately

After startup authorization:
- `MainWindow` is created
- `MainViewModel` is used as the main coordinator

## Main runtime creation flow
Inside `ViewModels\MainViewModel.cs`, the app:
1. creates `PlcDataStore`
2. creates `DatabaseService`
3. initializes the database
4. creates `ModbusPollingService`
5. starts polling/logging service
6. creates all child page view models
7. selects `HomeViewModel` as default screen
8. starts a UI timer for current date/time display

---

# 4. Inputs to the system

The project currently has several logical input sources.

## A. Modbus device inputs
Defined in `Services\PlcDataStore.cs` under slave configurations.

Current configured devices:
- `MFM Meter`
- `Zone 1 Controller`
- `Zone 2 Controller`
- `PLC`

Connection types:
- RTU over COM ports
- TCP over IP

Current configured communication endpoints:
- `COM1`
- `COM6`
- `192.168.1.100:502`

### Input data types from devices
- holding register values
- coil values

These are intended to represent:
- temperatures
- setpoints
- output percentage
- meter voltage/current/power
- PLC digital inputs
- PLC digital outputs

## B. User UI inputs
The user can currently enter or change values through the UI, such as:
- zone setpoints
- ramp rate
- soak time
- report date range
- report save path
- logging interval

## C. Local machine authorization input
The app currently includes a MAC-based startup restriction in `App.xaml.cs`.

That means the app startup also depends on:
- current machine network adapter MAC addresses
- hardcoded allowed MAC address list

---

# 5. Outputs from the system

## A. UI outputs
The app displays:
- live temperatures
- setpoints
- outputs
- I/O status
- communication status
- graph of historical temperatures
- status messages
- date/time

## B. Database output
The app writes to SQLite database file:
- `ovendata.db`

Stored data currently includes:
- timestamp
- zone 1 temperature
- zone 2 temperature

## C. CSV report output
The app can export CSV files from stored DB data.

## D. Debug / status output
The app writes some debug status information to the debug output, especially around DB insert errors and intended Modbus polling status.

---

# 6. Main files and what each one is for

## Application startup
### `App.xaml`
Defines the WPF application root.

### `App.xaml.cs`
Currently contains:
- startup override
- MAC-based machine authorization check
- logic to open `MainWindow` only if the machine is allowed

## Main shell
### `MainWindow.xaml`
Defines the main shell UI.
Contains:
- header
- navigation bar
- content area
- data templates linking view models to views

## Main coordinator
### `ViewModels\MainViewModel.cs`
Main app coordinator.
Responsible for:
- creating services
- starting runtime services
- creating page view models
- navigation between screens
- exposing date/time

## Shared runtime state
### `Services\PlcDataStore.cs`
Central shared in-memory store.
This is one of the most important files in the project.

It holds:
- zone temperatures
- setpoints
- outputs
- meter data
- chamber/process values
- PLC I/O points
- slave configs and runtime connection flags

## Background logic
### `Services\ModbusPollingService.cs`
Intended to:
- poll Modbus devices
- update `PlcDataStore`
- update connection status
- log temperatures to SQLite periodically

Important current status:
- DB logging loop is active
- actual Modbus transport is not wired yet
- polling currently marks devices as `No transport`

## Database layer
### `Services\DatabaseService.cs`
Handles:
- SQLite connection
- database initialization
- insert temperature records
- query by date range
- CSV export

## Modbus abstraction and implementation
### `Services\IModbusService.cs`
Contains:
- `IModbusTransport`
- `IModbusTransportFactory`

These define the Modbus communication contracts.

### `Services\BasicModbusTransport.cs`
Contains:
- `BasicModbusTransportFactory` — creates the correct transport based on slave config
- `RtuModbusTransport` — opens a COM port and communicates using Modbus RTU with CRC
- `TcpModbusTransport` — opens a TCP connection and communicates using Modbus TCP with MBAP header

This is the concrete implementation of the interfaces defined in `IModbusService.cs`.
Interfaces cannot contain code in C#, so implementations live here separately.

---

# 7. UI screens and what they show

## `Home`
Files:
- `Views\HomeView.xaml`
- `ViewModels\HomeViewModel.cs`

Purpose:
- live dashboard for zone values and process values

Shows:
- Zone 1 PV
- Zone 2 PV
- outputs
- setpoint
- soak time
- elapsed time

## `Extra Settings`
Files:
- `Views\SettingsView.xaml`
- `ViewModels\SettingsViewModel.cs`

Purpose:
- modify runtime setpoints and process parameters

Current behavior:
- changes in-memory values
- reset defaults works in memory
- apply settings only updates status text
- no actual Modbus write-back yet

## `PLC I/O`
Files:
- `Views\PlcIoView.xaml`
- `ViewModels\PlcIoViewModel.cs`

Purpose:
- display digital input/output point status
- show last changed time

## `Graph`
Files:
- `Views\GraphView.xaml`
- `ViewModels\GraphViewModel.cs`

Purpose:
- show historical trend of temperatures from DB

Important:
- graph reads SQLite data
- graph does not directly read live Modbus values

## `Menu`
Files:
- `Views\MenuView.xaml`
- `ViewModels\MenuViewModel.cs`

Purpose:
- report export
- logging interval control
- communication state display

---

# 8. Current project limitations / important facts

## A. Real Modbus communication is not active yet
This is the most important current limitation.

In `Services\ModbusPollingService.cs`:
- transport creation is still TODO
- the actual polling loop is still disabled
- slaves are marked disconnected with `No transport`

So the current app structure is ready for Modbus, but real hardware polling is not complete.

## B. DB logging still runs
Even though Modbus polling is not active, DB logging still runs.

This means:
- the database can still receive entries
- those entries may reflect default/stale runtime values if polling is not updating them

## C. Settings are not written back to devices
`SettingsViewModel.ApplySettings()` does not currently send values to devices.

## D. Machine lock is currently MAC-based
The app currently includes a MAC-based startup lock in `App.xaml.cs`.

This means:
- the app will start only on allowed machine(s)
- deployment output is effectively machine-specific

---

# 9. Current configured inputs and outputs in more detail

## Inputs currently configured
### Communication inputs
- `COM1`
- `COM6`
- `192.168.1.100:502`

### Configured device definitions
- MFM meter
- Zone 1 controller
- Zone 2 controller
- PLC

### User-editable runtime values
- `Zone1Setpoint`
- `Zone2Setpoint`
- `RampRate`
- `SoakTime`
- report date range
- report path
- DB logging interval

## Outputs currently produced
### UI values
- temperatures
- outputs
- I/O state indicators
- graph
- communication state

### Database values
- `TemperatureLog`
  - `Timestamp`
  - `Zone1Temp`
  - `Zone2Temp`

### File outputs
- CSV report file
- SQLite DB file

---

# 10. Things a developer should know before changing code

## 1. `PlcDataStore` is central
Many screens depend on it.

Any mapping changes in:
- register addresses
- coil addresses
- slave IDs

can affect multiple screens.

## 2. UI uses MVVM
Views mostly bind to ViewModel properties or `DataStore` properties.

## 3. Graph uses DB, not live data directly
If the graph is wrong, the issue may be in:
- DB logging
- DB query
- data recording
not only in live display

## 4. Modbus architecture is incomplete
Before real hardware support works, the project still needs:
- concrete `IModbusTransport` implementations
- transport factory wiring
- polling loop activation

## 5. Startup is now controlled in `App.xaml.cs`
Because of the MAC-based startup check:
- startup behavior is no longer only XAML-driven
- `App.xaml` and `App.xaml.cs` are important deployment files

---

# 11. Known configuration inconsistencies

These should be reviewed by anyone continuing work:

## Zone 1 controller mismatch
There are inconsistencies around:
- slave ID comment vs actual configured ID
- expected register addresses vs configured addresses
- COM port comment vs actual COM port

This area should be verified before live hardware deployment.

---

# 12. Typical runtime flow in one sequence

1. app starts
2. startup authorization check runs in `App.xaml.cs`
3. if machine is allowed, `MainWindow` opens
4. `MainViewModel` creates services and child view models
5. DB service initializes SQLite
6. polling service starts
7. DB logging loop begins
8. UI displays current values from `PlcDataStore`
9. user navigates between pages
10. graph page reads historical DB data
11. menu page can export CSV and show communication status

---

# 13. Fast file map for a new developer

## Startup / shell
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`

## Runtime core
- `ViewModels\MainViewModel.cs`
- `Services\PlcDataStore.cs`
- `Services\ModbusPollingService.cs`
- `Services\DatabaseService.cs`
- `Services\IModbusService.cs`

## Models
- `Models\PlcIoPoint.cs`
- `Models\SlaveDeviceConfig.cs`
- `Models\ModbusRegisterDef.cs`
- `Models\TemperatureDataPoint.cs`

## Screens
- `Views\HomeView.xaml`
- `Views\SettingsView.xaml`
- `Views\PlcIoView.xaml`
- `Views\GraphView.xaml`
- `Views\MenuView.xaml`

## Page view models
- `ViewModels\HomeViewModel.cs`
- `ViewModels\SettingsViewModel.cs`
- `ViewModels\PlcIoViewModel.cs`
- `ViewModels\GraphViewModel.cs`
- `ViewModels\MenuViewModel.cs`

---

# 14. Final quick explanation for a teammate

If someone asks, "What is this project?", the short answer is:

`TempControl` is a WPF MVVM application for monitoring an oven chamber. It is structured around a shared runtime data store, a background service intended for Modbus polling and DB logging, a SQLite database for history, and multiple pages for dashboard, settings, I/O, graphs, and export. The UI structure is mostly ready, DB logging works, but real Modbus transport is still incomplete. The application currently also includes a MAC-based startup restriction in `App.xaml.cs`.

---

# 15. How to enable read / write and where to change code

This section is intended to help a developer quickly locate the exact areas that must be changed when enabling real Modbus communication.

## A. Where read operation is currently blocked

### File: `Services\ModbusPollingService.cs`

#### 1. Transport creation point
Look at the `PollGroupAsync(...)` method.

The transport creation lines are currently commented in this file around:
- `Services\ModbusPollingService.cs:96-100`

This is the place where the real transport is intended to be created and connected.

#### 2. Forced no-transport block
In the same method, the current logic that prevents real reads is around:
- `Services\ModbusPollingService.cs:102-111`

This block currently:
- marks slaves disconnected
- sets `LastPollStatus = "No transport"`
- returns from the method

This is the block that currently stops all real Modbus polling.

#### 3. Polling loop to restore
Still in `PollGroupAsync(...)`, the polling loop is currently commented around:
- `Services\ModbusPollingService.cs:113-144`

This is the section that should be enabled when real polling transport is available.

It is intended to:
- iterate through slave devices
- call `PollSlaveAsync(...)`
- update connection flags
- wait between polling cycles

#### 4. Existing read logic already present
The actual per-slave read logic already exists in:
- `Services\ModbusPollingService.cs:151-177`

Method:
- `PollSlaveAsync(...)`

This method already performs:
- holding register reads
- coil reads
- update of `PlcDataStore`

So the read mapping logic is already present; the missing part is real transport wiring and enabling the loop.

---

## B. Where transport implementation is expected

### File: `Services\IModbusService.cs`

Relevant lines:
- `Services\IModbusService.cs:9-26`

This file contains only:
- `IModbusTransport`
- `IModbusTransportFactory`

These are only interfaces.

So to enable real Modbus reads/writes, concrete implementations still need to be added for:
- RTU transport
- TCP transport
- transport factory

---

## C. Where startup wiring would need to change for real polling transport

### File: `ViewModels\MainViewModel.cs`

Relevant line:
- `ViewModels\MainViewModel.cs:49`

Current construction:
- `PollingService = new ModbusPollingService(DataStore, DbService, Dispatcher.CurrentDispatcher);`

If `ModbusPollingService` is updated to depend on a transport factory or write-capable service, this is one of the main places that would need to be changed.

---

## D. Where to enable write operation for setpoints

### File: `ViewModels\SettingsViewModel.cs`

Relevant lines:
- `ViewModels\SettingsViewModel.cs:20-24`

Method:
- `ApplySettings()`

Current behavior:
- only updates `StatusMessage`
- does not call any Modbus write logic

This is the main place where setpoint write logic would be added manually.

The intended operation here would be:
- write Zone 1 setpoint register
- write Zone 2 setpoint register
- optionally update status message after successful write

### Also check constructor wiring
Relevant line:
- `ViewModels\MainViewModel.cs:54`

Current construction:
- `SettingsVm = new SettingsViewModel(DataStore);`

If `SettingsViewModel` needs access to transport or a dedicated write service, this constructor call will also need to be updated.

---

## E. Where to change register and coil addresses

### File: `Services\PlcDataStore.cs`

#### 1. Slave configuration addresses
Method:
- `InitializeSlaveConfigs()`

Relevant lines:
- `Services\PlcDataStore.cs:92-193`

This is where configured slave IDs, COM ports, TCP endpoint, register addresses, and coil addresses are currently defined.

Examples:
- Zone 1 controller config around `115-132`
- Zone 2 controller config around `134-149`
- PLC coil definitions around `151-192`

If actual hardware addresses differ, this is one of the primary places to change them.

#### 2. Register-to-property mapping
Method:
- `UpdateRegisterValue(...)`

Relevant lines:
- `Services\PlcDataStore.cs:203-238`

This method maps raw values into:
- `Zone1Temperature`
- `Zone1Setpoint`
- `Zone1Output`
- `Zone2Temperature`
- `Zone2Setpoint`
- `Zone2Output`
- meter values

If addresses are changed in `InitializeSlaveConfigs()`, the mapping here must stay aligned.

#### 3. Coil-to-I/O mapping
Method:
- `UpdateCoilValue(...)`

Relevant lines:
- `Services\PlcDataStore.cs:246-251`

This method maps coil values into the `IoPoints` collection shown in the I/O screen.

---

## F. Important places a developer should inspect together

To enable read operation correctly, a developer should inspect these together:
- `Services\ModbusPollingService.cs`
- `Services\IModbusService.cs`
- `Services\PlcDataStore.cs`
- `ViewModels\MainViewModel.cs`

To enable write operation for setpoints correctly, a developer should inspect these together:
- `ViewModels\SettingsViewModel.cs`
- `ViewModels\MainViewModel.cs`
- `Services\IModbusService.cs`
- `Services\PlcDataStore.cs`

---

## G. Quick developer checklist

### To enable reads
1. add concrete RTU/TCP transport implementations
2. add transport factory implementation
3. wire transport into `ModbusPollingService`
4. remove the current `No transport` return block
5. restore the commented polling loop
6. verify slave IDs and register/coil addresses in `PlcDataStore`

### To enable setpoint writes
1. add write access/service into `SettingsViewModel`
2. update `ApplySettings()` to call register write operations
3. keep write addresses aligned with configured register mapping
4. update `MainViewModel` constructor wiring if new dependencies are added

### To change device addresses
1. update slave config definitions in `InitializeSlaveConfigs()`
2. update mapping logic in `UpdateRegisterValue(...)`
3. update any write logic that uses those same addresses

---

# 16. Why BasicModbusTransport.cs was created separately from IModbusService.cs

This section explains why the concrete transport classes were not placed inside `IModbusService.cs`.

---

## Reason 1: Interfaces in C# cannot contain implementation code

`IModbusService.cs` contains only interfaces:
- `IModbusTransport`
- `IModbusTransportFactory`

An interface in C# is a contract, not a class.

It can only declare:
- method signatures
- property signatures

It cannot contain:
- field declarations such as `SerialPort _port`
- method bodies such as `_port.Open()`
- constructor logic
- any actual running code

So even if the intention was to put RTU and TCP logic inside `IModbusService.cs`, the compiler would reject it because interfaces cannot hold state or logic.

---

## Reason 2: One file should have one responsibility

`IModbusService.cs` has a clear single job:
- define the contracts for what a transport can do and how one is created

`BasicModbusTransport.cs` has a different single job:
- provide the actual working implementations of those contracts

Mixing both in one file would mean:
- one file defining the rules
- the same file also playing by its own rules
- that is poor separation and makes the codebase harder to maintain and test

---

## Reason 3: RTU and TCP require completely different internal logic

The project has two connection types that work differently at a low level:

### RTU over serial port
Needs:
- `SerialPort` object
- baud rate, parity, stop bits configuration
- CRC16 checksum calculation and validation on every frame

### TCP over network
Needs:
- `TcpClient` and `NetworkStream`
- IP address and port
- MBAP header (transaction ID, protocol ID, length) on every frame

Putting both inside `IModbusService.cs` would mix:
- abstract contract definition
- RTU-specific serial port logic
- TCP-specific network socket logic

Keeping them in `BasicModbusTransport.cs` means each file stays focused on one concern.

---

## Summary in one sentence for a senior

Interfaces in C# cannot contain fields or method bodies, so the actual RTU and TCP implementations had to go in a separate concrete class file (`BasicModbusTransport.cs`) that implements the contracts defined in `IModbusService.cs`.

---

# 17. What the CreateTransport error was and how it was fixed

This section is written so a developer can explain the issue clearly to a senior.

---

## What the problem was

The project originally had two interfaces defined in `Services\IModbusService.cs`:

- `IModbusTransport`
  - defines what a transport can do
  - read registers, read coils, write registers, write coils, connect, disconnect

- `IModbusTransportFactory`
  - defines how a transport is created
  - has one method: `CreateTransport(...)`

At some point during development, `CreateTransport(...)` was accidentally added to `IModbusTransport` as well.

So `IModbusTransport` was now saying:

> "Any class that implements me must also know how to create transports."

That is wrong because:
- a transport object is supposed to represent an active connection
- a factory object is supposed to create that connection
- they are two different responsibilities

---

## Why the compiler complained

When `BasicModbusTransport` was added as the concrete transport class, it tried to implement `IModbusTransport`.

Because `IModbusTransport` had `CreateTransport(...)` incorrectly added to it, the compiler said:

> `BasicModbusTransport` does not implement `IModbusTransport.CreateTransport(...)`

The class cannot implement `CreateTransport(...)` because it is a transport, not a factory.

---

## What was fixed

The fix was to remove `CreateTransport(...)` from `IModbusTransport`.

After the fix:
- `IModbusTransport` only defines communication operations
- `IModbusTransportFactory` is the only place that defines `CreateTransport(...)`

This is the correct separation of responsibilities.

---

## What else was fixed at the same time

Three additional issues were fixed together:

### 1. No concrete transport implementation existed
Before the fix:
- `IModbusTransport` and `IModbusTransportFactory` were only interfaces
- no real class existed that could open a COM port or TCP connection

After the fix:
- `Services\BasicModbusTransport.cs` was added
- it contains `BasicModbusTransportFactory`, `RtuModbusTransport`, and `TcpModbusTransport`
- `RtuModbusTransport` opens a real COM port and reads/writes Modbus RTU frames with CRC
- `TcpModbusTransport` opens a real TCP connection and reads/writes Modbus TCP frames with MBAP header

### 2. MainViewModel was passing null into polling service
Before the fix:
- `MainViewModel` had a property called `trans` that was never initialized
- it was passing `null` into `ModbusPollingService`
- this caused a `NullReferenceException` at runtime when polling tried to use it

After the fix:
- `MainViewModel` creates a real `BasicModbusTransportFactory` instance
- that factory is passed into `ModbusPollingService`
- no more null reference at runtime

### 3. Polling loop was blocked by an early return
Before the fix:
- `PollGroupAsync(...)` had a block that marked slaves as disconnected and returned immediately
- this prevented the polling loop from ever running, even after transport was wired

After the fix:
- that placeholder block was removed
- the method now creates a transport, connects it, and runs the polling loop

### 4. System.IO.Ports assembly was missing
Before the fix:
- the project did not reference `System.IO.Ports`
- so types like `SerialPort`, `Parity`, and `StopBits` could not be found by the compiler
- this caused multiple CS1069 errors in `BasicModbusTransport.cs`

After the fix:
- `System.IO.Ports` NuGet package was added to the project
- all serial port types resolved correctly

---

## Summary in one paragraph for a senior

The `CreateTransport(...)` method was accidentally placed on the wrong interface (`IModbusTransport` instead of `IModbusTransportFactory`), which forced the new concrete transport class to implement a method it should never own. Removing it from the wrong interface resolved the contract violation. At the same time, three other gaps were closed: a concrete RTU/TCP transport implementation was added, the startup wiring in `MainViewModel` was corrected to pass a real factory instance instead of null, the placeholder early-return that was blocking the polling loop was removed, and the missing `System.IO.Ports` assembly reference was added so serial port types could compile.

---

# 17. Where to change the PLC IP address and communication settings

This section tells a developer exactly which file and which lines to change when the communication endpoint changes.

---

## PLC IP address

### File
- `Services\PlcDataStore.cs`

### Location
- method: `InitializeSlaveConfigs()`
- slave: `Slave 4: PLC (Modbus TCP)`
- line: `IpAddress = "192.168.1.100"`

### What to change
Replace the value with your actual PLC IP address.

Example:
- `IpAddress = "192.168.1.50"`

### Also check
- `TcpPort = 502`
- change this if your PLC uses a non-standard Modbus TCP port

---

## Zone 1 controller COM port and baud rate

### File
- `Services\PlcDataStore.cs`

### Location
- method: `InitializeSlaveConfigs()`
- slave: `Zone 1 Controller`
- lines: `PortName = "COM6"` and `BaudRate = 9600`

---

## Zone 2 controller COM port and baud rate

### File
- `Services\PlcDataStore.cs`

### Location
- method: `InitializeSlaveConfigs()`
- slave: `Zone 2 Controller`
- lines: `PortName = "COM1"` and `BaudRate = 9600`

---

## MFM Meter COM port and baud rate

### File
- `Services\PlcDataStore.cs`

### Location
- method: `InitializeSlaveConfigs()`
- slave: `MFM Meter`
- lines: `PortName = "COM1"` and `BaudRate = 9600`

---

## Quick checklist before first hardware connection

1. Open `Services\PlcDataStore.cs`
2. Go to `InitializeSlaveConfigs()`
3. For PLC:
   - verify `IpAddress`
   - verify `TcpPort`
4. For Zone 1 controller:
   - verify `PortName`
   - verify `BaudRate`
   - verify `SlaveId`
5. For Zone 2 controller:
   - verify `PortName`
   - verify `BaudRate`
   - verify `SlaveId`
6. For MFM meter:
   - verify `PortName`
   - verify `BaudRate`
   - verify `SlaveId`
7. Also verify register addresses match your actual device datasheet in:
   - `InitializeSlaveConfigs()` register list
   - `UpdateRegisterValue(...)` mapping

---

# Change summary for this edit

## What changed
- created `Assets\ProjectOnboardingSummary.md`
- added a new onboarding-style summary for a teammate
- documented project purpose, flow, inputs, outputs, key files, and current limitations
- included a short explanation of startup flow and the current MAC-based startup restriction
- inserted a new section: `How to enable read / write and where`
- documented exact files and line ranges to inspect for enabling Modbus reads, writes, and address changes

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

## Type of change
- documentation only
