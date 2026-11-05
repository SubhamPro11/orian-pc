using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orion.App.ViewModels;

namespace Orion.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = ((App)Application.Current).Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
}

