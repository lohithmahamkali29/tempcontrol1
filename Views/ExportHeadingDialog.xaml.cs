using System.Windows;
using System.Windows.Input;

namespace TempControl.Views;

public partial class ExportHeadingDialog : Window
{
    public string ReportHeading { get; private set; } = string.Empty;

    public ExportHeadingDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => HeadingTextBox.Focus();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ReportHeading = HeadingTextBox.Text ?? string.Empty;
        DialogResult = true;
    }

    private void HeadingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReportHeading = HeadingTextBox.Text ?? string.Empty;
            DialogResult = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
