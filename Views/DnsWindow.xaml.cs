using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Services;

namespace MyBasicNetworkTools.Views
{
    public partial class DnsWindow : Window
    {
        private readonly DnsService _dnsService;

        private CancellationTokenSource? _cancellationTokenSource;

        private DnsQueryResult? _lastResult;

        public DnsWindow()
        {
            InitializeComponent();

            _dnsService = new DnsService();

            StatusText.Text = "Ready";
        }

        // =========================================================
        // QUERY
        // =========================================================

        private async void QueryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string domain =
                DomainTextBox.Text.Trim();

            string dnsServer =
                GetSelectedDnsServer();

            string recordType =
                GetSelectedRecordType();

            if (string.IsNullOrWhiteSpace(domain))
            {
                MessageBox.Show(
                    "Vui lòng nhập Domain.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!int.TryParse(
                    TimeoutTextBox.Text.Trim(),
                    out int timeout))
            {
                MessageBox.Show(
                    "Timeout phải là số nguyên.",
                    "DNS Query");

                return;
            }

            if (!int.TryParse(
                    PortTextBox.Text.Trim(),
                    out int port))
            {
                MessageBox.Show(
                    "Port phải là số nguyên.",
                    "DNS Query");

                return;
            }

            // =====================================================
            // PREPARE
            // =====================================================

            QueryButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            _lastResult = null;

            ResultGrid.ItemsSource = null;

            ClearResultInformation();

            StatusText.Text = "QUERYING...";

            _cancellationTokenSource =
                new CancellationTokenSource();

            try
            {
                DnsQueryResult result =
                    await _dnsService.QueryAsync(
                        domain,
                        dnsServer,
                        recordType,
                        timeout,
                        port,
                        _cancellationTokenSource.Token);

                _lastResult = result;

                // =================================================
                // RECORDS
                // =================================================

                ResultGrid.ItemsSource =
                    result.Records;

                // =================================================
                // DNS HEADER
                // =================================================

                TransactionIdText.Text =
                    $"0x{result.TransactionId:X4}";

                OpcodeText.Text =
                    result.Opcode.ToString();

                RcodeText.Text =
                    $"{result.Status} ({result.RCode})";

                string flags =
                    $"QR={(result.IsResponse ? "1" : "0")}   " +
                    $"RD={(result.RecursionDesired ? "1" : "0")}   " +
                    $"RA={(result.RecursionAvailable ? "1" : "0")}   " +
                    $"AA={(result.AuthoritativeAnswer ? "1" : "0")}   " +
                    $"TC={(result.Truncated ? "1" : "0")}";

                FlagsText.Text = flags;

                CountsText.Text =
                    $"{result.QuestionCount} / " +
                    $"{result.AnswerCount} / " +
                    $"{result.AuthorityCount} / " +
                    $"{result.AdditionalCount}";

                // =================================================
                // INFORMATION
                // =================================================

                StatusText.Text =
                    result.Status;

                DnsServerUsedText.Text =
                    result.DnsServer;

                ProtocolText.Text =
                    result.Protocol;

                QueryTimeText.Text =
                    $"{result.QueryTime} ms";

                AaText.Text =
                    result.AuthoritativeAnswer
                        ? "YES"
                        : "NO";

                TcText.Text =
                    result.Truncated
                        ? "YES"
                        : "NO";

                // =================================================
                // RAW
                // =================================================

                RawPacketTextBox.Text =
                    result.RawPacketHex;
            }
            catch (TimeoutException ex)
            {
                StatusText.Text = "TIMEOUT";

                MessageBox.Show(
                    ex.Message,
                    "DNS Query Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "CANCELLED";
            }
            catch (Exception ex)
            {
                StatusText.Text = "ERROR";

                MessageBox.Show(
                    ex.Message,
                    "DNS Query Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                QueryButton.IsEnabled = true;
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
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();

                StatusText.Text =
                    "CANCELLING...";
            }
        }

        // =========================================================
        // CLEAR
        // =========================================================

        private void ClearButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();

            DomainTextBox.Clear();

            TimeoutTextBox.Text =
                "5000";

            PortTextBox.Text =
                "53";

            DnsServerComboBox.SelectedIndex =
                0;

            RecordTypeComboBox.SelectedIndex =
                0;

            ResultGrid.ItemsSource = null;

            _lastResult = null;

            ClearResultInformation();

            StatusText.Text =
                "Ready";
        }

        // =========================================================
        // COPY
        // =========================================================

