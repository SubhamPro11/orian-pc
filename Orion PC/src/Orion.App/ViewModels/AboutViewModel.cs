using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orion.Core.Models;

namespace Orion.App.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty]
    private string _version = "Orion Store for PC v2.6.5";

    [ObservableProperty]
    private string _license = "GNU General Public License v3.0 (GPL-3.0)";

    [ObservableProperty]
    private string _author = "RookieZ / Orion Community";

    [ObservableProperty]
    private string _description = "A transparent, multi-source open-source store for Windows PC with no analytics, no tracking, and direct public release resolution.";

    [RelayCommand]
    public void OpenGithub()
    {
        Process.Start(new ProcessStartInfo { FileName = "https://github.com/RookieEnough/Orion-Store", UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenDiscord()
    {
        Process.Start(new ProcessStartInfo { FileName = "https://discord.com/invite/CrM6y4ujnq", UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenDonation()
    {
        Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/rookie_z", UseShellExecute = true });
    }
}

