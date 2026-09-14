using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace MyBasicNetworkTools.Native
{
    public class AdapterInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public string MacAddress { get; set; } = "";

        public List<string> IPv4Addresses { get; set; } = new();

        public List<string> IPv6Addresses { get; set; } = new();

        public List<string> Gateways { get; set; } = new();

        public List<string> DnsServers { get; set; } = new();
    }

    public class IpHelper
    {
        private const uint AF_UNSPEC = 0;

        private const uint GAA_FLAG_INCLUDE_PREFIX = 0x0010;

        private const uint ERROR_BUFFER_OVERFLOW = 111;

        private const uint ERROR_SUCCESS = 0;

        [DllImport(
            "iphlpapi.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetAdaptersAddresses(
            uint Family,
            uint Flags,
            IntPtr Reserved,
            IntPtr AdapterAddresses,
            ref uint SizePointer);

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct IP_ADAPTER_ADDRESSES
        {
            public uint Length;
            public uint IfIndex;

            public IntPtr Next;

            public IntPtr AdapterName;

            public IntPtr FirstUnicastAddress;

            public IntPtr FirstAnycastAddress;

            public IntPtr FirstMulticastAddress;

            public IntPtr FirstDnsServerAddress;

            public IntPtr DnsSuffix;

            public IntPtr Description;

            public IntPtr FriendlyName;

            public byte PhysicalAddress0;
            public byte PhysicalAddress1;
            public byte PhysicalAddress2;
            public byte PhysicalAddress3;
            public byte PhysicalAddress4;
            public byte PhysicalAddress5;

            public uint PhysicalAddressLength;

            public uint Flags;

            public uint Mtu;

            public uint IfType;

            public uint OperStatus;

            public uint Ipv6IfIndex;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public uint[] ZoneIndices;

            public IntPtr FirstPrefix;

            public ulong TransmitLinkSpeed;

            public ulong ReceiveLinkSpeed;

            public IntPtr FirstWinsServerAddress;

            public IntPtr FirstGatewayAddress;

            public uint Ipv4Metric;

            public uint Ipv6Metric;

            public ulong Luid;

            public SOCKET_ADDRESS Dhcpv4Server;

            public uint CompartmentId;

            public Guid NetworkGuid;

            public uint ConnectionType;

            public uint TunnelType;

            public SOCKET_ADDRESS Dhcpv6Server;

            public byte Dhcpv6ClientDuid0;
            public byte Dhcpv6ClientDuid1;
            public byte Dhcpv6ClientDuid2;
            public byte Dhcpv6ClientDuid3;
            public byte Dhcpv6ClientDuid4;
            public byte Dhcpv6ClientDuid5;
            public byte Dhcpv6ClientDuid6;
            public byte Dhcpv6ClientDuid7;
            public byte Dhcpv6ClientDuid8;
            public byte Dhcpv6ClientDuid9;
            public byte Dhcpv6ClientDuid10;
            public byte Dhcpv6ClientDuid11;
            public byte Dhcpv6ClientDuid12;
            public byte Dhcpv6ClientDuid13;
            public byte Dhcpv6ClientDuid14;
            public byte Dhcpv6ClientDuid15;
            public byte Dhcpv6ClientDuid16;
            public byte Dhcpv6ClientDuid17;
            public byte Dhcpv6ClientDuid18;
            public byte Dhcpv6ClientDuid19;
            public byte Dhcpv6ClientDuid20;
            public byte Dhcpv6ClientDuid21;
            public byte Dhcpv6ClientDuid22;
            public byte Dhcpv6ClientDuid23;
            public byte Dhcpv6ClientDuid24;
            public byte Dhcpv6ClientDuid25;
            public byte Dhcpv6ClientDuid26;
            public byte Dhcpv6ClientDuid27;
            public byte Dhcpv6ClientDuid28;
            public byte Dhcpv6ClientDuid29;
            public byte Dhcpv6ClientDuid30;
            public byte Dhcpv6ClientDuid31;

            public uint Dhcpv6IAID;

            public IntPtr FirstDnsSuffix;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SOCKET_ADDRESS
        {
            public IntPtr lpSockaddr;
            public int iSockaddrLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADAPTER_UNICAST_ADDRESS
        {
            public uint Length;
            public uint Flags;

            public IntPtr Next;

            public SOCKET_ADDRESS Address;

            public uint PrefixOrigin;
            public uint SuffixOrigin;
            public uint DadState;
            public uint ValidLifetime;
            public uint PreferredLifetime;
            public uint LeaseLifetime;

            public byte OnLinkPrefixLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADAPTER_GATEWAY_ADDRESS
        {
            public uint Length;

            public uint Reserved;

            public IntPtr Next;

            public SOCKET_ADDRESS Address;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_ADAPTER_DNS_SERVER_ADDRESS
        {
            public uint Length;

            public uint Reserved;

            public IntPtr Next;

            public SOCKET_ADDRESS Address;
        }

        public List<AdapterInfo> GetAdapters()
        {
            List<AdapterInfo> result = new();

            uint bufferSize = 15000;

            IntPtr buffer = Marshal.AllocHGlobal(
                (int)bufferSize);

            try
            {
                uint status = GetAdaptersAddresses(
                    AF_UNSPEC,
                    GAA_FLAG_INCLUDE_PREFIX,
                    IntPtr.Zero,
                    buffer,
                    ref bufferSize);

                if (status == ERROR_BUFFER_OVERFLOW)
                {
                    Marshal.FreeHGlobal(buffer);

                    buffer = Marshal.AllocHGlobal(
                        (int)bufferSize);

                    status = GetAdaptersAddresses(
                        AF_UNSPEC,
                        GAA_FLAG_INCLUDE_PREFIX,
                        IntPtr.Zero,
                        buffer,
                        ref bufferSize);
                }

                if (status != ERROR_SUCCESS)
                {
                    throw new InvalidOperationException(
                        $"GetAdaptersAddresses failed. Error code: {status}");
                }

                IntPtr current = buffer;

                while (current != IntPtr.Zero)
                {
                    IP_ADAPTER_ADDRESSES adapter =
                        Marshal.PtrToStructure
                        <IP_ADAPTER_ADDRESSES>(current);

                    AdapterInfo info = new AdapterInfo();

                    info.Name =
                        Marshal.PtrToStringUni(
                            adapter.FriendlyName) ?? "";

                    info.Description =
                        Marshal.PtrToStringUni(
                            adapter.Description) ?? "";

                    info.Status =
                        GetOperationalStatus(
                            adapter.OperStatus);

                    info.MacAddress =
                        GetMacAddress(
                            adapter);

                    ReadUnicastAddresses(
                        adapter.FirstUnicastAddress,
                        info);

                    ReadGatewayAddresses(
                        adapter.FirstGatewayAddress,
                        info);

                    ReadDnsAddresses(
                        adapter.FirstDnsServerAddress,
                        info);

                    result.Add(info);

                    current = adapter.Next;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return result;
        }

        private void ReadUnicastAddresses(
            IntPtr addressPtr,
            AdapterInfo info)
        {
            IntPtr current = addressPtr;

            while (current != IntPtr.Zero)
            {
                IP_ADAPTER_UNICAST_ADDRESS address =
                    Marshal.PtrToStructure
                    <IP_ADAPTER_UNICAST_ADDRESS>(current);

                string ip =
                    GetSocketAddress(
                        address.Address);

                if (!string.IsNullOrEmpty(ip))
                {
                    if (IPAddress.TryParse(
                        ip,
                        out IPAddress? parsed))
                    {
                        if (parsed.AddressFamily ==
                            AddressFamily.InterNetwork)
                        {
                            info.IPv4Addresses.Add(ip);
                        }
                        else if (parsed.AddressFamily ==
                                 AddressFamily.InterNetworkV6)
                        {
                            info.IPv6Addresses.Add(ip);
                        }
                    }
                }

                current = address.Next;
            }
        }

        private void ReadGatewayAddresses(
            IntPtr gatewayPtr,
            AdapterInfo info)
        {
            IntPtr current = gatewayPtr;

            while (current != IntPtr.Zero)
            {
                IP_ADAPTER_GATEWAY_ADDRESS gateway =
                    Marshal.PtrToStructure
                    <IP_ADAPTER_GATEWAY_ADDRESS>(
                        current);

                string address =
                    GetSocketAddress(
                        gateway.Address);

                if (!string.IsNullOrEmpty(address))
                {
                    info.Gateways.Add(address);
                }

                current = gateway.Next;
            }
        }

        private void ReadDnsAddresses(
            IntPtr dnsPtr,
            AdapterInfo info)
        {
            IntPtr current = dnsPtr;

            while (current != IntPtr.Zero)
            {
                IP_ADAPTER_DNS_SERVER_ADDRESS dns =
                    Marshal.PtrToStructure
                    <IP_ADAPTER_DNS_SERVER_ADDRESS>(
                        current);

                string address =
                    GetSocketAddress(
                        dns.Address);

                if (!string.IsNullOrEmpty(address))
                {
                    info.DnsServers.Add(address);
                }

                current = dns.Next;
            }
        }

        private string GetSocketAddress(
     SOCKET_ADDRESS socketAddress)
        {
            if (socketAddress.lpSockaddr == IntPtr.Zero)
                return "";

            try
            {
                int family =
                    Marshal.ReadInt16(
                        socketAddress.lpSockaddr);

                if (family == (int)AddressFamily.InterNetwork)
                {
                    byte[] bytes = new byte[4];

                    Marshal.Copy(
                        IntPtr.Add(
                            socketAddress.lpSockaddr,
                            4),
                        bytes,
                        0,
                        4);

                    return new IPAddress(bytes).ToString();
                }

                if (family == (int)AddressFamily.InterNetworkV6)
                {
                    byte[] bytes = new byte[16];

                    Marshal.Copy(
                        IntPtr.Add(
                            socketAddress.lpSockaddr,
                            8),
                        bytes,
                        0,
                        16);

                    return new IPAddress(bytes).ToString();
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private string GetMacAddress(
            IP_ADAPTER_ADDRESSES adapter)
        {
            if (adapter.PhysicalAddressLength == 0)
                return "";

            byte[] mac =
            {
                adapter.PhysicalAddress0,
                adapter.PhysicalAddress1,
                adapter.PhysicalAddress2,
                adapter.PhysicalAddress3,
                adapter.PhysicalAddress4,
                adapter.PhysicalAddress5
            };

            return string.Join(
                "-",
                mac.Take(
                    (int)adapter.PhysicalAddressLength)
                   .Select(x => x.ToString("X2")));
        }

        private string GetOperationalStatus(
            uint status)
        {
            return status switch
            {
                1 => "Up",
                2 => "Down",
                3 => "Testing",
                4 => "Unknown",
                5 => "Dormant",
                6 => "NotPresent",
                7 => "LowerLayerDown",
                _ => "Unknown"
            };
        }
    }
}