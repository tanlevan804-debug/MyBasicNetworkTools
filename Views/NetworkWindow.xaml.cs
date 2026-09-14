using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

using MyBasicNetworkTools.Native;

namespace MyBasicNetworkTools.Views
{
    public partial class NetworkWindow : Window
    {
        private readonly IpHelper _ipHelper;

        // Lưu dữ liệu gốc
        private List<NetworkAdapterInfo> _networkData = new();

        // Lưu trạng thái sort hiện tại
        private string _currentSortColumn = "";

        private bool _sortAscending = true;

        public NetworkWindow()
        {
            InitializeComponent();

            _ipHelper = new IpHelper();

            LoadNetworkInformation();
        }

        // =========================================================
        // LOAD NETWORK INFORMATION
        // =========================================================

        private void LoadNetworkInformation()
        {
            try
            {
                // Gọi Windows IP Helper API
                var nativeAdapters =
                    _ipHelper.GetAdapters();

                var adapters =
                    NetworkInterface
                        .GetAllNetworkInterfaces()

                        // Chỉ lấy adapter đang hoạt động
                        .Where(adapter =>
                            adapter.OperationalStatus ==
                            OperationalStatus.Up)

                        // Bỏ Loopback
                        .Where(adapter =>
                            adapter.NetworkInterfaceType !=
                            NetworkInterfaceType.Loopback)

                        // Bỏ Tunnel
                        .Where(adapter =>
                            adapter.NetworkInterfaceType !=
                            NetworkInterfaceType.Tunnel)

                        .ToList();

                _networkData =
                    adapters.Select(adapter =>
                    {
                        var properties =
                            adapter.GetIPProperties();

                        // =================================================
                        // IPv4
                        // =================================================

                        var ipv4 =
                            properties
                                .UnicastAddresses
                                .FirstOrDefault(
                                    x =>
                                        x.Address.AddressFamily ==
                                        AddressFamily.InterNetwork);

                        // =================================================
                        // Gateway
                        // =================================================

                        string gateway =
                            string.Join(
                                ", ",
                                properties
                                    .GatewayAddresses
                                    .Where(
                                        x =>
                                            x.Address.AddressFamily ==
                                            AddressFamily.InterNetwork)
                                    .Select(
                                        x =>
                                            x.Address.ToString()));

                        // =================================================
                        // DNS
                        // =================================================

                        string dns =
                            string.Join(
                                ", ",
                                properties
                                    .DnsAddresses
                                    .Where(
                                        x =>
                                            x.AddressFamily ==
                                            AddressFamily.InterNetwork)
                                    .Select(
                                        x =>
                                            x.ToString()));

                        // =================================================
                        // MAC
                        // =================================================

                        string mac =
                            adapter
                                .GetPhysicalAddress()
                                .ToString();

                        if (mac.Length == 12)
                        {
                            mac = string.Join(
                                "-",
                                Enumerable
                                    .Range(0, 6)
                                    .Select(
                                        i =>
                                            mac.Substring(
                                                i * 2,
                                                2)));
                        }

                        return new NetworkAdapterInfo
                        {
                            AdapterName =
                                adapter.Name,

                            Type =
                                adapter
                                    .NetworkInterfaceType
                                    .ToString(),

                            Status =
                                adapter
                                    .OperationalStatus
                                    .ToString(),

                            IPv4 =
                                ipv4?
                                    .Address
                                    .ToString()
                                ?? "",

                            SubnetMask =
                                ipv4?
                                    .IPv4Mask?
                                    .ToString()
                                ?? "",

                            Gateway =
                                gateway,

                            DNS =
                                dns,

                            MAC =
                                mac
                        };

                    }).ToList();

                NetworkGrid.ItemsSource =
                    _networkData;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể lấy thông tin mạng.\n\n{ex.Message}",
                    "Network Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // =========================================================
        // REFRESH
        // =========================================================

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            _currentSortColumn = "";
            _sortAscending = true;

            LoadNetworkInformation();
        }

        // =========================================================
        // SORT
        // =========================================================

        private void NetworkGrid_Sorting(
            object sender,
            DataGridSortingEventArgs e)
        {
            // Ngăn DataGrid tự xử lý
            e.Handled = true;

            string column =
                e.Column.SortMemberPath;

            if (string.IsNullOrWhiteSpace(column))
            {
                return;
            }

            // Nếu click lại cùng một cột
            // thì đảo chiều sort
            if (_currentSortColumn == column)
            {
                _sortAscending =
                    !_sortAscending;
            }
            else
            {
                _currentSortColumn =
                    column;

                _sortAscending = true;
            }

            // Xóa mũi tên ở các cột khác
            foreach (DataGridColumn c
                     in NetworkGrid.Columns)
            {
                if (c != e.Column)
                {
                    c.SortDirection = null;
                }
            }

            // Hiển thị mũi tên
            e.Column.SortDirection =
                _sortAscending
                    ? System.ComponentModel
                        .ListSortDirection
                        .Ascending
                    : System.ComponentModel
                        .ListSortDirection
                        .Descending;

            // Thực hiện sort
            SortData(column);
        }

        // =========================================================
        // SORT DATA
        // =========================================================

        private void SortData(
            string column)
        {
            IEnumerable<NetworkAdapterInfo> sorted;

            switch (column)
            {
                case "IPv4":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    IpToNumber(
                                        x.IPv4));

                    break;

                case "Gateway":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    IpToNumber(
                                        GetFirstAddress(
                                            x.Gateway)));

                    break;

                case "DNS":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    IpToNumber(
                                        GetFirstAddress(
                                            x.DNS)));

