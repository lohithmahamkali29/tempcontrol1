using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempControl.Models;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly HashSet<string> TrackedProperties =
    [
        nameof(Step1Zone1Temp), nameof(Step1Zone2Temp), nameof(Step1SoakTime), nameof(Step1RampRate),
        nameof(Step2Zone1Temp), nameof(Step2Zone2Temp), nameof(Step2SoakTime), nameof(Step2RampRate),
        nameof(Step3Zone1Temp), nameof(Step3Zone2Temp), nameof(Step3SoakTime), nameof(Step3RampRate),
        nameof(Step4Zone1Temp), nameof(Step4Zone2Temp), nameof(Step4SoakTime), nameof(Step4RampRate),
        nameof(Step5Zone1Temp), nameof(Step5Zone2Temp), nameof(Step5SoakTime), nameof(Step5RampRate),
        nameof(ProcessZone1Safety), nameof(ProcessZone2Safety), nameof(ProcessBlower1), nameof(ProcessBlower2),
        nameof(IsEditMode)
    ];

    public PlcDataStore DataStore { get; }
    private readonly ManualControlService _manualControlService;
    private readonly DatabaseService _databaseService;
    private readonly AuthorizationService _authorizationService;
    private bool _loadingRecipe;
    private bool _isAuthorizedForEditing;

    public ObservableCollection<Recipe> Recipes { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private Recipe? _selectedRecipe;

    public bool IsNotEditMode => !IsEditMode;
    public bool CanAttemptSupervisorEdit => true;
    public bool CanUseSupervisorEdit =>
        _authorizationService.CurrentRole == UserRole.Supervisor;
    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(IsNotEditMode));

    // Editable local fields — step params (D301–D320)
    [ObservableProperty] private double _step1Zone1Temp;
    [ObservableProperty] private double _step1Zone2Temp;
    [ObservableProperty] private double _step1SoakTime;
    [ObservableProperty] private double _step1RampRate;
    [ObservableProperty] private double _step2Zone1Temp;
    [ObservableProperty] private double _step2Zone2Temp;
    [ObservableProperty] private double _step2SoakTime;
    [ObservableProperty] private double _step2RampRate;
    [ObservableProperty] private double _step3Zone1Temp;
    [ObservableProperty] private double _step3Zone2Temp;
    [ObservableProperty] private double _step3SoakTime;
    [ObservableProperty] private double _step3RampRate;
    [ObservableProperty] private double _step4Zone1Temp;
    [ObservableProperty] private double _step4Zone2Temp;
    [ObservableProperty] private double _step4SoakTime;
    [ObservableProperty] private double _step4RampRate;
    [ObservableProperty] private double _step5Zone1Temp;
    [ObservableProperty] private double _step5Zone2Temp;
    [ObservableProperty] private double _step5SoakTime;
    [ObservableProperty] private double _step5RampRate;

    // Global process settings (D321–D324)
    [ObservableProperty] private double _processZone1Safety;
    [ObservableProperty] private double _processZone2Safety;
    [ObservableProperty] private double _processBlower1;
    [ObservableProperty] private double _processBlower2;

    public SettingsViewModel(
        PlcDataStore dataStore,
        ManualControlService manualControlService,
        DatabaseService databaseService,
        AuthorizationService authorizationService)
    {
        DataStore = dataStore;
        _manualControlService = manualControlService;
        _databaseService = databaseService;
        _authorizationService = authorizationService;
        _authorizationService.CurrentRoleChanged += OnCurrentRoleChanged;
        PropertyChanged += OnViewModelPropertyChanged;
        LoadRecipes();
        DataStore.PropertyChanged += (_, e) =>
        {
            Trace($"DataStore changed: {e.PropertyName}");
            if (!IsEditMode && SelectedRecipe is null)
                SnapshotFromDataStore();
        };
    }

    partial void OnSelectedRecipeChanged(Recipe? value)
    {
        if (value is not null && !_loadingRecipe)
        {
            LoadRecipeValues(value);
            StatusMessage = $"Recipe previewed: {value.RecipeName}. Press SELECT to apply it.";
        }
    }

    public bool AuthorizeRecipeSelection()
    {
        if (_authorizationService.HasActiveSession
            && _authorizationService.CurrentRole is UserRole.Operator or UserRole.Supervisor)
            return true;

        var authenticatedRole = _authorizationService.RequestAuthorization("Recipe Selection Authorization Required");
        return authenticatedRole is UserRole.Operator or UserRole.Supervisor
            && _authorizationService.HasActiveSession;
    }

    private void SnapshotFromDataStore()
    {
        Trace("SnapshotFromDataStore called");
        Step1Zone1Temp = DataStore.Step1Zone1Temp;
        Step1Zone2Temp = DataStore.Step1Zone2Temp;
        Step1SoakTime = DataStore.Step1SoakTime;
        Step1RampRate = DataStore.Step1RampRate;
        Step2Zone1Temp = DataStore.Step2Zone1Temp;
        Step2Zone2Temp = DataStore.Step2Zone2Temp;
        Step2SoakTime = DataStore.Step2SoakTime;
        Step2RampRate = DataStore.Step2RampRate;
        Step3Zone1Temp = DataStore.Step3Zone1Temp;
        Step3Zone2Temp = DataStore.Step3Zone2Temp;
        Step3SoakTime = DataStore.Step3SoakTime;
        Step3RampRate = DataStore.Step3RampRate;
        Step4Zone1Temp = DataStore.Step4Zone1Temp;
        Step4Zone2Temp = DataStore.Step4Zone2Temp;
        Step4SoakTime = DataStore.Step4SoakTime;
        Step4RampRate = DataStore.Step4RampRate;
        Step5Zone1Temp = DataStore.Step5Zone1Temp;
        Step5Zone2Temp = DataStore.Step5Zone2Temp;
        Step5SoakTime = DataStore.Step5SoakTime;
        Step5RampRate = DataStore.Step5RampRate;
        ProcessZone1Safety = DataStore.ProcessZone1Safety;
        ProcessZone2Safety = DataStore.ProcessZone2Safety;
        ProcessBlower1 = DataStore.ProcessBlower1;
        ProcessBlower2 = DataStore.ProcessBlower2;
        Trace($"SnapshotFromDataStore finished: S1Z1={Step1Zone1Temp}, S1Z2={Step1Zone2Temp}, S1Soak={Step1SoakTime}, S1Ramp={Step1RampRate}, S2Z1={Step2Zone1Temp}, S2Z2={Step2Zone2Temp}, S2Soak={Step2SoakTime}, S2Ramp={Step2RampRate}, PZ1Safe={ProcessZone1Safety}, PZ2Safe={ProcessZone2Safety}, PB1={ProcessBlower1}, PB2={ProcessBlower2}");
    }

    [RelayCommand]
    private void Edit()
    {
        Trace("Edit requested");

        if (DataStore.IsProcessRunning)
        {
            MessageBox.Show(
                "Process is running. Stop the process before editing process parameters.",
                "Edit Blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var authenticatedRole = _authorizationService.CurrentRole ?? _authorizationService.RequestAuthorization();

        if (!_authorizationService.HasActiveSession)
        {
            MessageBox.Show(
                "Authorization session expired. Please login again.",
                "Session Expired",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            IsEditMode = false;
            _isAuthorizedForEditing = false;
            return;
        }

        if (authenticatedRole == UserRole.Operator)
        {
            MessageBox.Show(
                "Operator is not allowed to edit process parameter values.\nPlease login as Supervisor.",
                "Edit Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            IsEditMode = false;
            _isAuthorizedForEditing = false;
            return;
        }

        if (authenticatedRole != UserRole.Supervisor)
        {
            IsEditMode = false;
            _isAuthorizedForEditing = false;
            return;
        }

        _isAuthorizedForEditing = true;
        IsEditMode = true;
    }

    /// <summary>
    /// Called when the Settings page becomes visible so the table always shows
    /// the latest PLC values on entry, regardless of polling timing.
    /// </summary>
    public void OnNavigatedTo()
    {
        Trace("OnNavigatedTo called");
        if (Recipes.Count == 0)
            LoadRecipes();
        if (SelectedRecipe is null && Recipes.Count > 0)
        {
            _loadingRecipe = true;
            SelectedRecipe = Recipes[0];
            _loadingRecipe = false;
            LoadRecipeValues(SelectedRecipe);
        }
    }

    public void OnNavigatedFrom()
    {
        // MODIFIED: authorization is limited to the current page interaction.
        _isAuthorizedForEditing = false;
        IsEditMode = false;
        _authorizationService.ClearAuthorization();
    }

    [RelayCommand]
    private async Task SelectRecipeAsync()
    {
        if (SelectedRecipe is null || DataStore.IsProcessRunning)
            return;

        if (!_authorizationService.HasActiveSession
            && _authorizationService.RequestAuthorization() is null)
            return;

        if (!_authorizationService.HasActiveSession
            || _authorizationService.CurrentRole is not UserRole.Operator
            and not UserRole.Supervisor)
            return;

        await ApplySelectedRecipeAsync(SelectedRecipe);
    }

    [RelayCommand]
    private async Task SetRecipeAsync()
    {
        // MODIFIED: defensive checks before any PLC write.
        if (!_isAuthorizedForEditing
            || !IsEditMode
            || DataStore.IsProcessRunning)
            return;

        if (!_authorizationService.HasActiveSession)
        {
            MessageBox.Show(
                "Authorization session expired. Please login again.",
                "Session Expired",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_authorizationService.CurrentRole == UserRole.Operator)
        {
            MessageBox.Show(
                "Operator is not allowed to perform this action. Please login as Supervisor.",
                "Permission Denied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_authorizationService.CurrentRole != UserRole.Supervisor)
            return;

        if (SelectedRecipe is null)
            return;

        Trace($"UpdateAsync starting: S1Z1={Step1Zone1Temp}, S1Z2={Step1Zone2Temp}, S1Soak={Step1SoakTime}, S1Ramp={Step1RampRate}, S2Z1={Step2Zone1Temp}, S2Z2={Step2Zone2Temp}, S2Soak={Step2SoakTime}, S2Ramp={Step2RampRate}, PZ1Safe={ProcessZone1Safety}, PZ2Safe={ProcessZone2Safety}, PB1={ProcessBlower1}, PB2={ProcessBlower2}");

        var writes = new (int Address, double Value, string Label)[]
        {
            (301, Step1Zone1Temp,     "Step 1 Zone 1 Temp"),
            (302, Step1Zone2Temp,     "Step 1 Zone 2 Temp"),
            (303, Step1SoakTime,      "Step 1 Soak Time"),
            (304, Step1RampRate,      "Step 1 Ramp Rate"),
            (305, Step2Zone1Temp,     "Step 2 Zone 1 Temp"),
            (306, Step2Zone2Temp,     "Step 2 Zone 2 Temp"),
            (307, Step2SoakTime,      "Step 2 Soak Time"),
            (308, Step2RampRate,      "Step 2 Ramp Rate"),
            (309, Step3Zone1Temp,     "Step 3 Zone 1 Temp"),
            (310, Step3Zone2Temp,     "Step 3 Zone 2 Temp"),
            (311, Step3SoakTime,      "Step 3 Soak Time"),
            (312, Step3RampRate,      "Step 3 Ramp Rate"),
            (313, Step4Zone1Temp,     "Step 4 Zone 1 Temp"),
            (314, Step4Zone2Temp,     "Step 4 Zone 2 Temp"),
            (315, Step4SoakTime,      "Step 4 Soak Time"),
            (316, Step4RampRate,      "Step 4 Ramp Rate"),
            (317, Step5Zone1Temp,     "Step 5 Zone 1 Temp"),
            (318, Step5Zone2Temp,     "Step 5 Zone 2 Temp"),
            (319, Step5SoakTime,      "Step 5 Soak Time"),
            (320, Step5RampRate,      "Step 5 Ramp Rate"),
            (DataStore.Zone1SafetyTemperatureAddress, ProcessZone1Safety, "Zone 1 Safety"),
            (DataStore.Zone2SafetyTemperatureAddress, ProcessZone2Safety, "Zone 2 Safety"),
            (323, ProcessBlower1,     "Blower 1"),
            (324, ProcessBlower2,     "Blower 2"),
        };

        var failedWrites = new List<string>();
        foreach (var (address, value, label) in writes)
        {
            Trace($"Writing {label}: D{address} = {value}");
            if (!await _manualControlService.WriteRegisterAsync(address, value))
                failedWrites.Add(label);
        }

        if (failedWrites.Count == 0)
        {
            UpdateRecipeFromFields(SelectedRecipe);
            try
            {
                _databaseService.UpdateRecipe(SelectedRecipe);
                PlcStateCache.Save(DataStore);
                StatusMessage = $"Recipe applied at {DateTime.Now:HH:mm:ss}";
                _isAuthorizedForEditing = false;
                IsEditMode = false;
                _authorizationService.ClearAuthorization();
                MessageBox.Show("All values have been changed successfully.", "Recipe Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "PLC values were written, but the recipe database update failed.";
                MessageBox.Show($"PLC values were written, but the recipe could not be saved: {ex.Message}", "Recipe Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            StatusMessage = $"Failed to write: {string.Join(", ", failedWrites)}";
            MessageBox.Show(
                $"The recipe was not saved. Failed fields/registers:\n{string.Join("\n", failedWrites)}",
                "Recipe Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        Trace(failedWrites.Count == 0
            ? "SetRecipeAsync finished successfully"
            : $"SetRecipeAsync finished with failures: {string.Join(", ", failedWrites)}");
    }

    private void LoadRecipes()
    {
        Recipes.Clear();
        foreach (var recipe in _databaseService.GetRecipes())
            Recipes.Add(recipe);

        if (SelectedRecipe is null && Recipes.Count > 0)
        {
            _loadingRecipe = true;
            SelectedRecipe = Recipes[0];
            _loadingRecipe = false;
            LoadRecipeValues(SelectedRecipe);
        }
    }

    private async Task ApplySelectedRecipeAsync(Recipe recipe)
    {
        if (!_authorizationService.HasActiveSession
            || _authorizationService.CurrentRole is not UserRole.Operator
            and not UserRole.Supervisor)
            return;

        if (DataStore.IsProcessRunning)
            return;

        var plc = DataStore.SlaveConfigs.FirstOrDefault(slave => slave.SlaveId == 4);
        if (plc?.IsConnected != true)
        {
            MessageBox.Show(
                "Cannot set the process sequence because the PLC is not connected.\nPlease check the PLC connection and try again.",
                "PLC Disconnected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var writes = new (int Address, double Value, string Label)[]
        {
            (301, recipe.Step1Zone1Temp, "Step 1 Zone 1 Temp"),
            (302, recipe.Step1Zone2Temp, "Step 1 Zone 2 Temp"),
            (303, recipe.Step1SoakTime, "Step 1 Soak Time"),
            (304, recipe.Step1RampRate, "Step 1 Ramp Rate"),
            (305, recipe.Step2Zone1Temp, "Step 2 Zone 1 Temp"),
            (306, recipe.Step2Zone2Temp, "Step 2 Zone 2 Temp"),
            (307, recipe.Step2SoakTime, "Step 2 Soak Time"),
            (308, recipe.Step2RampRate, "Step 2 Ramp Rate"),
            (DataStore.Zone1SafetyTemperatureAddress, recipe.ProcessZone1Safety, "Zone 1 Safety"),
            (DataStore.Zone2SafetyTemperatureAddress, recipe.ProcessZone2Safety, "Zone 2 Safety"),
            (323, recipe.ProcessBlower1, "Blower 1"),
            (324, recipe.ProcessBlower2, "Blower 2")
        };

        var failedWrites = new List<string>();
        foreach (var (address, value, label) in writes)
        {
            Trace($"Writing {label}: D{address} = {value}");
            if (!await _manualControlService.WriteRegisterAsync(address, value))
                failedWrites.Add(label);
        }

        if (failedWrites.Count > 0)
        {
            MessageBox.Show(
                "The PLC is connected, but one or more process parameters could not be written.\nPlease check the PLC communication and try again.",
                "Recipe Set Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        PlcStateCache.Save(DataStore);
        StatusMessage = $"Recipe set: {recipe.RecipeName}";
        MessageBox.Show(
            $"RECIPE SET SUCCESSFULLY\n\n" +
            $"Sequence: {recipe.RecipeName}\n" +
            $"Temperature: {recipe.Step1Zone1Temp:F0} °C\n" +
            $"Safety Temperature: {recipe.ProcessZone1Safety:F0} °C\n" +
            $"Soak Time: {recipe.Step1SoakTime / 60.0:F0} Hours\n" +
            $"Ramp Time: {recipe.Step1RampRate:F0} Minutes\n\n" +
            "This sequence is now set for the process.",
            "Recipe Set Successfully",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnCurrentRoleChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanAttemptSupervisorEdit));
        OnPropertyChanged(nameof(CanUseSupervisorEdit));
    }

    private void LoadRecipeValues(Recipe recipe)
    {
        Step1Zone1Temp = recipe.Step1Zone1Temp; Step1Zone2Temp = recipe.Step1Zone2Temp; Step1SoakTime = recipe.Step1SoakTime; Step1RampRate = recipe.Step1RampRate;
        Step2Zone1Temp = recipe.Step2Zone1Temp; Step2Zone2Temp = recipe.Step2Zone2Temp; Step2SoakTime = recipe.Step2SoakTime; Step2RampRate = recipe.Step2RampRate;
        Step3Zone1Temp = recipe.Step3Zone1Temp; Step3Zone2Temp = recipe.Step3Zone2Temp; Step3SoakTime = recipe.Step3SoakTime; Step3RampRate = recipe.Step3RampRate;
        Step4Zone1Temp = recipe.Step4Zone1Temp; Step4Zone2Temp = recipe.Step4Zone2Temp; Step4SoakTime = recipe.Step4SoakTime; Step4RampRate = recipe.Step4RampRate;
        Step5Zone1Temp = recipe.Step5Zone1Temp; Step5Zone2Temp = recipe.Step5Zone2Temp; Step5SoakTime = recipe.Step5SoakTime; Step5RampRate = recipe.Step5RampRate;
        ProcessZone1Safety = recipe.ProcessZone1Safety; ProcessZone2Safety = recipe.ProcessZone2Safety;
        ProcessBlower1 = recipe.ProcessBlower1; ProcessBlower2 = recipe.ProcessBlower2;
    }

    private void UpdateRecipeFromFields(Recipe recipe)
    {
        recipe.Step1Zone1Temp = Step1Zone1Temp; recipe.Step1Zone2Temp = Step1Zone2Temp; recipe.Step1SoakTime = Step1SoakTime; recipe.Step1RampRate = Step1RampRate;
        recipe.Step2Zone1Temp = Step2Zone1Temp; recipe.Step2Zone2Temp = Step2Zone2Temp; recipe.Step2SoakTime = Step2SoakTime; recipe.Step2RampRate = Step2RampRate;
        recipe.Step3Zone1Temp = Step3Zone1Temp; recipe.Step3Zone2Temp = Step3Zone2Temp; recipe.Step3SoakTime = Step3SoakTime; recipe.Step3RampRate = Step3RampRate;
        recipe.Step4Zone1Temp = Step4Zone1Temp; recipe.Step4Zone2Temp = Step4Zone2Temp; recipe.Step4SoakTime = Step4SoakTime; recipe.Step4RampRate = Step4RampRate;
        recipe.Step5Zone1Temp = Step5Zone1Temp; recipe.Step5Zone2Temp = Step5Zone2Temp; recipe.Step5SoakTime = Step5SoakTime; recipe.Step5RampRate = Step5RampRate;
        recipe.ProcessZone1Safety = ProcessZone1Safety; recipe.ProcessZone2Safety = ProcessZone2Safety;
        recipe.ProcessBlower1 = ProcessBlower1; recipe.ProcessBlower2 = ProcessBlower2;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || !TrackedProperties.Contains(e.PropertyName))
            return;

        Trace($"ViewModel property changed: {e.PropertyName}");
    }

    private static void Trace(string message)
    {
        Debug.WriteLine($"[SettingsVM] {message}");
        DiagnosticLogger.Instance.Log("SETTINGS-VM", message);
    }
}
