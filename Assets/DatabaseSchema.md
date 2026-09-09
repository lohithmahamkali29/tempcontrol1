# Present Database Schema — TempControl

This file documents the **current SQLite schema actually used by the application** based on `Services\DatabaseService.cs`.

---

## Database File

- **File name:** `ovendata.db`
- **Engine:** SQLite
- **Created/initialized by:** `DatabaseService.Initialize()`
- **Location:** next to the application executable unless a different path is passed to `DatabaseService`

Connection string used:

- `Data Source=ovendata.db`

---

## Current Tables

### 1. `TemperatureLog`

This is the only database table currently created by the application.

#### SQL

```sql
CREATE TABLE IF NOT EXISTS TemperatureLog (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT    NOT NULL,
    Zone1Temp REAL    NOT NULL,
    Zone2Temp REAL    NOT NULL,
    Zone1JobPv REAL   NOT NULL DEFAULT 0,
    Zone2JobPv REAL   NOT NULL DEFAULT 0
);
```

#### Columns

| Column | Type | Null | Key | Meaning |
|---|---|---:|---|---|
| `Id` | `INTEGER` | No | Primary Key, Auto Increment | Unique row id |
| `Timestamp` | `TEXT` | No | - | Record time stored in ISO 8601 round-trip format (`timestamp.ToString("o")`) |
| `Zone1Temp` | `REAL` | No | - | Zone 1 temperature value |
| `Zone2Temp` | `REAL` | No | - | Zone 2 temperature value |
| `Zone1JobPv` | `REAL` | No | - | Zone 1 job PV / output value |
| `Zone2JobPv` | `REAL` | No | - | Zone 2 job PV / output value |

---

## Current Indexes

### 1. `IX_TemperatureLog_Timestamp`

#### SQL

```sql
CREATE INDEX IF NOT EXISTS IX_TemperatureLog_Timestamp ON TemperatureLog(Timestamp);
```

#### Purpose

This index is used to speed up date/time range queries for:
- graph page loading
- CSV export by date range

---

## Current Insert Operation

The app currently inserts records using:

```sql
INSERT INTO TemperatureLog (Timestamp, Zone1Temp, Zone2Temp, Zone1JobPv, Zone2JobPv)
VALUES ($ts, $z1, $z2, $z1Job, $z2Job)
```

### Values currently stored

| DB Column | App Source |
|---|---|
| `Timestamp` | `DateTime.Now` or supplied timestamp |
| `Zone1Temp` | current Zone 1 PV (`Zone1Temperature`) |
| `Zone2Temp` | current Zone 2 PV (`Zone2Temperature`) |
| `Zone1JobPv` | current Zone 1 JOB PV (`Zone1Output`) |
| `Zone2JobPv` | current Zone 2 JOB PV (`Zone2Output`) |

---

## Current Query Used by Graph Page

The graph page now reads data using:

```sql
SELECT Timestamp, Zone1Temp, Zone1JobPv, Zone2Temp, Zone2JobPv
FROM TemperatureLog
WHERE Timestamp >= $from AND Timestamp <= $to
ORDER BY Timestamp
```

### Meaning

The present graph page uses:
- `Zone1Temp` as Zone 1 PV
- `Zone1JobPv` as Zone 1 JOB PV
- `Zone2Temp` as Zone 2 PV
- `Zone2JobPv` as Zone 2 JOB PV

---

## Current CSV Export Format

CSV export is generated from `TemperatureLog` and currently writes:

```csv
DateTime,Zone1 Temp (°C),Zone2 Temp (°C)
```

So exported reports also currently contain only:
- Zone 1 temperature
- Zone 2 temperature

---

## What Is Not In The Current Database Yet

The following values are **not present in the current DB schema**:

| Value | Present in DB? |
|---|---|
| Zone 1 PV | Yes (`Zone1Temp`) |
| Zone 2 PV | Yes (`Zone2Temp`) |
| Zone 1 JOB PV / Output | Yes (`Zone1JobPv`) |
| Zone 2 JOB PV / Output | Yes (`Zone2JobPv`) |
| Setpoint values | No |
| Elapsed time | No |
| PLC status bits | No |

---

## Present Schema Summary

### Simple view

```text
ovendata.db
└── TemperatureLog
    ├── Id
    ├── Timestamp
    ├── Zone1Temp
    ├── Zone2Temp
    ├── Zone1JobPv
    └── Zone2JobPv
```

### In words

The present database is very simple:
- one table
- one timestamp column
- four logged trend columns
- one index on timestamp

---

## If Graph Is Expanded Later

The graph schema has now been extended so both PV and JOB PV can be stored and plotted.

For existing old databases, the application adds missing columns automatically during startup using schema migration logic in `DatabaseService.Initialize()`.

---

## Source of Truth

This markdown reflects the current schema defined in:
- `Services\DatabaseService.cs`

If `DatabaseService.Initialize()` changes later, this file should also be updated.
