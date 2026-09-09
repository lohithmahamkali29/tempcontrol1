# PLC Handoff Summary — TempControl

This is the short handoff version for the PLC programmer.

---

## PLC Connection Used by the Software

- **Protocol:** Omron `FINS/TCP`
- **PLC IP:** `192.168.250.1`
- **Port:** `9600`
- **Software slave id:** `4`

---

## D Registers Used by the Software

### Main runtime values (polled — read by software)

| D Register | Purpose in software | Scaling |
|---|---|---|
| `D100` | Zone 1 Temperature (PV) | raw integer |
| `D101` | Zone 2 Temperature (PV) | raw integer |
| `D123` | Zone 1 Job Thermocouple PV | raw integer |
| `D125` | Zone 2 Job Thermocouple PV | raw integer |
| `D325` | Zone 1 Setpoint (read-back after manual write) | raw integer |
| `D326` | Zone 2 Setpoint (read-back after manual write) | raw integer |
| `D327` | Zone 1 Safety Temperature (read-back) | raw integer |
| `D328` | Zone 2 Safety Temperature (read-back) | raw integer |

### Run / Stop command (written by software)

| D Register | Value | Meaning |
|---|---:|---|
| `D300` | `1` | Run started |
| `D300` | `0` | Run stopped |

### Current process step display (polled — read by software)

| D Register | Value | Meaning |
|---|---:|---|
| `D400` | `1–5` | Current active step number shown in nav bar |
| `D400` | `0` | No active step — nav bar shows `--` |

### Process runtime registers (polled — read by software)

| D Register | Purpose |
|---|---|
| `D401` | Elapsed Time (minutes) |
| `D402` | Set Time / Soak Time (minutes) |

### Settings page — per-step parameters (written and polled by software)

5 steps × 4 values each. Written on "Update" button press; also polled back continuously.

| D Register | Field |
|---|---|
| `D301` | Step 1 — Zone 1 Temp |
| `D302` | Step 1 — Zone 2 Temp |
| `D303` | Step 1 — Soak Time |
| `D304` | Step 1 — Ramp Rate |
| `D305` | Step 2 — Zone 1 Temp |
| `D306` | Step 2 — Zone 2 Temp |
| `D307` | Step 2 — Soak Time |
| `D308` | Step 2 — Ramp Rate |
| `D309` | Step 3 — Zone 1 Temp |
| `D310` | Step 3 — Zone 2 Temp |
| `D311` | Step 3 — Soak Time |
| `D312` | Step 3 — Ramp Rate |
| `D313` | Step 4 — Zone 1 Temp |
| `D314` | Step 4 — Zone 2 Temp |
| `D315` | Step 4 — Soak Time |
| `D316` | Step 4 — Ramp Rate |
| `D317` | Step 5 — Zone 1 Temp |
| `D318` | Step 5 — Zone 2 Temp |
| `D319` | Step 5 — Soak Time |
| `D320` | Step 5 — Ramp Rate |

### Settings page — global process parameters (written and polled by software)

| D Register | Field |
|---|---|
| `D321` | Zone 1 Safety Temperature |
| `D322` | Zone 2 Safety Temperature |
| `D323` | Blower 1 Setting |
| `D324` | Blower 2 Setting |

### Manual page writes (written by software)

| D Register | Field |
|---|---|
| `D325` | Zone 1 Temp Setpoint |
| `D326` | Zone 2 Temp Setpoint |
| `D327` | Zone 1 Safety Temperature |
| `D328` | Zone 2 Safety Temperature |

### Energy / MFM meter registers (polled — read by software, 32-bit pairs)

Each value spans two consecutive 16-bit registers: `D[n]` = LSW (low word), `D[n+1]` = MSW (high word).

| D Register Pair | Signal | Scaling |
|---|---|---|
| `D126` / `D127` | R-Phase Voltage | ÷ 10 (e.g. 4134 → 413.4 V) |
| `D128` / `D129` | Y-Phase Voltage | ÷ 10 |
| `D130` / `D131` | B-Phase Voltage | ÷ 10 |
| `D132` / `D133` | R-Phase Current | ÷ 100 (e.g. 1900 → 19.00 A) |
| `D134` / `D135` | Y-Phase Current | ÷ 100 |
| `D136` / `D137` | B-Phase Current | ÷ 100 |
| `D138` / `D139` | Total Power (kW) | ÷ 10 (e.g. 2280 → 228.0 kW) |
| `D140` / `D141` | Zone 1 R-Phase Current | ÷ 100 |
| `D142` / `D143` | Zone 1 Y-Phase Current | ÷ 100 |
| `D144` / `D145` | Zone 1 B-Phase Current | ÷ 100 |
| `D160` / `D161` | Zone 2 R-Phase Current | ÷ 100 |
| `D162` / `D163` | Zone 2 Y-Phase Current | ÷ 100 |
| `D164` / `D165` | Zone 2 B-Phase Current | ÷ 100 |