        private void CopyButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_lastResult == null)
            {
                MessageBox.Show(
                    "Chưa có kết quả DNS để sao chép.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string text =
                BuildResultText(_lastResult);

            Clipboard.SetText(text);

            MessageBox.Show(
                "Đã copy kết quả DNS.",
                "DNS Query",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // =========================================================
        // EXPORT TXT
        // =========================================================

        private void ExportTxtButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_lastResult == null)
            {
                MessageBox.Show(
                    "Chưa có kết quả để export.",
                    "DNS Query");

                return;
            }

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title = "Export DNS Result",
                    Filter =
                        "Text File (*.txt)|*.txt",
                    FileName =
                        "dns_result.txt"
                };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                System.IO.File.WriteAllText(
                    dialog.FileName,
                    BuildResultText(_lastResult),
                    Encoding.UTF8);

                MessageBox.Show(
                    "Export TXT thành công.",
                    "DNS Query",
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
        // EXPORT CSV
        // =========================================================

        private void ExportCsvButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_lastResult == null)
            {
                MessageBox.Show(
                    "Chưa có kết quả để export.",
                    "DNS Query");

                return;
            }

            SaveFileDialog dialog =
                new SaveFileDialog
                {
                    Title = "Export DNS CSV",
                    Filter =
                        "CSV File (*.csv)|*.csv",
                    FileName =
                        "dns_records.csv"
                };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                StringBuilder csv =
                    new StringBuilder();

                csv.AppendLine(
                    "Record Type,Name,Data/Value,TTL,Section");

                foreach (var record in _lastResult.Records)
                {
                    csv.AppendLine(
                        $"{CsvEscape(record.RecordType)}," +
                        $"{CsvEscape(record.Name)}," +
                        $"{CsvEscape(record.Value)}," +
                        $"{record.TTL}," +
                        $"{CsvEscape(record.Section)}");
                }

                System.IO.File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    Encoding.UTF8);

                MessageBox.Show(
                    "Export CSV thành công.",
                    "DNS Query",
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
        // HELPERS
        // =========================================================

        private string GetSelectedDnsServer()
        {
            return
                (DnsServerComboBox.SelectedItem
                    as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "8.8.8.8";
        }

        private string GetSelectedRecordType()
        {
            return
                (RecordTypeComboBox.SelectedItem
                    as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "A";
        }

        private void ClearResultInformation()
        {
            TransactionIdText.Text = "-";

            OpcodeText.Text = "-";

            RcodeText.Text = "-";

            FlagsText.Text = "-";

            CountsText.Text = "-";

            DnsServerUsedText.Text = "-";

            ProtocolText.Text = "-";

            QueryTimeText.Text = "-";

            AaText.Text = "-";

            TcText.Text = "-";

            RawPacketTextBox.Clear();
        }

        private string BuildResultText(
            DnsQueryResult result)
        {
            StringBuilder text =
                new StringBuilder();

            text.AppendLine(
                "========== MY DNS QUERY ==========");

            text.AppendLine();

            text.AppendLine(
                $"Transaction ID : 0x{result.TransactionId:X4}");

            text.AppendLine(
                $"Response       : {(result.IsResponse ? "YES" : "NO")}");

            text.AppendLine(
                $"Opcode         : {result.Opcode}");

            text.AppendLine(
                $"Status         : {result.Status}");

            text.AppendLine(
                $"RCODE          : {result.RCode}");

            text.AppendLine(
                $"AA             : {(result.AuthoritativeAnswer ? "YES" : "NO")}");

            text.AppendLine(
                $"TC             : {(result.Truncated ? "YES" : "NO")}");

            text.AppendLine(
                $"RD             : {(result.RecursionDesired ? "YES" : "NO")}");

            text.AppendLine(
                $"RA             : {(result.RecursionAvailable ? "YES" : "NO")}");

            text.AppendLine();

            text.AppendLine(
                $"Questions      : {result.QuestionCount}");

            text.AppendLine(
                $"Answers        : {result.AnswerCount}");

            text.AppendLine(
                $"Authority      : {result.AuthorityCount}");

            text.AppendLine(
                $"Additional     : {result.AdditionalCount}");

            text.AppendLine();

            text.AppendLine(
                $"DNS Server     : {result.DnsServer}");

            text.AppendLine(
                $"Protocol       : {result.Protocol}");

            text.AppendLine(
                $"Query Time     : {result.QueryTime} ms");

            text.AppendLine();

            text.AppendLine(
                "========== RECORDS ==========");

            foreach (var record in result.Records)
            {
                text.AppendLine(
                    $"{record.RecordType,-8} " +
                    $"{record.Name,-35} " +
                    $"{record.Value,-50} " +
                    $"TTL={record.TTL,-6} " +
                    $"{record.Section}");
            }

            text.AppendLine();

            text.AppendLine(
                "========== RAW DNS RESPONSE ==========");

            text.AppendLine(
                result.RawPacketHex);

            return text.ToString();
        }

        private static string CsvEscape(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains(',') ||
                value.Contains('"') ||
                value.Contains('\n') ||
                value.Contains('\r'))
            {
                return "\"" +
                       value.Replace("\"", "\"\"") +
                       "\"";
            }

            return value;
        }
    }
}