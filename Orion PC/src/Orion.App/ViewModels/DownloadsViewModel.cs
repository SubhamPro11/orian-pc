using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Orion.Platform.Windows.Services;

namespace Orion.App.ViewModels;

public partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadManager _downloadManager;
    private readonly MainViewModel _mainViewModel;
    private readonly DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    private ObservableCollection<DownloadItem> _downloadQueue = new();

    public DownloadsViewModel(IDownloadManager downloadManager, MainViewModel mainViewModel)
    {
        _downloadManager = downloadManager;
        _mainViewModel = mainViewModel;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _downloadManager.DownloadProgressChanged += OnDownloadProgressChanged;
        _downloadManager.DownloadCompleted += OnDownloadCompleted;
    }

    public void RefreshQueue()
    {
        var items = _downloadManager.GetQueue();
        RunOnUIThread(() =>
        {
            DownloadQueue.Clear();
            foreach (var item in items)
            {
                DownloadQueue.Add(item);
            }

            _mainViewModel.DownloadBadgeCount = items.Count(i => i.Status == DownloadStatus.Downloading || i.Status == DownloadStatus.Queued);
        });
    }

    private void OnDownloadProgressChanged(DownloadItem item)
    {
        RunOnUIThread(() =>
        {
            var index = DownloadQueue.ToList().FindIndex(i => i.Id == item.Id);
            if (index >= 0)
            {
                DownloadQueue[index] = item;
            }
            else
            {
                DownloadQueue.Insert(0, item);
            }

            _mainViewModel.DownloadBadgeCount = DownloadQueue.Count(i => i.Status == DownloadStatus.Downloading || i.Status == DownloadStatus.Queued);
        });
    }

    private void OnDownloadCompleted(DownloadItem item)
    {
        OnDownloadProgressChanged(item);
    }

    private void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    [RelayCommand]
    public void Pause(string downloadId) => _downloadManager.Pause(downloadId);

    [RelayCommand]
    public void Resume(string downloadId) => _downloadManager.Resume(downloadId);

    [RelayCommand]
    public void Cancel(string downloadId) => _downloadManager.Cancel(downloadId);

    public void Dispose()
    {
        _downloadManager.DownloadProgressChanged -= OnDownloadProgressChanged;
        _downloadManager.DownloadCompleted -= OnDownloadCompleted;
        GC.SuppressFinalize(this);
    }
}