---

## CIO Bits Used by the Software

All Modbus coil addresses below use the Omron FINS/TCP bit-address scheme:
- CIO area bits: base address `10000` (e.g. `10000` = CIO0.0, `10016` = CIO1.0)
- Output coil bits: base address `0` (e.g. `1600` = CIO100.0)
- W area bits: base address `10000 + word×16` (e.g. `10640` = W40.0, `10704` = W44.0)

### Digital input bits (polled — read by software)

| Modbus Address | CIO Bit | Signal |
|---:|---|---|
| `10000` | `CIO0.0` | Single Phase Preventer |
| `10001` | `CIO0.1` | Emergency Switch |
| `10002` | `CIO0.2` | Door Limit Switch Close |
| `10003` | `CIO0.3` | Electrical Blower motor-1 |
| `10004` | `CIO0.4` | Electrical Blower motor-2 |
| `10005` | `CIO0.5` | Electrical Exhaust Blower Motor |
| `10006` | `CIO0.6` | VFD-1 Rotary Motor |
| `10007` | `CIO0.7` | VFD-1 Trolley Motor |
| `10008` | `CIO0.8` | Door Limit Switch Open |
| `10009` | `CIO0.9` | Trolley IN |
| `10010` | `CIO0.10` | Trolley OUT |
| `10011` | `CIO0.11` | Zone-1 Temp. Safety |
| `10016` | `CIO1.0` | Zone-2 Temp. Safety |
| `10017` | `CIO1.1` | Blower-1 Cont. ON |
| `10018` | `CIO1.2` | Blower-2 Cont. ON |
| `10019` | `CIO1.3` | Heater-1 Cont. ON |
| `10020` | `CIO1.4` | Heater-2 Cont. ON |
| `10021` | `CIO1.5` | Exhaust Blower motor Cont. ON |
| `10022` | `CIO1.6` | Rotary Motor Cont. ON |
| `10023` | `CIO1.7` | Trolley Motor Cont. ON |
| `10704` | `W44.0` | Process Run/Stop live feedback |

### Digital output bits (polled — read by software)

| Modbus Address | CIO Bit | Signal |
|---:|---|---|
| `1600` | `CIO100.0` | Collection fault |
| `1601` | `CIO100.1` | Heater-1 Cont. |
| `1602` | `CIO100.2` | Heater-2 Cont. |
| `1603` | `CIO100.3` | Blower motor-1 Cont. |
| `1604` | `CIO100.4` | Blower motor-2 Cont. |
| `1605` | `CIO100.5` | Exhaust Blower Motor |
| `1606` | `CIO100.6` | Tower Light — RED |
| `1607` | `CIO100.7` | Tower Light — GREEN |
| `1608` | `CIO100.8` | Tower Light — YELLOW |

### Manual command bits (written by software — pulse on button press)

| Modbus Address | W Bit | Function |
|---:|---|---|
| `10640` | `W40.0` | Blower-1 ON / OFF |
| `10641` | `W40.1` | Blower-2 ON / OFF |
| `10642` | `W40.2` | Heater-1 ON / OFF |
| `10643` | `W40.3` | Heater-2 ON / OFF |

---

## Alarm Signals Used by the App

### Alarm when ON
- `10011` (`CIO0.11`) → Zone 1 Temp Safety
- `10016` (`CIO1.0`) → Zone 2 Temp Safety

### Alarm when OFF
- `10000` (`CIO0.0`) → Single Phase Preventer
- `10001` (`CIO0.1`) → Emergency Switch
- `10002` (`CIO0.2`) → Door Limit Switch Close
- `10003` (`CIO0.3`) → Electrical Blower motor-1
- `10004` (`CIO0.4`) → Electrical Blower motor-2
- `10021` (`CIO1.5`) → Exhaust Blower motor Cont. ON
- `10005` (`CIO0.5`) → Electrical Exhaust Blower Motor

---

## Give This To PLC Programmer

Please confirm with the PLC programmer:

- that `D100` / `D101` are the final Zone 1 / Zone 2 temperature PV registers
- that `D123` / `D125` are the Zone 1 / Zone 2 job thermocouple PV registers
- that `D301–D324` are the approved final settings addresses (5 steps × 4 params + 4 global)
- that `D325–D328` are the manual setpoint and safety temperature write addresses
- that `D300` is used as the run/stop handshake register (1 = run, 0 = stop)
- that `D400` is the current active process step register (0 = none, 1–5 = active step)
- that `D126–D165` are the MFM energy meter 32-bit register pairs with the scaling shown above
- that `W40.0–W40.3` (Modbus addresses `10640–10643`) are the manual command bits
- that `W44.0` (Modbus address `10704`) is the process run/stop live feedback bit

---

## Related Detailed File

For the full software-audited version, see:
- `Assets\PlcRegisterMap.md`
