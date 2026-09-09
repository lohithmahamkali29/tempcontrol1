# Alarm History + Home Sidebar Checkpoint

This file is a manual restore/checkpoint note for the alarm-history implementation.

## Scope of this checkpoint
This checkpoint covers:
- new `Alarm History` page
- live alarm tracking service
- `ACK ALL` / `RESET ALL` logic
- Home page latest-alarms sidebar
- navigation wiring for the new page

## New files added
- `Models\AlarmHistoryItem.cs`
- `Services\AlarmHistoryService.cs`
- `ViewModels\AlarmHistoryViewModel.cs`
- `Views\AlarmHistoryView.xaml`
- `Views\AlarmHistoryView.xaml.cs`

## Existing files changed
- `ViewModels\MainViewModel.cs`
- `MainWindow.xaml`
- `ViewModels\HomeViewModel.cs`
- `Views\HomeView.xaml`

## Alarm rule implemented
### Alarm when signal is ON
- `Zone-1 Temp. Safety`
- `Zone-2 Temp. Safety`

### Alarm when signal is OFF
- `Single Phase Preventer`
- `Emergency Switch`
- `Door Limit Switch Close`
- `Electrical Blower motor-1`
- `Electrical Blower motor-2`
- `Exhaust Blower motor Cont. ON`
- `Electrical Exhaust Blower Motor`

## Alarm page columns
- `Alarm Description`
- `Time On`
- `Duration`
- `Condition`

## Home sidebar behavior
- toggle button on Home page opens/closes latest alarms panel
- panel is scrollable
- newest alarms appear first

## Important note
This is an `Assets` restore note only.
A real Copilot chat restore checkpoint cannot be created from inside the workspace.
