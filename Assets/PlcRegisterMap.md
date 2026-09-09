# PLC Register & I/O Map — TempControl Application
**Device:** Omron PLC  
**Protocol:** FINS/TCP  
**PLC IP:** `192.168.250.1`  
**Port:** `9600`  
**Slave ID:** `4`  
**Audit Basis:** Current application code in `PlcDataStore`, `ModbusPollingService`, `RunSessionService`, `ManualControlService`, `SettingsViewModel`, and `OmronFinsTcpTransport`  
**Last Updated:** 2025

---

## 1. Executive Summary

This file reflects what the **software is actually configured to do today**.

Important for the PLC programmer:

- The app connects to the PLC using **Omron FINS over TCP**, not Modbus TCP.
- The PLC slave configured in software is `SlaveId = 4`.
- The polling service reads **every configured register and coil individually** every `1000 ms`.
- Some addresses are:
  - **configured and polled**,
  - some are **written by the app**,
  - and some are **configured but not currently mapped back into typed app properties**.
- There is currently a **software mismatch** around some settings registers, so this document calls out those cases explicitly.

---

## 2. FINS Address Translation Used by the App

### Holding registers
Software address `N` maps directly to:
- `D{N}`

Examples:
- address `300` → `D300`
- address `402` → `D402`

### Coils / bits
The app converts logical coil addresses into Omron CIO bits like this:

#### Input/feedback area
- address `0–99` → `CIO{address / 16}.{address % 16}`

Examples:
- `0` → `CIO0.0`
- `10` → `CIO0.10`
- `16` → `CIO1.0`

#### Output word 100
- address `100–115` → `CIO100.{address - 100}`

Examples:
- `100` → `CIO100.0`
- `105` → `CIO100.5`

#### Manual command word 400
- address `400–415` → `CIO400.{address - 400}`

Examples:
- `400` → `CIO400.0`
- `407` → `CIO400.7`

---

## 3. PLC Registers Configured in Software

These are the PLC `D` registers currently listed under the PLC slave configuration in `Services\PlcDataStore.cs`.

### 3.1 Configured PLC holding registers

| D Register | Software Address | Present in PLC slave config | Notes |
|---|---:|---|---|
| D100–D130 | 100–130 | Yes | Polled every cycle |
| D301–D325 | 301–325 | Yes | Polled every cycle; D301–D320 also written from `Settings` page; D321–D324 written from `Settings` page (global fields); D325 is read-only current process step |
| D400–D403 | 400–403 | Yes | Polled every cycle; also written from `Manual` page |

---

## 4. Registers Actually Mapped Into Runtime App Properties

These are the `D` registers that are currently handled in `PlcDataStore.UpdateRegisterValue()` for PLC `SlaveId = 4`.

### 4.1 Active runtime-mapped PLC registers

| D Register | Software Address | App Property | Meaning in Software | Scale | Status |
|---|---:|---|---|---|---|
| D102 | 102 | `Zone1Setpoint` | Zone 1 setpoint | ÷ 10 | Active runtime mapping |
| D103 | 103 | `Zone2Setpoint` | Zone 2 setpoint | ÷ 10 | Active runtime mapping |
| D104 | 104 | `Zone1Output` | Zone 1 output / job PV | ÷ 10 | Active runtime mapping |
| D105 | 105 | `Zone2Output` | Zone 2 output / job PV | ÷ 10 | Active runtime mapping |
| D120 | 120 | `Zone1Temperature` | Zone 1 process value | ÷ 10 | Active runtime mapping |
| D121 | 121 | `Zone2Temperature` | Zone 2 process value | ÷ 10 | Active runtime mapping |
| D400 | 400 | `Zone1Setpoint` | Manual page Zone 1 temp set point | ÷ 10 | Active runtime mapping |
| D401 | 401 | `Zone2Setpoint` | Manual page Zone 2 temp set point | ÷ 10 | Active runtime mapping |
| D402 | 402 | `Zone1SafetyTemperature` | Manual page Zone 1 safety temperature | ÷ 10 | Active runtime mapping |
| D403 | 403 | `Zone2SafetyTemperature` | Manual page Zone 2 safety temperature | ÷ 10 | Active runtime mapping |

### 4.2 Configured/polled but **not** currently mapped in `UpdateRegisterValue()`

These addresses are listed in the PLC config and are polled by `ModbusPollingService`, but the software currently does **not** process them into typed properties in `UpdateRegisterValue()`.

| D Register | Software Address | Current software state | Notes |
|---|---:|---|---|
| D100 | 100 | Polled but not mapped | Name in config says `Zone 1 Temperature`, but no runtime case exists |
| D101 | 101 | Polled but not mapped | Name in config says `Zone 2 Temperature`, but no runtime case exists |
| D106–D119 | 106–119 | Polled but not mapped | No runtime cases |
| D122–D130 | 122–130 | Polled but not mapped | No runtime cases |
| D301–D314 | 301–314 | Polled but not mapped | `Settings` page writes these, but readback is not mapped in `UpdateRegisterValue()` |

