# Timer Card Consolidation — HomeView.xaml Update

## Changes Made

### File: `Views/HomeView.xaml`

**What Changed:**
Created a single unified **"Timer" card** that displays both "Set Time" and "Elapsed Time" side-by-side, similar to how Zone 1 displays "PV" and "Job PV".

### Layout Transformation

**Before:**
```
Grid with 4 columns:
┌─────────┬──────────────────┬──────────────┬──────────┐
│   SV    │   Set Time       │ Elp Time     │   Step   │
│ (Col 0) │ (Col 1-2, span2) │ (Col 2)      │ (Col 3)  │
└─────────┴──────────────────┴──────────────┴──────────┘
```

**After:**
```
Grid with 3 columns:
┌─────────┬──────────────────────────────┬──────────┐
│   SV    │        Timer                 │   Step   │
│ (Col 0) │  Set Time | Elp Time         │ (Col 2)  │
│         │ (Col 1)   | (Col 1)          │          │
└─────────┴──────────────────────────────┴──────────┘
```

### Specific Implementation

**Grid Structure (Lines 268-274):**
- Changed from 4 columns (`Width="*"` × 4) to 3 columns (`Width="*"` × 3)
- Column 0: SV card
- Column 1: Timer card (new consolidated card)
- Column 2: Step card

**Timer Card (Lines 297-341):**
- Created new `<Border>` with `Grid.Column="1"` (uses BottomBox style)
- Contains `<StackPanel>` with:
  - Header: "Timer" label (FontSize 26, Bold, #1A237E)
  - Two sub-sections in a horizontal Grid:

**Set Time Section (left side):**
- Label: "Set Time" (FontSize 18, SemiBold, smaller than header)
- Value: `{Binding DataStore.SoakTime, StringFormat=F1}`
- Unit: "in Min"
- Green foreground for value

**Elapsed Time Section (right side):**
- Label: "Elp Time" (FontSize 18, SemiBold)
- Value: `{Binding DataStore.ElapsedTime, StringFormat=hh\:mm\:ss}`
- Unit: "in Min"
- Uses DisplayTextBox style

**Spacing:**
- 40-unit margin between Set Time and Elp Time sections for clear visual separation
- 20-unit bottom margin on Timer header for spacing

### What Was NOT Changed

- SV (Zone 1 Setpoint) card — remains in Column 0 with °C symbol
- Step card — remains in Column 2
- Zone 1 and Zone 2 PV/Job PV displays — unchanged
- Data bindings — all properties still bind correctly
- Styles and colors — applied existing styles (BottomBox, DisplayTextBox, UnitLabel, UnitLabelPV)

## Visual Hierarchy

**Header Sizes (FontSize):**
- "Timer" title: 26 (matches "SV" and "Step" titles)
- "Set Time" / "Elp Time" labels: 18 (smaller, subordinate)

**Color Scheme:**
- Timer header: #1A237E (dark blue, matches SV)
- Set Time/Elp Time labels: #333 (dark gray)
- Set Time value: Green (active indicator)
- Elapsed Time value: Default

## Build Status
✅ Build successful - No compilation errors

## Copilot Instructions Compliance
✅ Documentation in markdown format inside Assets folder
✅ Explicit step-by-step changes documented
✅ Placement details included (column positions, margins)
✅ Summary of what changed and what didn't

