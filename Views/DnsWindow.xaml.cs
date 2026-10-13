using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

using MyBasicNetworkTools.Services;

// Alias để không còn ambiguous DnsQueryResult
using ModelDnsQueryResult = MyBasicNetworkTools.Models.DnsQueryResult;
using ModelDnsRecord = MyBasicNetworkTools.Models.DnsRecord;

namespace MyBasicNetworkTools.Views
{
    public partial class DnsWindow : Window
    {
        private readonly DnsService _dnsService;

        private CancellationTokenSource? _cancellationTokenSource;

        // LUÔN sử dụng DnsQueryResult trong Models
        private ModelDnsQueryResult? _lastResult;


        // =========================================================
        // CONSTRUCTOR
        // =========================================================

        public DnsWindow()
        {
            InitializeComponent();

            _dnsService = new DnsService();

            StatusText.Text = "Ready";

            ClearResultInformation();
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


            // -----------------------------------------------------
            // VALIDATE DOMAIN
            // -----------------------------------------------------

            if (string.IsNullOrWhiteSpace(domain))
            {
                MessageBox.Show(
                    "Vui lòng nhập Domain.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // VALIDATE TIMEOUT
            // -----------------------------------------------------

            if (!int.TryParse(
                    TimeoutTextBox.Text.Trim(),
                    out int timeout))
            {
                MessageBox.Show(
                    "Timeout phải là số nguyên.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (timeout < 100 || timeout > 60000)
            {
                MessageBox.Show(
                    "Timeout phải nằm trong khoảng 100 - 60000 ms.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // -----------------------------------------------------
            // VALIDATE PORT
            // -----------------------------------------------------

            if (!int.TryParse(
                    PortTextBox.Text.Trim(),
                    out int port))
            {
                MessageBox.Show(
                    "Port phải là số nguyên.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (port < 1 || port > 65535)
            {
                MessageBox.Show(
                    "Port phải nằm trong khoảng 1 - 65535.",
                    "DNS Query",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

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

            RawPacketTextBox.Clear();

            StatusText.Text = "QUERYING...";


            _cancellationTokenSource?.Dispose();

            _cancellationTokenSource =
                new CancellationTokenSource();


            try
            {
                // =================================================
                // CALL DNS SERVICE
                //
                // Quan trọng:
                // Service hiện tại đang trả về
                // Services.DnsQueryResult
                // =================================================

                var serviceResult =
                    await _dnsService.QueryAsync(
                        domain,
                        dnsServer,
                        recordType,
                        timeout,
                        port,
                        _cancellationTokenSource.Token);


                // =================================================
                // CONVERT
                //
                // Services.DnsQueryResult
                //          ↓
                // Models.DnsQueryResult
                // =================================================

                ModelDnsQueryResult result =
                    ConvertToModelResult(serviceResult);


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


                FlagsText.Text =
                    flags;


                CountsText.Text =
                    $"{result.QuestionCount} / " +
                    $"{result.AnswerCount} / " +
                    $"{result.AuthorityCount} / " +
                    $"{result.AdditionalCount}";


                // =================================================
                // EDNS
                // =================================================

                if (result.EdnsPresent)
                {
                    EdnsText.Text =
                        $"Present | " +
                        $"UDP Payload={result.EdnsUdpPayloadSize} | " +
                        $"Version={result.EdnsVersion} | " +
                        $"Extended RCODE={result.EdnsExtendedRCode} | " +
                        $"Flags=0x{result.EdnsFlags:X4}";
                }
                else
                {
                    EdnsText.Text =
                        "Not present";
                }


                // =================================================
                // INFORMATION
                // =================================================

                StatusText.Text =
                    result.Status;


                DnsServerUsedText.Text =
                    result.DnsServer;


                ProtocolText.Text =
                    result.Protocol;


                // =================================================
                // UDP TIME
                // =================================================

                UdpQueryTimeText.Text =
                    $"{result.UdpQueryTime} ms";


                // =================================================
                // TCP TIME
                // =================================================

                TcpQueryTimeText.Text =
                    result.TcpQueryTime > 0
                        ? $"{result.TcpQueryTime} ms"
                        : "-";


                // =================================================
                // TOTAL TIME
                // =================================================

                TotalQueryTimeText.Text =
                    result.TotalQueryTime > 0
                        ? $"{result.TotalQueryTime} ms"
                        : $"{result.QueryTime} ms";


                // =================================================
                // AA / TC
                // =================================================

                AaText.Text =
                    result.AuthoritativeAnswer
                        ? "YES"
                        : "NO";


                TcText.Text =
                    result.Truncated
                        ? "YES"
                        : "NO";


                // =================================================
                // RAW DNS
                // =================================================

                RawPacketTextBox.Text =
                    result.RawPacketHex;
            }
            catch (TimeoutException ex)
            {
                StatusText.Text =
                    "TIMEOUT";


                MessageBox.Show(
                    ex.Message,
                    "DNS Query Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text =
                    "CANCELLED";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "ERROR";


                MessageBox.Show(
                    ex.Message,
                    "DNS Query Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                QueryButton.IsEnabled =
                    true;

                StopButton.IsEnabled =
                    false;


                _cancellationTokenSource?.Dispose();

                _cancellationTokenSource =
                    null;
            }
        }


        // =========================================================
        // CONVERT SERVICE RESULT → MODEL RESULT
        // =========================================================

        private static ModelDnsQueryResult ConvertToModelResult(
            MyBasicNetworkTools.Services.DnsQueryResult source)
        {
            var result =
                new ModelDnsQueryResult
                {
                    // ---------------------------------------------
                    // DNS HEADER
                    // ---------------------------------------------

                    TransactionId =
                        source.TransactionId,

                    IsResponse =
                        source.IsResponse,

                    Opcode =
                        source.Opcode,

                    AuthoritativeAnswer =
                        source.AuthoritativeAnswer,

                    Truncated =
                        source.Truncated,

                    RecursionDesired =
                        source.RecursionDesired,

                    RecursionAvailable =
                        source.RecursionAvailable,

                    RCode =
                        source.RCode,

                    Status =
                        source.Status,


                    // ---------------------------------------------
                    // COUNTS
                    // ---------------------------------------------

                    QuestionCount =
                        source.QuestionCount,

                    AnswerCount =
                        source.AnswerCount,

                    AuthorityCount =
                        source.AuthorityCount,

                    AdditionalCount =
                        source.AdditionalCount,


                    // ---------------------------------------------
                    // CONNECTION
                    // ---------------------------------------------

                    DnsServer =
                        source.DnsServer,

                    Protocol =
                        source.Protocol,

                    QueryTime =
                        source.QueryTime,

                    // ---------------------------------------------
                    // RAW
                    // ---------------------------------------------

                    RawPacketHex =
                        source.RawPacketHex
                };


            // =====================================================
            // TRY COPY EDNS
            //
            // Nếu Service cũ chưa có EDNS thì mặc định:
            // EdnsPresent = false
            // =====================================================

            try
            {
                result.EdnsPresent =
                    source.EdnsPresent;

                result.EdnsUdpPayloadSize =
                    source.EdnsUdpPayloadSize;

                result.EdnsExtendedRCode =
                    source.EdnsExtendedRCode;

                result.EdnsVersion =
                    source.EdnsVersion;

                result.EdnsFlags =
                    source.EdnsFlags;
            }
            catch
            {
                result.EdnsPresent = false;
            }


            // =====================================================
            // TIME
            // =====================================================

            try
            {
                result.UdpQueryTime =
                    source.UdpQueryTime;

                result.TcpQueryTime =
                    source.TcpQueryTime;

                result.TotalQueryTime =
                    source.TotalQueryTime;
            }
            catch
            {
                result.UdpQueryTime =
                    source.QueryTime;

                result.TcpQueryTime =
                    0;

                result.TotalQueryTime =
                    source.QueryTime;
            }


            // =====================================================
            // RECORDS
            // =====================================================

            if (source.Records != null)
            {
                foreach (var record in source.Records)
                {
                    var modelRecord =
                        new ModelDnsRecord
                        {
                            RecordType =
                                record.RecordType,

                            Name =
                                record.Name,

                            Value =
                                record.Value,

                            TTL =
                                record.TTL,

                            Section =
                                record.Section
                        };


                    // ---------------------------------------------
                    // TYPE CODE
                    // ---------------------------------------------

                    try
                    {
                        modelRecord.RecordTypeCode =
                            record.RecordTypeCode;
                    }
                    catch
                    {
                        modelRecord.RecordTypeCode = 0;
                    }


                    // ---------------------------------------------
                    // CLASS
                    // ---------------------------------------------

                    try
                    {
                        modelRecord.Class =
                            record.Class;
                    }
                    catch
                    {
                        modelRecord.Class = 1;
                    }


                    result.Records.Add(modelRecord);
                }
            }


            return result;
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


            ResultGrid.ItemsSource =
                null;


            _lastResult =
                null;


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
                    Title =
                        "Export DNS Result",

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
                    Title =
                        "Export DNS CSV",

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


                // -------------------------------------------------
                // HEADER
                // -------------------------------------------------

                csv.AppendLine(
                    "Record Type,Type Code,Name,Data/Value,TTL,Class,Section");


                // -------------------------------------------------
                // RECORDS
                // -------------------------------------------------

                foreach (var record in _lastResult.Records)
                {
                    csv.AppendLine(
                        $"{CsvEscape(record.RecordType)}," +
                        $"{record.RecordTypeCode}," +
                        $"{CsvEscape(record.Name)}," +
                        $"{CsvEscape(record.Value)}," +
                        $"{record.TTL}," +
                        $"{record.Class}," +
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
        // GET DNS SERVER
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


        // =========================================================
        // GET RECORD TYPE
        // =========================================================

        private string GetSelectedRecordType()
        {
            return
                (RecordTypeComboBox.SelectedItem
                    as ComboBoxItem)
                    ?.Content
                    ?.ToString()
                ?? "A";
        }


        // =========================================================
        // CLEAR RESULT INFORMATION
        // =========================================================

        private void ClearResultInformation()
        {
            TransactionIdText.Text =
                "-";


            OpcodeText.Text =
                "-";


            RcodeText.Text =
                "-";


            FlagsText.Text =
                "-";


            CountsText.Text =
                "-";


            EdnsText.Text =
                "-";


            DnsServerUsedText.Text =
                "-";


            ProtocolText.Text =
                "-";


            UdpQueryTimeText.Text =
                "-";


            TcpQueryTimeText.Text =
                "-";


            TotalQueryTimeText.Text =
                "-";


            AaText.Text =
                "-";


            TcText.Text =
                "-";


            RawPacketTextBox.Clear();
        }


        // =========================================================
        // BUILD RESULT TEXT
        // =========================================================

        private string BuildResultText(
            ModelDnsQueryResult result)
        {
            StringBuilder text =
                new StringBuilder();


            text.AppendLine(
                "========== MY DNS QUERY ==========");

            text.AppendLine();


            // -----------------------------------------------------
            // DNS HEADER
            // -----------------------------------------------------

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


            // -----------------------------------------------------
            // COUNTS
            // -----------------------------------------------------

            text.AppendLine(
                $"Questions      : {result.QuestionCount}");

            text.AppendLine(
                $"Answers        : {result.AnswerCount}");

            text.AppendLine(
                $"Authority      : {result.AuthorityCount}");

            text.AppendLine(
                $"Additional     : {result.AdditionalCount}");

            text.AppendLine();


            // -----------------------------------------------------
            // EDNS
            // -----------------------------------------------------

            text.AppendLine(
                "========== EDNS ==========");

            text.AppendLine(
                $"EDNS Present   : {(result.EdnsPresent ? "YES" : "NO")}");


            if (result.EdnsPresent)
            {
                text.AppendLine(
                    $"UDP Payload    : {result.EdnsUdpPayloadSize}");

                text.AppendLine(
                    $"Version        : {result.EdnsVersion}");

                text.AppendLine(
                    $"Extended RCODE : {result.EdnsExtendedRCode}");

                text.AppendLine(
                    $"EDNS Flags     : 0x{result.EdnsFlags:X4}");
            }


            text.AppendLine();


            // -----------------------------------------------------
            // CONNECTION
            // -----------------------------------------------------

            text.AppendLine(
                "========== CONNECTION ==========");

            text.AppendLine(
                $"DNS Server     : {result.DnsServer}");

            text.AppendLine(
                $"Protocol       : {result.Protocol}");

            text.AppendLine(
                $"UDP Time       : {result.UdpQueryTime} ms");

            text.AppendLine(
                $"TCP Time       : {result.TcpQueryTime} ms");

            text.AppendLine(
                $"Total Time     : {result.TotalQueryTime} ms");

            text.AppendLine();


            // -----------------------------------------------------
            // RECORDS
            // -----------------------------------------------------

            text.AppendLine(
                "========== RECORDS ==========");


            foreach (var record in result.Records)
            {
                text.AppendLine(
                    $"{record.RecordType,-8} " +
                    $"TYPE={record.RecordTypeCode,-4} " +
                    $"{record.Name,-35} " +
                    $"{record.Value,-50} " +
                    $"TTL={record.TTL,-6} " +
                    $"CLASS={record.Class,-5} " +
                    $"{record.Section}");
            }


            text.AppendLine();


            // -----------------------------------------------------
            // RAW DNS
            // -----------------------------------------------------

            text.AppendLine(
                "========== RAW DNS RESPONSE ==========");

            text.AppendLine(
                result.RawPacketHex);


            return text.ToString();
        }


        // =========================================================
        // CSV ESCAPE
        // =========================================================

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