### Important software note about settings registers

The `Settings` page writes:
- `D301–D310` for the 5 preset steps (`Temp`, `Time`)
- `D311` for `RampRate`
- `D312` for `SoakTime`
- `D313` for `Zone1SafetyTemperature`
- `D314` for `Zone2SafetyTemperature`

However, **those addresses do not currently have PLC readback mapping in `PlcDataStore.UpdateRegisterValue()`**.

That means:
- the app **does write them to the PLC**,
- the polling service **does read them back from the PLC**,
- but the current software **does not map those polled values back into typed properties**.

---

## 5. Registers Written by the Application

These are PLC `D` registers that the app writes intentionally.

### 5.1 Run/Stop register

| D Register | Value | Trigger | Purpose |
|---|---:|---|---|
| D300 | `1` | Run started | PLC run/session start signal |
| D300 | `0` | Run stopped | PLC run/session stop signal |

### 5.2 Settings page writes

These writes happen when the operator clicks `Apply Settings` in `Settings`.

| D Register | Settings field |
|---|---|
| D301 | Step 1 Temp |
| D302 | Step 1 Time |
| D303 | Step 2 Temp |
| D304 | Step 2 Time |
### 5.2 Settings page writes

These writes happen when the operator clicks `Enter` in `Settings` (Process Parameters page).

| D Register | Step | Field |
|---|---|---|
| D301 | Step 1 | Zone 1 Temp (°C) |
| D302 | Step 1 | Zone 2 Temp (°C) |
| D303 | Step 1 | Soak Time (min) |
| D304 | Step 1 | Ramp Rate (°C/min) |
| D305 | Step 2 | Zone 1 Temp (°C) |
| D306 | Step 2 | Zone 2 Temp (°C) |
| D307 | Step 2 | Soak Time (min) |
| D308 | Step 2 | Ramp Rate (°C/min) |
| D309 | Step 3 | Zone 1 Temp (°C) |
| D310 | Step 3 | Zone 2 Temp (°C) |
| D311 | Step 3 | Soak Time (min) |
| D312 | Step 3 | Ramp Rate (°C/min) |
| D313 | Step 4 | Zone 1 Temp (°C) |
| D314 | Step 4 | Zone 2 Temp (°C) |
| D315 | Step 4 | Soak Time (min) |
| D316 | Step 4 | Ramp Rate (°C/min) |
| D317 | Step 5 | Zone 1 Temp (°C) |
| D318 | Step 5 | Zone 2 Temp (°C) |
| D319 | Step 5 | Soak Time (min) |
| D320 | Step 5 | Ramp Rate (°C/min) |
| D321 | Global | Zone 1 Safety (°C) |
| D322 | Global | Zone 2 Safety (°C) |
| D323 | Global | Blower 1 |
| D324 | Global | Blower 2 |

> **D325** — Current Process Step (read-only, written by PLC ladder).

---

## 6. PLC Coils / CIO Bits Configured in Software

### 6.1 Digital inputs / feedbacks

These are configured under the PLC slave, polled every cycle, and shown on the PLC I/O page.

| Coil Address | CIO Bit | Signal Name |
|---|---|---|
| 0 | CIO0.0 | Single Phase Preventer |
| 1 | CIO0.1 | Emergency Switch |
| 2 | CIO0.2 | Door Limit Switch Close |
| 3 | CIO0.3 | Electrical Blower motor-1 |
| 4 | CIO0.4 | Electrical Blower motor-2 |
| 5 | CIO0.5 | VFD-1 Rotary Motor |
| 6 | CIO0.6 | VFD-1 Trolley Motor |
| 7 | CIO0.7 | Door Limit Switch Open |
| 8 | CIO0.8 | Trolley IN |
| 9 | CIO0.9 | Trolley OUT |
| 10 | CIO0.10 | Zone-1 Temp. Safety |
| 11 | CIO0.11 | Zone-2 Temp. Safety |
| 12 | CIO0.12 | Blower-1 Cont. ON |
| 13 | CIO0.13 | Blower-2 Cont. ON |
| 14 | CIO0.14 | Heater-1 Cont. ON |
| 15 | CIO0.15 | Heater-2 Cont. ON |
| 16 | CIO1.0 | Exhaust Blower motor Cont. ON |
| 17 | CIO1.1 | Rotary Motor Cont. ON |
| 18 | CIO1.2 | Trolley Motor Cont. ON |
| 19 | CIO1.3 | Electrical Exhaust Blower Motor |

### 6.2 PLC output status coils

These are configured under the PLC slave, polled every cycle, and shown on the PLC I/O page.

| Coil Address | CIO Bit | Signal Name |
|---|---|---|
| 100 | CIO100.0 | Collection fault |
| 101 | CIO100.1 | Heater-1 Cont. |
| 102 | CIO100.2 | Heater-2 Cont. |
| 103 | CIO100.3 | Blower motor-1 Cont. |
| 104 | CIO100.4 | Blower motor-2 Cont. |
| 105 | CIO100.5 | Exhaust Blower Motor |

