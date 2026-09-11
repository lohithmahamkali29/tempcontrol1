# Temperature Display and Scaling Fixes — 2026-09-11

## Summary

This session covered two categories of work:

1. **Fixed** — a Home-page display bug (large numbers clipped) and two data-scaling bugs (Job PV and Manual-page setpoint reading/writing 10x off).
2. **Diagnosed, not fixed** — a PLC connection-limit issue (not a code defect) and a Process Parameters → Home setpoint mismatch that traces to PLC ladder logic, outside this application's code.

Build status after all changes: `dotnet build` succeeds, **0 errors**. `git diff --check` clean. Nothing has been committed.

---

## 1. Home page — large PV values were being clipped

**File:** `Views/HomeView.xaml`

**Problem:** The ZONE 1 / ZONE 2 "PV" value boxes sat in `*`-width grid columns inside a fixed `Width="550"` card, and the Set Temperature box had a hardcoded `Width="100"`. Once a reading grew past ~4 digits, the box couldn't grow and WPF silently clipped it (confirmed visually — a value of `123456.7` rendered as `3456.`, cut on both edges).

**Fix:**
- PV value columns: `*` → `Auto` width, centered, so each box sizes to its own text instead of a fixed share of the card.
- Card `Width="550"` → `MinWidth="550"` (both ZONE 1 and ZONE 2 cards) — same size for normal values, can grow if content needs more.
- Set Temperature `TextBox`: removed the hardcoded `Width="100"`.
- Wrapped each zone card's content in `<Viewbox Stretch="Uniform" StretchDirection="DownOnly">` as a safety net for narrow windows — the card renders at full original size until a value genuinely wouldn't fit, then scales the whole block down proportionally (font, spacing, everything together) instead of clipping a digit.

**Verified:** built a throwaway offscreen-WPF harness loading the real `HomeView.xaml` with a real `PlcDataStore`, fed values from 2-digit through 7-digit raw registers at six window sizes (1920×1080 down to 1000×650). All three `/10`-converted, `F1`-formatted displays (Zone1Temperature, Zone2Temperature, Zone1Setpoint) render in full with exactly one decimal digit at every size tested; 0 clipping on those three fields (previously clipping at ≤1366px).

**Not part of this fix (pre-existing, confirmed via the same harness against the original file):** the TIMER card's "Set Time"/"Elp Time" values and the "PROCESS STEP NUMBER" label also clip at narrow widths — those use `StringFormat=F0` on raw (non-`/10`) values, unrelated to this fix, left untouched.

---

## 2. Job PV showing 10x too high (Home "JOB TEMPERATURE" + Graph blue line)

**File:** `Services/PlcDataStore.cs`

**Symptom:** Zone 1/2 "JOB TEMPERATURE" on Home, and the Graph's "Job PV" line (including its cursor/ruler label), showed values 10x the real reading — e.g. Zone 2 PV = `32.1°C` but Job Temperature = `301.0°C` on the same card.

**Root cause:** D123 and D125 (Job PV registers) are read the same way as D100/D120/D121 (which are already correctly divided by 10) but were left as raw integers.

**Fix — `Services/PlcDataStore.cs`, `UpdateRegisterValue`:**

```csharp
// before
case 123: Zone1Output = rawValue; break;
case 125: Zone2Output = rawValue; break;

// after
case 123: Zone1Output = rawValue/10.0; break; // D123 — tenths-scaled like D120/D121/D100
case 125: Zone2Output = rawValue/10.0; break; // D125 — tenths-scaled like D123
```

D123 was fixed first (single, explicit approval from the user); D125 was confirmed broken the same way from a live screenshot and fixed to match.

Both registers are read-only in this app (never written back anywhere) — verified by grep across `ViewModels/*.cs` and `Services/*.cs` — so no corresponding write-side change was needed.

