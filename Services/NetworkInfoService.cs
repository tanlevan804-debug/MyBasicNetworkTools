using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MyBasicNetworkTools.Services
{
    public class NetworkInfo
    {
        public string AdapterName { get; set; } = "";
        public string Status { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string IPv4Address { get; set; } = "";
        public string SubnetMask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string DnsServers { get; set; } = "";
    }

    public class NetworkInfoService
    {
        public List<NetworkInfo> GetNetworkInformation()
        {
            List<NetworkInfo> results = new();

            foreach (NetworkInterface adapter in
                     NetworkInterface.GetAllNetworkInterfaces())
            {
                // Chỉ lấy card mạng đang hoạt động
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                IPInterfaceProperties properties =
                    adapter.GetIPProperties();

                // Tìm địa chỉ IPv4
                var ipv4 = properties.UnicastAddresses
                    .FirstOrDefault(x =>
                        x.Address.AddressFamily ==
                        AddressFamily.InterNetwork);

                if (ipv4 == null)
                    continue;

                // Gateway IPv4
                string gateway = string.Join(
                    ", ",
                    properties.GatewayAddresses
                        .Where(x =>
                            x.Address.AddressFamily ==
                            AddressFamily.InterNetwork)
                        .Select(x =>
                            x.Address.ToString())
                );

                // DNS IPv4
                string dnsServers = string.Join(
                    ", ",
                    properties.DnsAddresses
                        .Where(x =>
                            x.AddressFamily ==
                            AddressFamily.InterNetwork)
                        .Select(x =>
                            x.ToString())
                );

                // MAC Address
                string mac =
                    adapter.GetPhysicalAddress().ToString();

                // Format MAC:
                // AABBCCDDEEFF
                // thành:
                // AA-BB-CC-DD-EE-FF
                if (mac.Length == 12)
                {
                    mac = string.Join(
                        "-",
                        Enumerable.Range(0, 6)
                            .Select(i =>
                                mac.Substring(i * 2, 2))
                    );
                }

                results.Add(new NetworkInfo
                {
                    AdapterName = adapter.Name,

                    Status =
                        adapter.OperationalStatus.ToString(),

                    MacAddress = mac,

                    IPv4Address =
                        ipv4.Address.ToString(),

                    SubnetMask =
                        ipv4.IPv4Mask?.ToString() ?? "",

                    Gateway = gateway,

                    DnsServers = dnsServers
                });
            }

            return results;
        }
    }
}