### 6.3 Manual command coils written by the app

These are written from the `Manual` page and also configured under the PLC slave for polling.

| Coil Address | CIO Bit | Manual action |
|---|---|---|
| 400 | CIO400.0 | Blower-1 ON |
| 401 | CIO400.1 | Blower-1 OFF |
| 402 | CIO400.2 | Blower-2 ON |
| 403 | CIO400.3 | Blower-2 OFF |
| 404 | CIO400.4 | Heater-1 ON |
| 405 | CIO400.5 | Heater-1 OFF |
| 406 | CIO400.6 | Heater-2 ON |
| 407 | CIO400.7 | Heater-2 OFF |

### Important software note about manual command coils

- The PLC slave config **does include** coils `400–407`.
- `UpdateCoilValue()` does use them to update:
  - `Blower1ManualStatus`
  - `Blower2ManualStatus`
  - `Heater1ManualStatus`
  - `Heater2ManualStatus`
- But the corresponding `IoPoints` entries for `400–407` are currently commented out in `PlcDataStore.InitializeIoPoints()`.

So:
- the manual page **does use** these coils,
- but they are **not currently listed on the PLC I/O screen**.

---

## 7. Alarm-Related PLC Signals Used by the App

The alarm system reuses existing PLC I/O points. These are the alarm sources currently tracked in software:

### Alarm when signal is `ON`
- coil `10` → `Zone-1 Temp. Safety`
- coil `11` → `Zone-2 Temp. Safety`

### Alarm when signal is `OFF`
- coil `0` → `Single Phase Preventer`
- coil `1` → `Emergency Switch`
- coil `2` → `Door Limit Switch Close`
- coil `3` → `Electrical Blower motor-1`
- coil `4` → `Electrical Blower motor-2`
- coil `16` → `Exhaust Blower motor Cont. ON`
- coil `19` → `Electrical Exhaust Blower Motor`

These are app-side alarm rules only; there are no dedicated PLC alarm-acknowledge bits defined in software at this time.

---

## 8. What the Polling Service Actually Does

`ModbusPollingService` does **not** read one big fixed range anymore.

Current behavior:
- it iterates through every configured `HoldingRegisters` entry and reads them **one by one**
- it iterates through every configured `Coils` entry and reads them **one by one**
- PLC poll interval is currently `1000 ms`

So for the PLC slave, the software currently polls:
- `D100–D130`
- `D301–D314`
- `D400–D403`
- coils `0–19`
- coils `100–105`
- coils `400–407`

---

## 9. Current Software Gaps / Things to Confirm With PLC Programmer

These points should be reviewed before final PLC handoff is frozen:

1. **Zone setpoint source conflict**
   - runtime PLC mapping uses `D102` / `D103`
   - manual page uses `D400` / `D401`

2. **Safety temperature source conflict**
   - settings page writes `D313` / `D314`
   - manual page uses `D402` / `D403`
   - runtime readback currently exists only for `D402` / `D403`

3. **Settings readback gap**
   - `D301–D314` are polled and written
   - but current `UpdateRegisterValue()` does not map them back into properties

4. **Legacy names in PLC config**
   - `D100` and `D101` are named as temperatures in the slave config
   - but runtime software does not map them as temperatures

5. **Manual command coils not visible on PLC I/O page**
   - coils `400–407` are real in the software
   - but their `IoPoints` are commented out, so the PLC I/O page does not show them

6. **Current process step — D315 — now mapped**
   - `D315` is polled and mapped to `CurrentProcessStep` / `CurrentProcessStepDisplay`
   - the nav-bar badge shows the live step number `1–5`
   - when `D315 = 0` the badge shows `--` (idle / no active step)

---

## 10. Recommended PLC Handoff Summary

If you need to brief the PLC programmer quickly, this is the current software expectation:

### Definite addresses already used by the software
- `D300` → run/stop command
- `D102`, `D103` → current runtime setpoint mapping
- `D104`, `D105` → output / job PV mapping
- `D120`, `D121` → process value mapping
- `D301–D314` → settings page write targets
- `D400–D403` → manual page write targets
- `CIO0.0–CIO1.3` → input/feedback points used by PLC I/O and alarms
- `CIO100.0–CIO100.5` → output status points used by PLC I/O
- `CIO400.0–CIO400.7` → manual ON/OFF command bits

### Best immediate review items with PLC programmer
- confirm whether the real setpoints should be `D102/103` or `D400/401`
- confirm whether safety temps should be `D313/314` or `D402/403`
- confirm whether `D301–D314` are the final intended step/settings addresses

---

## 11. Source of Truth in Code

This document was audited from:

- `Services\PlcDataStore.cs`
- `Services\ModbusPollingService.cs`
- `Services\RunSessionService.cs`
- `Services\ManualControlService.cs`
- `Services\OmronFinsTcpTransport.cs`
- `ViewModels\SettingsViewModel.cs`

---

*This markdown is now intended as the PLC handoff note for the current TempControl software behavior, including current mismatches that still need PLC/software alignment.*