**Downstream effect (expected, not a separate change):** `Zone1Output`/`Zone2Output` also feed the SQLite `Zone1JobPv`/`Zone2JobPv` columns, CSV export, and PDF export — those now carry correctly-scaled values too, since it's the same shared property.

---

## 3. Manual page setpoint showing 10x too high, plus write-back risk

**Files:** `Services/PlcDataStore.cs`, `ViewModels/ManualPageViewModel.cs`

**Symptom:** Manual page's "Zone 1 and 2 Temp Set Point" field showed `500.0` while Home's "SET TEMPERATURE" (a different register, D100, already correctly scaled) showed `50.0` for what is the same real setpoint — confirmed from a live screenshot with the PLC connected.

**Root cause:** D325 (`Zone1SetPointValueManual`) was read as a raw integer (comment claimed "plain 16-bit int from controller"), but live data showed it's actually tenths-scaled like D100.

**Fix, read side — `Services/PlcDataStore.cs`:**

```csharp
// before
case 325: Zone1SetPointValueManual = rawValue; break; // D325 — Zone 1 Temp Setpoint (plain 16-bit int from controller)

// after
case 325: Zone1SetPointValueManual = rawValue/10.0; break; // D325 — Zone 1 Manual Setpoint, tenths-scaled like D100
```

**Fix, write side — `ViewModels/ManualPageViewModel.cs`, `UpdateAsync`:**

D325 is also *written* when a Supervisor edits it and clicks Update. Fixing only the read side would mean an on-screen `50.0` round-trips back out as raw `50` the moment Update is clicked — silently setting the real setpoint to `5.0°C` instead of `50.0°C`, even with no change made to the value. Multiplied by 10 on the way out to match:

```csharp
// before
(325, Zone1SetPointValue,                "Zone 1 Setpoint"),

// after
(325, Zone1SetPointValue * 10.0,         "Zone 1 Setpoint"), // D325 is tenths-scaled (matches PlcDataStore's rawValue/10.0 on read)
```