                    break;

                case "SubnetMask":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    IpToNumber(
                                        x.SubnetMask));

                    break;

                case "MAC":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    MacToNumber(
                                        x.MAC));

                    break;

                case "AdapterName":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    x.AdapterName,
                                StringComparer
                                    .OrdinalIgnoreCase);

                    break;

                case "Type":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    x.Type,
                                StringComparer
                                    .OrdinalIgnoreCase);

                    break;

                case "Status":

                    sorted =
                        _networkData
                            .OrderBy(
                                x =>
                                    x.Status,
                                StringComparer
                                    .OrdinalIgnoreCase);

                    break;

                default:

                    sorted =
                        _networkData;

                    break;
            }

            if (!_sortAscending)
            {
                sorted =
                    sorted.Reverse();
            }

            NetworkGrid.ItemsSource =
                sorted.ToList();
        }

        // =========================================================
        // CONVERT IP -> NUMBER
        // =========================================================

        private static ulong IpToNumber(
            string ip)
        {
            if (!IPAddress.TryParse(
                    ip,
                    out IPAddress? address))
            {
                // Đưa giá trị rỗng xuống cuối
                return ulong.MaxValue;
            }

            byte[] bytes =
                address
                    .GetAddressBytes();

            if (bytes.Length != 4)
            {
                return ulong.MaxValue;
            }

            return
                ((ulong)bytes[0] << 24) |
                ((ulong)bytes[1] << 16) |
                ((ulong)bytes[2] << 8) |
                bytes[3];
        }

        // =========================================================
        // GET FIRST ADDRESS
        // =========================================================

        private static string GetFirstAddress(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string[] parts =
                value.Split(
                    ',',
                    StringSplitOptions
                        .RemoveEmptyEntries);

            return parts.Length > 0
                ? parts[0].Trim()
                : "";
        }

        // =========================================================
        // CONVERT MAC -> NUMBER
        // =========================================================

        private static ulong MacToNumber(
            string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
            {
                return ulong.MaxValue;
            }

            string clean =
                mac.Replace("-", "")
                   .Replace(":", "");

            if (ulong.TryParse(
                    clean,
                    System.Globalization
                        .NumberStyles.HexNumber,
                    null,
                    out ulong result))
            {
                return result;
            }

            return ulong.MaxValue;
        }
    }

    // =============================================================
    // NETWORK ADAPTER MODEL
    // =============================================================

    public class NetworkAdapterInfo
    {
        public string AdapterName { get; set; } = "";

        public string Type { get; set; } = "";

        public string Status { get; set; } = "";

        public string IPv4 { get; set; } = "";

        public string SubnetMask { get; set; } = "";

        public string Gateway { get; set; } = "";

        public string DNS { get; set; } = "";

        public string MAC { get; set; } = "";
    }
}