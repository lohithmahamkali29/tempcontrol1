using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TempControl.Views;

public partial class RunDialogWindow : Window
{
    private static readonly string _folderPersistFile =
        Path.Combine(AppContext.BaseDirectory, "run_folder.txt");
    private const string DefaultFolder = @"C:\ovencycle_runs";

    public string FolderPath { get; private set; }
    public string FileName   { get; private set; } = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    public int    IntervalSeconds => 60;

    private readonly DispatcherTimer _countdown = new();
    private int _remaining = 15;

    public RunDialogWindow()
    {
        InitializeComponent();

        // Load last used folder (or default)
        FolderPath = LoadSavedFolder();
        FolderPathBox.Text = FolderPath;

        FileNameBox.Text  = FileName;
        UpdateCountdownText();

        _countdown.Interval = TimeSpan.FromSeconds(1);
        _countdown.Tick    += OnCountdownTick;
        _countdown.Start();
    }

    // Clicking the read-only folder TextBox also opens the browser
    private void FolderPathBox_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BrowseForFolder();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        => BrowseForFolder();

    private void BrowseForFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title            = "Select folder to save cycle run files",
            InitialDirectory = FolderPath
        };

        if (dlg.ShowDialog(this) == true)
        {
            FolderPath         = dlg.FolderName;
            FolderPathBox.Text = FolderPath;
            SaveFolder(FolderPath);
        }
    }

    private static string LoadSavedFolder()
    {
        try
        {
            if (File.Exists(_folderPersistFile))
            {
                var saved = File.ReadAllText(_folderPersistFile).Trim();
                if (!string.IsNullOrEmpty(saved)) return saved;
            }
        }
        catch { /* fall through to default */ }
        return DefaultFolder;
    }

    private static void SaveFolder(string folder)
    {
        try { File.WriteAllText(_folderPersistFile, folder); }
        catch { /* non-critical */ }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        _remaining--;
        UpdateCountdownText();
        if (_remaining <= 0)
            AutoStart();
    }

    private void UpdateCountdownText()
        => CountdownText.Text = $"Auto-starts with default name in {_remaining}s";

    private void AutoStart()
    {
        _countdown.Stop();
        if (string.IsNullOrWhiteSpace(FileNameBox.Text))
            FileNameBox.Text = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        Commit();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _countdown.Stop();
        Commit();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _countdown.Stop();
        DialogResult = false;
    }

    private void Commit()
    {
        FileName = string.IsNullOrWhiteSpace(FileNameBox.Text)
            ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
            : FileNameBox.Text.Trim();

        DialogResult = true;
    }
}