**Deliberately left untouched (no evidence found, or evidence pointed elsewhere):**
- D327 / D328 (Zone 1/2 Safety Temperature, Manual page) — screenshot showed `200.0` matching a plausible value, no contradiction found against another source.
- D326 (Zone 2's half of "Zone 1 and 2 Temp Set Point") — the Manual page only has one textbox wired to `Zone1SetPointValue`; there's no UI element showing `Zone2SetPointValue` to compare against, so there's no direct evidence either way. D326 also writes into the same `Zone2Setpoint` property as D103 (`case 103: Zone2Setpoint = rawValue;`, also unscaled) — a separate, pre-existing entanglement that needs its own investigation before touching.

---

## 4. Open issue — Process Parameters "Set Recipe" sets Home's setpoint to 1/10th

**Status: diagnosed, not a code fix — needs PLC ladder-logic investigation.**

**Symptom:** Using Process Parameters (Settings page) to set Zone 1 temp to `70` results in Home's "SET TEMPERATURE" showing `7.0` instead of `70.0`.

**What the evidence shows** (from `Logs\debug_20260911.txt`, all 4 times this was tested today — 15:46:57, 15:51:36, 15:54:27, 15:57:08 — same result every time, no exceptions):

```
[SETTINGS-VM] UpdateAsync starting: S1Z1=70, ...
[FINS-WRITE ] WriteRegister D301 = 70      <- app writes D301 as a plain integer (70 = 70°C), matching
                                               the five predefined recipes' stored convention
...a few seconds later...
[FINS-READ  ] OK D100 = [70]               <- D100 becomes a 1:1 raw copy of D301, not raw 700
```

`PlcDataStore.cs`'s `case 100: Zone1Setpoint = rawValue/10.0;` is unchanged and correct for D100's normal (sensor-driven) behavior. The mismatch is that after a "Set Recipe" write, D100 appears to be driven by a **direct, unscaled copy of D301** rather than a properly tenths-scaled live SV — something the PLC's own ladder logic does when propagating a recipe value into the running setpoint, not something this C# application controls.

**Why this wasn't changed:** `D301` (and the sibling step/safety/blower registers D302–D324) are written as plain integers, matching the app's long-standing, already-correct convention for recipe data — the same values are used by the SELECT/predefined-recipe flow and match the documented five recipes (e.g. `STATOR PREHEATING` = 70°C, stored as `70`). Changing what the app writes to "fix" this would risk breaking the already-working recipe-apply flow, for a mismatch that actually originates in what the PLC ladder does with D301 after receiving it — outside this codebase. This needs to be checked in the Omron ladder program (CX-Programmer/Sysmac Studio), specifically how a recipe/step temperature value is converted into the controller's live SV register.

**Also confirmed not a regression:** the four `case 100`/`SettingsViewModel.cs` code paths involved here were never touched in this session, and the log shows this exact behavior was 100% consistent across all four tests run today — not something that started partway through today's session.

---

## 5. PLC "Exceeding connection limit" — diagnosed, not a code issue

**Status: explained, no code change made.**

Observed: several consecutive app restarts (7 launches in ~26 minutes) produced escalating connect failures — plain 10s timeouts at first, then explicit `Exceeding connection limit` from the PLC's own FINS/TCP session table.

Confirmed via `git log` that `Services/OmronFinsTcpTransport.cs` and `Services/ModbusPollingService.cs` (the actual connection code) have not been modified since the repository's first commit — nothing in this session's changes touches connection handling. The two existing mitigations already in the code (`Connection_Pool_Exhaustion_Fix.md`'s one-time `ConnectClose()` reset, and `ModbusPollingService`'s 5s/15s/30s backoff) are both present and working as designed.

Most likely explanation: repeated app restarts during testing, if any weren't a clean shutdown (`App.OnExit` → `Dispose()` → `transport.DisconnectAsync()`), each leave a session open on the PLC's side for roughly 1–3 minutes. Restarting again before that clears adds another, and enough of those in a short window exhausts the PLC's own connection table. Recommended: avoid rapid restarts; if a genuinely idle wait doesn't clear it, check for a second client (Omron programming software, another HMI) holding a connection to the same PLC.

---

## Files changed in this session

| File | What changed |
|---|---|
| `Views/HomeView.xaml` | Zone card layout (Auto columns, `MinWidth`, `Viewbox` safety scaling) so large PV values are never clipped. |
| `Services/PlcDataStore.cs` | `case 123`, `case 125` (Job PV), `case 325` (Manual setpoint) — added `/10.0` to match the PLC's tenths scaling. |
| `ViewModels/ManualPageViewModel.cs` | D325 write path — added `* 10.0` so Update doesn't corrupt the real PLC setpoint by 10x. |

## Files explicitly not changed

- `Services/OmronFinsTcpTransport.cs`, `Services/ModbusPollingService.cs` — connection/transport logic, untouched.
- `Services/DatabaseService.cs`, `Services/RunSessionService.cs` — untouched; they already carry Job PV values through correctly once the source is fixed.
- `ViewModels/SettingsViewModel.cs` — Process Parameters write logic, untouched (see §4 — the mismatch there is PLC-side, not app-side).
- `Services/PlcDataStore.cs` cases 326, 327, 328 — no evidence found of a scaling issue; left as-is pending further verification.

## Verification performed

- `dotnet build --no-incremental` — clean, **0 errors**, same pre-existing warnings as before any of these changes (no new warnings introduced).
- `git diff --check` — clean, no whitespace errors.
- Offscreen WPF harness rendering the real `HomeView.xaml` against a real `PlcDataStore`, across six window sizes, confirmed no clipping on the three `/10`-scaled display fields.
- Log-file cross-checks against a live, PLC-connected session (`Logs\debug_20260911.txt`) for D100, D120, D123, D125, D301, D325 raw values, used to confirm/rule out each fix rather than guessing.

Nothing in this session has been committed to git.
