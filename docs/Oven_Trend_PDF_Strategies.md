# Oven Trend PDF – Two Strategies

## Goal

The customer wants the oven trend graph to be saved as a PDF.

The PDF trend should contain exactly these 3 lines:

1. Zone 1 Temperature
2. Zone 2 Temperature
3. Zone 1 Setpoint

No premium reporting software should be required.

---

# Strategy 1 – Save Data Every 1 Minute

## Recommended for the current requirement

Instead of recording every 1 second, record the trend values every **1 minute**.

### Data flow

```text
PLC / DataStore
      |
      | every 1 minute
      v
SQLite
      |
      +----> Graph
      |
      +----> PDF
```

### Data stored

Every minute:

```text
Timestamp
Zone1Temperature
Zone2Temperature
Zone1Setpoint
```

For 24 hours:

```text
24 × 60 = 1,440 points per line
```

For 3 lines:

```text
1,440 × 3 = 4,320 plotted values
```

The PDF does **not** print all these values as text. The values are used to draw continuous graph lines.

### PDF appearance

A 24-hour trend can be shown as a normal landscape trend graph with:

- Recipe name
- Date
- Time range
- Temperature axis
- Time axis
- Zone 1 Temperature
- Zone 2 Temperature
- Zone 1 Setpoint

Optional summary:

```text
Description             Min       Max       Average
Zone 1 Temperature       ...       ...        ...
Zone 2 Temperature       ...       ...        ...
Zone 1 Setpoint          ...       ...        ...
```

### Advantages

- Simple implementation.
- Smaller database compared with 1-second logging.
- Easy graph rendering.
- Easy PDF generation.
- No special downsampling algorithm required.
- No premium reporting software required.
- Customer has already accepted 1-minute sampling.

### Disadvantage

A temperature change that happens completely within one minute may not be captured accurately.

---

# Strategy 2 – Keep 1-Second Raw Data, Reduce Only for PDF

## Alternative if 1-second history is required later

Keep the existing 1-second data in SQLite, but do not send all raw points directly to the PDF.

Before creating the PDF, reduce the data while preserving important changes.

### Data flow

```text
PLC / DataStore
      |
      | every 1 second
      v
SQLite
      |
      | raw historical data
      v
PDF preparation
      |
      | intelligent reduction
      | preserve important min/max changes
      v
PDF graph
```

### Do not use average-only reduction

Example:

```text
160
161
162
195   <-- important spike
161
160
```

If only an average is used, the 195°C spike can become less visible or disappear.

Instead, preserve important values such as:

- Minimum
- Maximum
- Representative/last value
- Important changes/spikes

### Multiple-page PDF

For a 24-hour trend, if one page becomes too compressed, use multiple landscape pages:

```text
Page 1 → 00:00 to 06:00
Page 2 → 06:00 to 12:00
Page 3 → 12:00 to 18:00
Page 4 → 18:00 to 24:00
```

Each page can contain the same 3 lines and the relevant time range.

### Advantages

- Keeps complete 1-second historical data.
- Better for detecting short spikes/drops.
- Flexible for future reporting requirements.
- PDF resolution can be changed without changing stored raw data.

### Disadvantages

- More implementation work.
- Larger database.
- PDF generation needs a reduction/downsampling step.
- Multiple pages may be preferable for long periods.

---

# Comparison

| Item | Strategy 1 | Strategy 2 |
|---|---|---|
| Database sampling | 1 minute | 1 second |
| 24-hour points per line | 1,440 | 86,400 |
| PDF processing | Simple | More complex |
| Short spikes | May be missed | Better preserved |
| Database size | Smaller | Larger |
| Implementation | Easier | More work |
| PDF pages | One page can be practical | Multiple pages may be better |
| Premium software | Not required | Not required |
| Recommended now | **Yes** | Future option |

---

# Recommendation

For the **current customer requirement, use Strategy 1**.

The customer has said that **1-second sampling is not compulsory and 1-minute sampling is acceptable**.

Therefore:

```text
Store every 1 minute
        ↓
1,440 points / 24 hours / line
        ↓
Plot directly
        ↓
Generate readable PDF
```

This is the simplest and cleanest solution.

Keep Strategy 2 as a future option if the customer later requires 1-second historical resolution.

---

# Implementation Rules

1. Keep exactly these 3 graph lines:
   - Zone 1 Temperature
   - Zone 2 Temperature
   - Zone 1 Setpoint

2. Keep the existing graph theme.

3. Keep existing Live/History functionality.

4. Do not delete existing historical data just to implement PDF export.

5. Use a free/open-source PDF solution. Do not introduce a premium reporting product.

6. The PDF should contain the trend graph, not a huge table containing every reading.

7. The PDF should be readable when printed.

8. Before implementation, inspect the existing:
   - `GraphViewModel`
   - `GraphView.xaml`
   - `RunSessionService`
   - `DatabaseService`
   - SQLite schema
   - LiveCharts configuration

9. Do not modify unrelated features.

---

# Suggested PDF Layout

```text
========================================================
                 OVEN TEMPERATURE TREND

Recipe: Rotor VPI Curing
Date: 08-Sep-2026
Time: 00:00 - 24:00

250°C |
      |
200°C |        _________
      |       /         \________
150°C |______/
      |
100°C |
      |
 50°C |
      |
  0°C +-----------------------------------------------
        00:00   06:00   12:00   18:00   24:00

       ── Zone 1 Temperature
       ── Zone 2 Temperature
       ── Zone 1 Setpoint

--------------------------------------------------------
Description              Min      Max      Average
Zone 1 Temperature       ...      ...        ...
Zone 2 Temperature       ...      ...        ...
Zone 1 Setpoint          ...      ...        ...
========================================================
```

## Final Recommendation

**Implement Strategy 1 first: 1-minute sampling.**

Do not redesign the existing graph.

Do not delete existing graph features.

Do not use a premium reporting product.

First inspect the existing project and determine the cleanest way to change the recording interval from the current interval to **1 minute**, then implement PDF export using the existing graph data.
