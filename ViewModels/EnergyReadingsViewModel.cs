using CommunityToolkit.Mvvm.ComponentModel;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class EnergyReadingsViewModel : ObservableObject
{
    public PlcDataStore DataStore { get; }

    public EnergyReadingsViewModel(PlcDataStore dataStore)
    {
        DataStore = dataStore;
    }
}
