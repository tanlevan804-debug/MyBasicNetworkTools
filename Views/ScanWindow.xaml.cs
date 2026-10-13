using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Services;

namespace MyBasicNetworkTools.Views
{
    public partial class ScanWindow : Window
    {
        private readonly ScanService _scanService;

        private CancellationTokenSource? _cancellationTokenSource;

        private readonly ObservableCollection<ScanResult> _results = new();

        private int _total;
        private int _completed;
        private int _alive;
        private int _dead;
        private int _openPorts;

        private Stopwatch? _stopwatch;

        public ScanWindow()
        {
            InitializeComponent();

            _scanService = new ScanService();

            ResultGrid.ItemsSource = _results;

            ResetStatistics();
        }

        // =========================================================
        // MY IP / SUBNET
        // =========================================================

        private void MySubnetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string? cidr = GetLocalSubnetCidr();

                if (string.IsNullOrWhiteSpace(cidr))
                {
                    MessageBox.Show(
                        "Không tìm thấy IPv4 của card mạng đang hoạt động.",
                        "My Scan IP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                TargetComboBox.SelectedIndex = 0;
                StartIpTextBox.Text = cidr;
                EndIpTextBox.Clear();

                StatusText.Text = $"Local subnet: {cidr}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =========================================================
        // SCAN
        // =========================================================

        private async void ScanButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
                return;

            try
            {
                List<IPAddress> addresses = BuildTargetList();

                int timeout = ParseTimeout();
                int concurrency = ParseConcurrency();

                string scanMode = GetSelectedScanMode();

                List<int> ports = ParsePorts(PortsTextBox.Text);

                _total = addresses.Count;

                if (_total == 0)
                {
                    MessageBox.Show(
                        "Không có địa chỉ IP để quét.",
                        "My Scan IP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (_total > 65536)
                {
                    MessageBox.Show(
                        $"Dải mạng có {_total:N0} địa chỉ IP. " +
                        "Vui lòng giới hạn phạm vi quét tối đa 65.536 IP.",
                        "My Scan IP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (scanMode.Contains(
                        "Full Scan",
                        StringComparison.OrdinalIgnoreCase)
                    && ports.Count == 0)
                {
                    MessageBox.Show(
                        "Full Scan cần ít nhất một cổng để kiểm tra.",
                        "My Scan IP",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                _results.Clear();
                ResetStatistics();

                ProgressBar.Minimum = 0;
                ProgressBar.Maximum = _total;
                ProgressBar.Value = 0;

                ProgressText.Text = $"0 / {_total} (0%)";
                StatusText.Text = "Scanning...";

                ScanButton.IsEnabled = false;
                StopButton.IsEnabled = true;

                _cancellationTokenSource = new CancellationTokenSource();
                CancellationToken token = _cancellationTokenSource.Token;

                _stopwatch = Stopwatch.StartNew();

                // Dùng callback để cập nhật UI trên Dispatcher.
                var resultProgress = new Progress<ScanResult>(result =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    _results.Add(result);

                    if (result.IsAlive)
                        _alive++;
                    else
                        _dead++;

                    _openPorts += result.OpenPortCount;

                    ScannedText.Text = _completed.ToString();
                    AliveText.Text = _alive.ToString();
                    DeadText.Text = _dead.ToString();
                    OpenPortsText.Text = _openPorts.ToString();
                });

                var countProgress = new Progress<int>(completed =>
                {
                    _completed = Math.Clamp(completed, 0, _total);

                    ProgressBar.Value = _completed;

                    int percent = _total == 0
                        ? 0
                        : (int)(_completed * 100.0 / _total);

                    ProgressText.Text =
                        $"{_completed} / {_total} ({percent}%)";

                    ScannedText.Text = _completed.ToString();
                });

                await _scanService.ScanAsync(
                    addresses,
                    scanMode,
                    timeout,
                    concurrency,
                    ports,
                    resultProgress,
                    countProgress,
                    token);

                token.ThrowIfCancellationRequested();

                StatusText.Text = "Scan completed";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Scan stopped";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Scan error";

                MessageBox.Show(
                    ex.Message,
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (_stopwatch != null)
                {
                    _stopwatch.Stop();
                    ScanTimeText.Text =
                        $"{_stopwatch.ElapsedMilliseconds} ms";
                }

                ScanButton.IsEnabled = true;
                StopButton.IsEnabled = false;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        // =========================================================
        // STOP
        // =========================================================

        private void StopButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cancellationTokenSource == null)
                return;

            _cancellationTokenSource.Cancel();
            StatusText.Text = "Stopping...";
        }

        // =========================================================
        // CLEAR
        // =========================================================

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();

                // Không xóa dữ liệu trong khi tiến trình đang cập nhật UI.
                StatusText.Text = "Stopping before clear...";
                return;
            }

            _results.Clear();

            ResetStatistics();

            ProgressBar.Minimum = 0;
            ProgressBar.Maximum = 100;
            ProgressBar.Value = 0;

            ProgressText.Text = "0 / 0 (0%)";
            StatusText.Text = "Ready";
        }

        // =========================================================
        // BUILD TARGET LIST
        // =========================================================

        private List<IPAddress> BuildTargetList()
        {
            string mode = GetSelectedTargetMode();

            if (mode == "CIDR")
            {
                return _scanService.GenerateIpsFromCidr(
                    StartIpTextBox.Text.Trim());
            }

            return _scanService.GenerateIpsFromRange(
                StartIpTextBox.Text.Trim(),
                EndIpTextBox.Text.Trim());
        }

        // =========================================================
        // TARGET MODE
        // =========================================================

        private string GetSelectedTargetMode()
        {
            return
                (TargetComboBox.SelectedItem as ComboBoxItem)
                    ?.Content?.ToString()
                ?? "CIDR";
        }

        // =========================================================
        // SCAN MODE
        // =========================================================

        private string GetSelectedScanMode()
        {
            return
                (ScanModeComboBox.SelectedItem as ComboBoxItem)
                    ?.Content?.ToString()
                ?? "Quick Ping";
        }

        // =========================================================
        // TIMEOUT
        // =========================================================

        private int ParseTimeout()
        {
            if (!int.TryParse(TimeoutTextBox.Text.Trim(), out int timeout))
                throw new ArgumentException("Timeout phải là số nguyên.");

            if (timeout < 100 || timeout > 60000)
            {
                throw new ArgumentException(
                    "Timeout phải nằm trong khoảng 100 - 60000 ms.");
            }

            return timeout;
        }

        // =========================================================
        // CONCURRENCY
        // =========================================================

        private int ParseConcurrency()
        {
            if (!int.TryParse(
                    ConcurrencyTextBox.Text.Trim(),
                    out int concurrency))
            {
                throw new ArgumentException(
                    "Concurrency phải là số nguyên.");
            }

            if (concurrency < 1 || concurrency > 200)
            {
                throw new ArgumentException(
                    "Concurrency phải nằm trong khoảng 1 - 200.");
            }

            return concurrency;
        }

        // =========================================================
        // PORTS
        // =========================================================

        private static List<int> ParsePorts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<int>();

            List<int> ports = new();

            string[] parts = text.Split(
                new[] { ',', ';', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (!int.TryParse(part.Trim(), out int port))
                {
                    throw new ArgumentException(
                        $"Port '{part}' không hợp lệ.");
                }

                if (port < 1 || port > 65535)
                {
                    throw new ArgumentException(
                        $"Port {port} phải nằm trong khoảng 1 - 65535.");
                }

                if (!ports.Contains(port))
                    ports.Add(port);
            }

            return ports;
        }

        // =========================================================
        // LOCAL SUBNET
        // =========================================================

        private static string? GetLocalSubnetCidr()
        {
            foreach (NetworkInterface adapter
                     in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (adapter.NetworkInterfaceType ==
                    NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties properties =
                    adapter.GetIPProperties();

                // Ưu tiên adapter có default gateway.
                bool hasGateway = properties.GatewayAddresses.Any(
                    gateway =>
                        gateway.Address.AddressFamily ==
                        AddressFamily.InterNetwork
                        && !gateway.Address.Equals(IPAddress.Any));

                if (!hasGateway)
                    continue;

                foreach (UnicastIPAddressInformation info
                         in properties.UnicastAddresses)
                {
                    if (info.Address.AddressFamily !=
                        AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(info.Address))
                        continue;

                    IPAddress mask = info.IPv4Mask;

                    uint ipValue = IpToUInt32(info.Address);
                    uint maskValue = IpToUInt32(mask);

                    if (!IsContiguousMask(maskValue))
                        continue;

                    uint networkValue = ipValue & maskValue;

                    int prefix = CountPrefixBits(maskValue);

                    return $"{UInt32ToIp(networkValue)}/{prefix}";
                }
            }

            // Dự phòng: thử adapter Up đầu tiên có IPv4.
            foreach (NetworkInterface adapter
                     in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (adapter.NetworkInterfaceType ==
                    NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation info
                         in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily !=
                        AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(info.Address))
                        continue;

                    uint maskValue = IpToUInt32(info.IPv4Mask);

                    if (!IsContiguousMask(maskValue))
                        continue;

                    uint networkValue =
                        IpToUInt32(info.Address) & maskValue;

                    return
                        $"{UInt32ToIp(networkValue)}/{CountPrefixBits(maskValue)}";
                }
            }

            return null;
        }

        private static bool IsContiguousMask(uint mask)
        {
            bool zeroSeen = false;

            for (int bit = 31; bit >= 0; bit--)
            {
                bool set = (mask & (1u << bit)) != 0;

                if (!set)
                    zeroSeen = true;
                else if (zeroSeen)
                    return false;
            }

            return true;
        }

        private static int CountPrefixBits(uint mask)
        {
            int count = 0;

            while (mask != 0)
            {
                count += (int)(mask & 1u);
                mask >>= 1;
            }

            return count;
        }

        // =========================================================
        // SORT RESULTS
        // =========================================================

        private void SortResults()
        {
            List<ScanResult> sorted = _results
                .OrderBy(x =>
                {
                    if (IPAddress.TryParse(x.IPAddress, out IPAddress? ip))
                        return IpToUInt32(ip);

                    return uint.MaxValue;
                })
                .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                int oldIndex = _results.IndexOf(sorted[i]);

                if (oldIndex >= 0 && oldIndex != i)
                {
                    _results.Move(oldIndex, i);
                }
            }
        }

        // =========================================================
        // RESET STATISTICS
        // =========================================================

        private void ResetStatistics()
        {
            _total = 0;
            _completed = 0;
            _alive = 0;
            _dead = 0;
            _openPorts = 0;

            ScannedText.Text = "0";
            AliveText.Text = "0";
            DeadText.Text = "0";
            OpenPortsText.Text = "0";
            ScanTimeText.Text = "0 ms";
        }

        // =========================================================
        // EXPORT CSV
        // =========================================================

        private void ExportCsvButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(
                    "Chưa có kết quả để export.",
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            SaveFileDialog dialog = new()
            {
                Title = "Export Scan Results",
                Filter = "CSV File (*.csv)|*.csv",
                FileName = "scan_results.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                StringBuilder csv = new();

                csv.AppendLine(
                    "IP Address,Status,Hostname,RTT (ms),MAC Address,Open Ports");

                foreach (ScanResult result in _results)
                {
                    csv.AppendLine(
                        $"{CsvEscape(result.IPAddress)}," +
                        $"{CsvEscape(result.Status)}," +
                        $"{CsvEscape(result.Hostname)}," +
                        $"{result.RTT}," +
                        $"{CsvEscape(result.MACAddress)}," +
                        $"{CsvEscape(result.OpenPorts)}");
                }

                File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    Encoding.UTF8);

                MessageBox.Show(
                    "Export CSV thành công.",
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =========================================================
        // COPY
        // =========================================================

        private void CopyButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(
                    "Chưa có kết quả để copy.",
                    "My Scan IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            StringBuilder text = new();

            text.AppendLine(
                "IP Address\tStatus\tHostname\tRTT\tMAC Address\tOpen Ports");

            foreach (ScanResult result in _results)
            {
                text.AppendLine(
                    $"{result.IPAddress}\t" +
                    $"{result.Status}\t" +
                    $"{result.Hostname}\t" +
                    $"{result.RTT}\t" +
                    $"{result.MACAddress}\t" +
                    $"{result.OpenPorts}");
            }

            Clipboard.SetText(text.ToString());

            MessageBox.Show(
                "Đã copy kết quả.",
                "My Scan IP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // =========================================================
        // HELPERS
        // =========================================================

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(',') ||
                value.Contains('"') ||
                value.Contains('\n') ||
                value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static uint IpToUInt32(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();

            return
                ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];
        }

        private static IPAddress UInt32ToIp(uint value)
        {
            return new IPAddress(new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            });
        }
    }
}