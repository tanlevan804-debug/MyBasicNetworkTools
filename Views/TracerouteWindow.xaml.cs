using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using Microsoft.Win32;

using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Services;

namespace MyBasicNetworkTools.Views
{
    public partial class TracerouteWindow : Window
    {
        private readonly TracerouteService _tracerouteService;

        private CancellationTokenSource? _cancellationTokenSource;

        private readonly ObservableCollection<TracerouteResult> _results
            = new();

        public TracerouteWindow()
        {
            InitializeComponent();

            _tracerouteService =
                new TracerouteService();

            ResultGrid.ItemsSource = _results;
        }


        // =====================================================
        // START
        // =====================================================

        private async void StartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string host =
                TargetTextBox.Text.Trim();


            // -------------------------------------------------
            // HOST
            // -------------------------------------------------

            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(
                    "Vui lòng nhập IP hoặc tên miền.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -------------------------------------------------
            // MAX HOPS
            // -------------------------------------------------

            if (!int.TryParse(
                    MaxHopsTextBox.Text.Trim(),
                    out int maxHops) ||
                maxHops < 1 ||
                maxHops > 255)
            {
                MessageBox.Show(
                    "Max Hops phải nằm trong khoảng 1 - 255.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -------------------------------------------------
            // TIMEOUT
            // -------------------------------------------------

            if (!int.TryParse(
                    TimeoutTextBox.Text.Trim(),
                    out int timeout) ||
                timeout < 100 ||
                timeout > 60000)
            {
                MessageBox.Show(
                    "Timeout phải nằm trong khoảng 100 - 60000 ms.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -------------------------------------------------
            // PROBES
            // -------------------------------------------------

            if (ProbesComboBox.SelectedItem
                is not ComboBoxItem selectedItem)
            {
                MessageBox.Show(
                    "Vui lòng chọn số lượng Probes per Hop.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (!int.TryParse(
                    selectedItem.Content?.ToString(),
                    out int probesPerHop))
            {
                MessageBox.Show(
                    "Probes per Hop không hợp lệ.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -------------------------------------------------
            // REVERSE DNS
            // -------------------------------------------------

            bool resolveHostnames =
                ResolveHostnamesCheckBox.IsChecked == true;


            // -------------------------------------------------
            // CLEAR KẾT QUẢ CŨ
            // -------------------------------------------------

            _results.Clear();


            // -------------------------------------------------
            // CANCELLATION TOKEN
            // -------------------------------------------------

            _cancellationTokenSource =
                new CancellationTokenSource();

            CancellationToken token =
                _cancellationTokenSource.Token;


            // -------------------------------------------------
            // PROGRESS REALTIME
            // -------------------------------------------------

            Progress<TracerouteResult> progress =
                new Progress<TracerouteResult>(
                    hop =>
                    {
                        _results.Add(hop);

                        // Scroll tới hop mới nhất
                        if (_results.Count > 0)
                        {
                            ResultGrid.ScrollIntoView(
                                _results[_results.Count - 1]);
                        }

                        StatusText.Text =
                            $"Hop {hop.Hop}: {hop.Status}";
                    });


            // -------------------------------------------------
            // DISABLE CONTROLS
            // -------------------------------------------------

            SetControlsEnabled(false);

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;


            StatusText.Text =
                $"Resolving {host}...";


            try
            {
                // =============================================
                // TRACE
                // =============================================

                await _tracerouteService.TraceAsync(
                    host,
                    maxHops,
                    timeout,
                    probesPerHop,
                    resolveHostnames,
                    progress,
                    token);


                // =============================================
                // COMPLETE
                // =============================================

                StatusText.Text =
                    $"Trace completed: {host}";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text =
                    "Trace stopped.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "Trace error.";

                MessageBox.Show(
                    ex.Message,
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetControlsEnabled(true);

                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;

                _cancellationTokenSource?.Dispose();

                _cancellationTokenSource = null;
            }
        }


        // =====================================================
        // ENABLE / DISABLE INPUT
        // =====================================================

        private void SetControlsEnabled(bool enabled)
        {
            TargetTextBox.IsEnabled = enabled;

            MaxHopsTextBox.IsEnabled = enabled;

            TimeoutTextBox.IsEnabled = enabled;

            ProbesComboBox.IsEnabled = enabled;

            ResolveHostnamesCheckBox.IsEnabled = enabled;

            ClearButton.IsEnabled = enabled;

            CopyButton.IsEnabled = enabled &&
                                   _results.Count > 0;

            ExportTxtButton.IsEnabled = enabled &&
                                       _results.Count > 0;

            ExportCsvButton.IsEnabled = enabled &&
                                       _results.Count > 0;
        }


        // =====================================================
        // STOP
        // =====================================================

        private void StopButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();

                StatusText.Text =
                    "Stopping...";
            }
        }


        // =====================================================
        // CLEAR
        // =====================================================

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                MessageBox.Show(
                    "Vui lòng STOP Trace trước khi CLEAR.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _results.Clear();

            StatusText.Text =
                "Ready";

            CopyButton.IsEnabled = false;

            ExportTxtButton.IsEnabled = false;

            ExportCsvButton.IsEnabled = false;
        }


        // =====================================================
        // COPY
        // =====================================================

        private void CopyButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(
                    "Chưa có kết quả để copy.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string text =
                BuildTextResult();

            Clipboard.SetText(text);

            StatusText.Text =
                "Trace result copied to clipboard.";

            MessageBox.Show(
                "Đã copy toàn bộ kết quả traceroute.",
                "My Tracert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        // =====================================================
        // EXPORT TXT
        // =====================================================

        private void ExportTxtButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(
                    "Chưa có kết quả để export.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }


            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title = "Export Traceroute TXT",

                    Filter =
                        "Text file (*.txt)|*.txt",

                    FileName =
                        "tracert_result.txt"
                };


            if (dialog.ShowDialog() != true)
                return;


            try
            {
                string text =
                    BuildTextResult();

                File.WriteAllText(
                    dialog.FileName,
                    text,
                    new UTF8Encoding(true));

                StatusText.Text =
                    "TXT exported successfully.";

                MessageBox.Show(
                    "Đã export kết quả sang TXT.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể export TXT:\n{ex.Message}",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // =====================================================
        // EXPORT CSV
        // =====================================================

        private void ExportCsvButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show(
                    "Chưa có kết quả để export.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }


            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title = "Export Traceroute CSV",

                    Filter =
                        "CSV file (*.csv)|*.csv",

                    FileName =
                        "tracert_result.csv"
                };


            if (dialog.ShowDialog() != true)
                return;


            try
            {
                StringBuilder csv =
                    new StringBuilder();


                // =========================================
                // HEADER
                // =========================================

                csv.AppendLine(
                    "Hop,RTT 1 (ms),RTT 2 (ms),RTT 3 (ms)," +
                    "Average RTT (ms),IP Address,Hostname,Status");


                // =========================================
                // DATA
                // =========================================

                foreach (TracerouteResult result in _results)
                {
                    csv.AppendLine(
                        string.Join(",",
                            result.Hop,

                            CsvValue(result.RTT1),

                            CsvValue(result.RTT2),

                            CsvValue(result.RTT3),

                            CsvValue(result.AverageRTT),

                            CsvValue(result.IPAddress),

                            CsvValue(result.Hostname),

                            CsvValue(result.Status)
                        ));
                }


                File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    new UTF8Encoding(true));


                StatusText.Text =
                    "CSV exported successfully.";


                MessageBox.Show(
                    "Đã export kết quả sang CSV.",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể export CSV:\n{ex.Message}",
                    "My Tracert",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        // =====================================================
        // BUILD TXT
        // =====================================================

        private string BuildTextResult()
        {
            StringBuilder text =
                new StringBuilder();


            string target =
                TargetTextBox.Text.Trim();


            text.AppendLine(
                "==============================================");

            text.AppendLine(
                "              MY TRACERT RESULT");

            text.AppendLine(
                "==============================================");

            text.AppendLine();

            text.AppendLine(
                $"Target Host : {target}");

            text.AppendLine(
                $"Max Hops    : {MaxHopsTextBox.Text.Trim()}");

            text.AppendLine(
                $"Timeout     : {TimeoutTextBox.Text.Trim()} ms");

            text.AppendLine(
                $"Probes/Hop  : {GetSelectedProbeCount()}");

            text.AppendLine(
                $"Reverse DNS : " +
                $"{(ResolveHostnamesCheckBox.IsChecked == true ? "ON" : "OFF")}");

            text.AppendLine();

            text.AppendLine(
                "Hop\tRTT1\tRTT2\tRTT3\tAverage\tIP Address\tHostname\tStatus");

            text.AppendLine(
                "--------------------------------------------------------------------------------");


            foreach (TracerouteResult result in _results)
            {
                text.AppendLine(
                    $"{result.Hop}\t" +
                    $"{FormatRtt(result.RTT1)}\t" +
                    $"{FormatRtt(result.RTT2)}\t" +
                    $"{FormatRtt(result.RTT3)}\t" +
                    $"{FormatRtt(result.AverageRTT)}\t" +
                    $"{result.IPAddress}\t" +
                    $"{result.Hostname}\t" +
                    $"{result.Status}");
            }


            text.AppendLine();

            text.AppendLine(
                "==============================================");

            text.AppendLine(
                "Generated by My Basic Network Tools");

            text.AppendLine(
                "==============================================");


            return text.ToString();
        }


        // =====================================================
        // FORMAT RTT
        // =====================================================

        private static string FormatRtt(double value)
        {
            if (value < 0)
                return "*";

            return $"{value:0.0} ms";
        }


        // =====================================================
        // CSV VALUE
        // =====================================================

        private static string CsvValue(double value)
        {
            if (value < 0)
                return "";

            return value
                .ToString("0.0", CultureInfo.InvariantCulture);
        }


        private static string CsvValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value =
                value.Replace(
                    "\"",
                    "\"\"");

            return $"\"{value}\"";
        }


        // =====================================================
        // GET PROBE COUNT
        // =====================================================

        private int GetSelectedProbeCount()
        {
            if (ProbesComboBox.SelectedItem
                is ComboBoxItem item &&
                int.TryParse(
                    item.Content?.ToString(),
                    out int count))
            {
                return count;
            }

            return 3;
        }


        // =====================================================
        // WINDOW CLOSED
        // =====================================================

        protected override void OnClosed(
            EventArgs e)
        {
            _cancellationTokenSource?.Cancel();

            base.OnClosed(e);
        }
    }


    // =========================================================
    // RTT CONVERTER
    // =========================================================

    public class RttConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value == null)
                return "*";


            if (!double.TryParse(
                    value.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out double rtt))
            {
                return "*";
            }


            if (rtt < 0)
                return "*";


            return $"{rtt:0.0} ms";
        }


        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}