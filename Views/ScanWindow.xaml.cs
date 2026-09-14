using System.Collections.ObjectModel;
using System.Windows;
using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Services;

namespace MyBasicNetworkTools.Views
{
    public partial class ScanWindow : Window
    {
        private readonly ScanService _scanService;

        private readonly ObservableCollection<ScanResult> _results;

        private CancellationTokenSource? _cancellationTokenSource;

        private int _completed;
        private int _online;
        private int _offline;

        public ScanWindow()
        {
            InitializeComponent();

            _scanService = new ScanService();

            _results =
                new ObservableCollection<ScanResult>();

            ResultGrid.ItemsSource = _results;
        }

        private async void ScanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string prefix =
                PrefixTextBox.Text.Trim();

            if (!ValidatePrefix(prefix))
            {
                return;
            }

            _results.Clear();

            _completed = 0;
            _online = 0;
            _offline = 0;

            ScanProgressBar.Value = 0;

            TotalText.Text = "254";
            OnlineText.Text = "0";
            OfflineText.Text = "0";
            ProgressText.Text = "Scanning...";

            ScanButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            PrefixTextBox.IsEnabled = false;

            _cancellationTokenSource =
                new CancellationTokenSource();

            try
            {
                List<string> ips = new();

                for (int i = 1; i <= 254; i++)
                {
                    ips.Add($"{prefix}.{i}");
                }

                using SemaphoreSlim semaphore =
                    new SemaphoreSlim(20);

                List<Task> tasks = new();

                foreach (string ip in ips)
                {
                    tasks.Add(
                        ScanSingleIpAsync(
                            ip,
                            semaphore,
                            _cancellationTokenSource.Token));
                }

                await Task.WhenAll(tasks);

                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    ProgressText.Text =
                        "Scan completed";
                }
                else
                {
                    ProgressText.Text =
                        "Scan stopped";
                }
            }
            catch (OperationCanceledException)
            {
                ProgressText.Text =
                    "Scan stopped";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ScanButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                PrefixTextBox.IsEnabled = true;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task ScanSingleIpAsync(
            string ip,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(
                cancellationToken);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                ScanResultData result =
                    await _scanService.ScanHostAsync(
                        ip);

                cancellationToken.ThrowIfCancellationRequested();

                await Dispatcher.InvokeAsync(() =>
                {
                    _results.Add(
                        new ScanResult
                        {
                            IP = result.IP,
                            Status = result.Status,
                            ResponseTime =
                                result.ResponseTime
                        });

                    _completed++;

                    if (result.Status == "Online")
                    {
                        _online++;
                    }
                    else
                    {
                        _offline++;
                    }

                    ScanProgressBar.Value =
                        _completed;

                    OnlineText.Text =
                        _online.ToString();

                    OfflineText.Text =
                        _offline.ToString();

                    ProgressText.Text =
                        $"Scanning... {_completed}/254";
                });
            }
            finally
            {
                semaphore.Release();
            }
        }

        private void StopButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _cancellationTokenSource?
                .Cancel();

            ProgressText.Text =
                "Stopping...";
        }

        private bool ValidatePrefix(
            string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                MessageBox.Show(
                    "Vui lòng nhập Network Prefix.",
                    "My Scan IP");

                return false;
            }

            string[] parts =
                prefix.Split('.');

            if (parts.Length != 3)
            {
                MessageBox.Show(
                    "Network Prefix phải có dạng:\n\n192.168.1",
                    "My Scan IP");

                return false;
            }

            foreach (string part in parts)
            {
                if (!int.TryParse(
                        part,
                        out int number)
                    || number < 0
                    || number > 255)
                {
                    MessageBox.Show(
                        "Network Prefix không hợp lệ.",
                        "My Scan IP");

                    return false;
                }
            }

            return true;
        }

        protected override void OnClosed(
            EventArgs e)
        {
            _cancellationTokenSource?
                .Cancel();

            base.OnClosed(e);
        }
    }
}