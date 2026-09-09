using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TempControl.Models;
using TempControl.Services;

namespace TempControl.ViewModels;

public partial class PlcIoViewModel : ObservableObject
{
    public PlcDataStore DataStore { get; }

    public ObservableCollection<PlcIoPoint> IoPoints => DataStore.IoPoints;

    public IEnumerable<PlcIoPoint> InputPoints => IoPoints.Where(p => p.IsInput);
    public IEnumerable<PlcIoPoint> OutputPoints => IoPoints.Where(p => p.IsOutput);

    [ObservableProperty]
    private string _filterType = "All";

    public string[] FilterOptions { get; } = ["All", "Digital Input", "Digital Output", "Analog Input", "Analog Output"];

    public PlcIoViewModel(PlcDataStore dataStore)
    {
        DataStore = dataStore;
    }
}
