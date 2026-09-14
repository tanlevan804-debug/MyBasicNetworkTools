using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Native;
using MyBasicNetworkTools.Services;

namespace MyBasicNetworkTools.Views
{
    public partial class PingWindow : Window
    {
        private readonly PingService _pingService;

        private readonly ObservableCollection<PingResult> _results;

        private CancellationTokenSource? _cancellationTokenSource;


        public PingWindow()
        {
            InitializeComponent();

            _pingService = new PingService();

            _results =
                new ObservableCollection<PingResult>();

            ResultGrid.ItemsSource = _results;
        }


        // =========================================================
        // PING BUTTON
        // =========================================================

        private async void PingButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // -----------------------------------------------------
            // 1. GET HOST
            // -----------------------------------------------------

            string host =
                TargetTextBox.Text.Trim();


            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(
                    "Vui lòng nhập IP hoặc tên miền.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // 2. GET COUNT
            // -----------------------------------------------------

            if (!int.TryParse(
                    CountTextBox.Text.Trim(),
                    out int count) ||
                count <= 0)
            {
                MessageBox.Show(
                    "Packet Count phải là số nguyên lớn hơn 0.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (count > 1000)
            {
                MessageBox.Show(
                    "Packet Count tối đa là 1000.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // 3. GET BUFFER SIZE
            // -----------------------------------------------------

            if (!int.TryParse(
                    BufferSizeTextBox.Text.Trim(),
                    out int bufferSize) ||
                bufferSize <= 0)
            {
                MessageBox.Show(
                    "Buffer Size phải là số nguyên lớn hơn 0.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (bufferSize > 65000)
            {
                MessageBox.Show(
                    "Buffer Size tối đa là 65000 bytes.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // 4. GET TIMEOUT
            // -----------------------------------------------------

            if (!int.TryParse(
                    TimeoutTextBox.Text.Trim(),
                    out int timeout) ||
                timeout <= 0)
            {
                MessageBox.Show(
                    "Timeout phải là số nguyên lớn hơn 0.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // 5. GET TTL
            // -----------------------------------------------------

            if (!int.TryParse(
                    TtlTextBox.Text.Trim(),
                    out int ttl) ||
                ttl < 1 ||
                ttl > 255)
            {
                MessageBox.Show(
                    "TTL phải nằm trong khoảng từ 1 đến 255.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // 6. GET DF
            // -----------------------------------------------------

            bool dontFragment =
                DontFragmentCheckBox.IsChecked == true;


            // -----------------------------------------------------
            // 7. RESET UI
            // -----------------------------------------------------

            _results.Clear();


            SentText.Text =
                "Sent: 0";

            ReceivedText.Text =
                "Received: 0";

            LostText.Text =
                "Lost: 0";

            LossText.Text =
                "Packet Loss: 0%";

            MinText.Text =
                "Minimum: - ms";

            MaxText.Text =
                "Maximum: - ms";

            AverageText.Text =
                "Average: - ms";

            StatusText.Text =
                $"Pinging {host}...";


            // -----------------------------------------------------
            // 8. CREATE CANCELLATION TOKEN
            // -----------------------------------------------------

            _cancellationTokenSource =
                new CancellationTokenSource();


            CancellationToken cancellationToken =
                _cancellationTokenSource.Token;


            // -----------------------------------------------------
            // 9. DISABLE INPUT
            // -----------------------------------------------------

            PingButton.IsEnabled = false;

            StopButton.IsEnabled = true;

            TargetTextBox.IsEnabled = false;

            CountTextBox.IsEnabled = false;

            BufferSizeTextBox.IsEnabled = false;

            TimeoutTextBox.IsEnabled = false;

            TtlTextBox.IsEnabled = false;

            DontFragmentCheckBox.IsEnabled = false;


            int sent = 0;

            int received = 0;


            try
            {
                // =================================================
                // SEND PACKETS
                // =================================================

                for (int sequence = 1;
                     sequence <= count;
                     sequence++)
                {
                    // ---------------------------------------------
                    // Check STOP
                    // ---------------------------------------------

                    cancellationToken.ThrowIfCancellationRequested();


                    sent++;


                    // ---------------------------------------------
                    // Update Sent
                    // ---------------------------------------------

                    SentText.Text =
                        $"Sent: {sent}";


                    // ---------------------------------------------
                    // Send ICMP
                    // ---------------------------------------------

                    IcmpPingResult result =
                        await _pingService.PingAsync(
                            host,
                            bufferSize,
                            timeout,
                            ttl,
                            dontFragment,
                            cancellationToken);


                    // ---------------------------------------------
                    // Check STOP
                    // ---------------------------------------------

                    cancellationToken.ThrowIfCancellationRequested();


                    // ---------------------------------------------
                    // Convert result
                    // ---------------------------------------------

                    PingResult pingResult =
                        new PingResult
                        {
                            Sequence = sequence,

                            IPAddress =
                                result.IPAddress,

                            Bytes =
                                result.Bytes,

                            RTT =
                                result.Success
                                    ? result.RTT
                                    : -1,

                            TTL =
                                result.Success
                                    ? result.TTL
                                    : 0,

                            Status =
                                result.Status
                        };


                    // ---------------------------------------------
                    // Add to DataGrid
                    // ---------------------------------------------

                    _results.Add(
                        pingResult);


                    // ---------------------------------------------
                    // Count received
                    // ---------------------------------------------

                    if (result.Success)
                    {
                        received++;
                    }


                    // ---------------------------------------------
                    // Statistics
                    // ---------------------------------------------

                    UpdateStatistics(
                        sent,
                        received);


                    // ---------------------------------------------
                    // Status
                    // ---------------------------------------------

                    StatusText.Text =
                        $"Pinging {host}... " +
                        $"{sequence}/{count}";


                    // ---------------------------------------------
                    // Small delay between packets
                    // ---------------------------------------------

                    if (sequence < count)
                    {
                        await Task.Delay(
                            500,
                            cancellationToken);
                    }
                }


                // =================================================
                // COMPLETED
                // =================================================

                StatusText.Text =
                    $"Ping completed: {host}";
            }
            catch (OperationCanceledException)
            {
                // =================================================
                // STOPPED
                // =================================================

                StatusText.Text =
                    "Ping stopped";


                UpdateStatistics(
                    sent,
                    received);
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "Error";

                MessageBox.Show(
                    ex.Message,
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // =================================================
                // ENABLE UI
                // =================================================

                PingButton.IsEnabled = true;

                StopButton.IsEnabled = false;

                TargetTextBox.IsEnabled = true;

                CountTextBox.IsEnabled = true;

                BufferSizeTextBox.IsEnabled = true;

                TimeoutTextBox.IsEnabled = true;

                TtlTextBox.IsEnabled = true;

                DontFragmentCheckBox.IsEnabled = true;


                _cancellationTokenSource?.Dispose();

                _cancellationTokenSource = null;
            }
        }


        // =========================================================
        // STOP BUTTON
        // =========================================================

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


        // =========================================================
        // CLEAR BUTTON
        // =========================================================

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // Không cho Clear trong lúc Ping
            if (_cancellationTokenSource != null)
            {
                MessageBox.Show(
                    "Vui lòng STOP Ping trước khi CLEAR.",
                    "My Ping",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }


            // Clear results

            _results.Clear();


            // Reset statistics

            SentText.Text =
                "Sent: 0";

            ReceivedText.Text =
                "Received: 0";

            LostText.Text =
                "Lost: 0";

            LossText.Text =
                "Packet Loss: 0%";

            MinText.Text =
                "Minimum: - ms";

            MaxText.Text =
                "Maximum: - ms";

            AverageText.Text =
                "Average: - ms";

            StatusText.Text =
                "Ready";
        }


        // =========================================================
        // UPDATE STATISTICS
        // =========================================================

        private void UpdateStatistics(
            int sent,
            int received)
        {
            int lost =
                sent - received;


            double lossPercent =
                sent > 0
                    ? (double)lost / sent * 100
                    : 0;


            // -----------------------------------------------------
            // Packet statistics
            // -----------------------------------------------------

            SentText.Text =
                $"Sent: {sent}";

            ReceivedText.Text =
                $"Received: {received}";

            LostText.Text =
                $"Lost: {lost}";

            LossText.Text =
                $"Packet Loss: {lossPercent:F1}%";


            // -----------------------------------------------------
            // RTT statistics
            // -----------------------------------------------------

            var successfulResults =
                _results
                    .Where(x =>
                        x.Status == "Success" &&
                        x.RTT >= 0)
                    .Select(x => x.RTT)
                    .ToList();


            if (successfulResults.Count == 0)
            {
                MinText.Text =
                    "Minimum: - ms";

                MaxText.Text =
                    "Maximum: - ms";

                AverageText.Text =
                    "Average: - ms";

                return;
            }


            long minimum =
                successfulResults.Min();

            long maximum =
                successfulResults.Max();

            double average =
                successfulResults.Average();


            MinText.Text =
                $"Minimum: {minimum} ms";

            MaxText.Text =
                $"Maximum: {maximum} ms";

            AverageText.Text =
                $"Average: {average:F1} ms";
        }


        // =========================================================
        // WINDOW CLOSED
        // =========================================================

        protected override void OnClosed(
            EventArgs e)
        {
            _cancellationTokenSource?.Cancel();

            base.OnClosed(e);
        }
    }
}