using System.Windows;
using Zora.Desktop.Services;
namespace Zora.Desktop;
public partial class LicenseWindow : Window
{
    private readonly LicenseService _service;
    public LicenseWindow(LicenseService service)
    {
        InitializeComponent();
        _service = service;
        ApiBox.Text = service.ApiUrl;
    }
    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ActivateButton.IsEnabled = false;
            ErrorLabel.Text = "Verifying your key…";
            _service.ApiUrl = ApiBox.Text.Trim();
            await _service.ActivateAsync(UsernameBox.Text.Trim(), KeyBox.Text.Trim());
            DialogResult = true;
        }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; }
        finally { ActivateButton.IsEnabled = true; }
    }
}
