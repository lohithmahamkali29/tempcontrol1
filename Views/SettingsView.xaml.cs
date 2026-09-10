using System;
using System.Windows;
using System.Windows.Controls;
using TempControl.ViewModels;

namespace TempControl.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue && DataContext is SettingsViewModel vm)
                vm.OnNavigatedTo();
            else if (!(bool)e.NewValue && DataContext is SettingsViewModel settingsVm)
                settingsVm.OnNavigatedFrom();
        };
    }

    private void RecipeComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && viewModel.AuthorizeRecipeSelection())
            return;

        if (sender is ComboBox comboBox)
            comboBox.IsDropDownOpen = false;
    }
}
