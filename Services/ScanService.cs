using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Native;

namespace MyBasicNetworkTools.Services
{
    public class ScanService
    {
        private readonly IcmpHelper _icmpHelper;

        public ScanService()
        {
            _icmpHelper = new IcmpHelper();
        }

        // ============================================================
        // GENERATE IP LIST FROM CIDR
        // ============================================================

        public List<IPAddress> GenerateIpsFromCidr(string cidr)
        {
            if (string.IsNullOrWhiteSpace(cidr))
                throw new ArgumentException(
                    "CIDR không được để trống.");

            string[] parts =
                cidr.Trim().Split('/');

            if (parts.Length != 2)
            {
                throw new ArgumentException(
                    "CIDR phải có dạng 192.168.1.0/24.");
            }

            if (!IPAddress.TryParse(
                    parts[0],
                    out IPAddress? networkIp))
            {
                throw new ArgumentException(
                    "Network IP không hợp lệ.");
            }

            if (!int.TryParse(
                    parts[1],
                    out int prefixLength))
            {
                throw new ArgumentException(
                    "Prefix CIDR không hợp lệ.");
            }

            if (networkIp.AddressFamily !=
                AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Chỉ hỗ trợ IPv4.");
            }

            if (prefixLength < 0 ||
                prefixLength > 32)
            {
                throw new ArgumentException(
                    "CIDR phải nằm trong /0 đến /32.");
            }

            uint ip =
                IpToUInt32(networkIp);

            uint mask;

            if (prefixLength == 0)
                mask = 0;
            else
                mask =
                    uint.MaxValue <<
                    (32 - prefixLength);

            uint network =
                ip & mask;

            uint broadcast =
                network | ~mask;

            uint first;
            uint last;

            // /31 và /32 không loại bỏ network/broadcast
            if (prefixLength >= 31)
            {
                first = network;
                last = broadcast;
            }
            else
            {
                first = network + 1;
                last = broadcast - 1;
            }

            List<IPAddress> result =
                new();

            // Giới hạn để tránh vô tình quét Internet-sized subnet
            ulong count =
                (ulong)last -
                first +
                1UL;

            if (count > 65536)
            {
                throw new ArgumentException(
                    "Dải mạng quá lớn. Vui lòng sử dụng subnet nhỏ hơn /16.");
            }

            for (uint current = first;
                 current <= last;
                 current++)
            {
                result.Add(
                    UInt32ToIp(current));

                if (current == uint.MaxValue)
                    break;
            }

            return result;
        }

        // ============================================================
        // GENERATE IP RANGE
        // ============================================================

        public List<IPAddress> GenerateIpsFromRange(
            string startIp,
            string endIp)
        {
            if (!IPAddress.TryParse(
                    startIp.Trim(),
                    out IPAddress? start))
            {
                throw new ArgumentException(
                    "Start IP không hợp lệ.");
            }

            if (!IPAddress.TryParse(
                    endIp.Trim(),
                    out IPAddress? end))
            {
                throw new ArgumentException(
                    "End IP không hợp lệ.");
            }

            if (start.AddressFamily !=
                    AddressFamily.InterNetwork ||
                end.AddressFamily !=
                    AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Chỉ hỗ trợ IPv4.");
            }

            uint startValue =
                IpToUInt32(start);

            uint endValue =
                IpToUInt32(end);

            if (startValue > endValue)
            {
                throw new ArgumentException(
                    "Start IP phải nhỏ hơn hoặc bằng End IP.");
            }

            ulong count =
                (ulong)endValue -
                startValue +
                1UL;

            if (count > 65536)
            {
                throw new ArgumentException(
                    "Dải IP quá lớn. Tối đa 65536 địa chỉ.");
            }

            List<IPAddress> result =
                new();

            for (uint current = startValue;
                 current <= endValue;
                 current++)
            {
                result.Add(
                    UInt32ToIp(current));

                if (current == uint.MaxValue)
                    break;
            }

            return result;
        }

        // ============================================================
        // SCAN
        // ============================================================

        public async Task<List<ScanResult>> ScanAsync(
            IEnumerable<IPAddress> addresses,
            string scanMode,
            int timeout,
            int concurrency,
            IEnumerable<int> ports,
            IProgress<ScanResult>? progress = null,
            IProgress<int>? progressCount = null,
            CancellationToken cancellationToken = default)
        {
            if (timeout < 100)
                throw new ArgumentException(
                    "Timeout tối thiểu là 100 ms.");

            if (timeout > 60000)
                throw new ArgumentException(
                    "Timeout tối đa là 60000 ms.");

            if (concurrency < 1)
                concurrency = 1;

            if (concurrency > 200)
                concurrency = 200;

            bool fullScan =
                string.Equals(
                    scanMode,
                    "Full Scan (Ping + Ports)",
                    StringComparison.OrdinalIgnoreCase);

            int[] portList =
                ports?
                    .Where(p => p >= 1 && p <= 65535)
                    .Distinct()
                    .ToArray()
                ?? Array.Empty<int>();

            List<IPAddress> ipList =
                addresses
                    .Distinct()
                    .ToList();

            List<ScanResult> results =
                new();

            using SemaphoreSlim semaphore =
                new(concurrency);

            List<Task> tasks =
                new();

            int completed = 0;

            object lockObject =
                new();

            foreach (IPAddress address in ipList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await semaphore.WaitAsync(
                    cancellationToken);

                IPAddress currentIp =
                    address;

                Task task =
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                cancellationToken
                                    .ThrowIfCancellationRequested();

                                ScanResult result =
                                    await ScanHostAsync(
                                        currentIp,
                                        timeout,
                                        fullScan,
                                        portList,
                                        cancellationToken);

                                lock (lockObject)
                                {
                                    results.Add(result);
                                }

                                progress?.Report(result);

                                int value =
                                    Interlocked.Increment(
                                        ref completed);

                                progressCount?.Report(value);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        },
                        cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            return results
                .OrderBy(
                    x => IpToUInt32(
                        IPAddress.Parse(x.IPAddress)))
                .ToList();
        }

        // ============================================================
        // SCAN ONE HOST
        // ============================================================

        private async Task<ScanResult> ScanHostAsync(
            IPAddress ip,
            int timeout,
            bool fullScan,
            int[] ports,
            CancellationToken cancellationToken)
        {
            ScanResult result =
                new()
                {
                    IPAddress = ip.ToString(),
                    Status = "Dead",
                    Hostname = "*",
                    RTT = -1,
                    MACAddress = "-",
                    OpenPorts = "-",
                    IsAlive = false,
                    OpenPortCount = 0
                };

            cancellationToken.ThrowIfCancellationRequested();

            // ========================================================
            // ICMP
            // ========================================================

            IcmpPingResult pingResult;

            try
            {
                pingResult =
                    await Task.Run(
                        () =>
                            _icmpHelper.Ping(
                                ip.ToString(),
                                32,
                                timeout,
                                128,
                                false),
                        cancellationToken);
            }
            catch
            {
                pingResult =
                    new IcmpPingResult
                    {
                        Success = false,
                        IPAddress = ip.ToString(),
                        RTT = -1,
                        IcmpStatus = 11010
                    };
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool icmpAlive =
                pingResult.Success;

            if (icmpAlive)
            {
                result.IsAlive = true;

                result.Status = "Alive";

                result.RTT =
                    pingResult.RTT;

                if (!string.IsNullOrWhiteSpace(
                        pingResult.IPAddress))
                {
                    result.IPAddress =
                        pingResult.IPAddress;
                }
            }

            // ========================================================
            // ARP
            // ========================================================

            string mac =
                TryGetMacAddress(ip);

            if (!string.IsNullOrWhiteSpace(mac))
            {
                result.MACAddress =
                    mac;

                // ARP có thể phát hiện host
                // dù ICMP bị firewall chặn.
                result.IsAlive = true;

                result.Status = "Alive";

                if (result.RTT < 0)
                {
                    result.RTT = 0;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ========================================================
            // HOSTNAME
            // ========================================================

            if (result.IsAlive)
            {
                result.Hostname =
                    await ResolveHostnameAsync(
                        ip,
                        cancellationToken);
            }

            // ========================================================
            // PORT SCAN
            // ========================================================

            if (fullScan &&
                result.IsAlive &&
                ports.Length > 0)
            {
                List<int> openPorts =
                    await ScanPortsAsync(
                        ip,
                        ports,
                        timeout,
                        cancellationToken);

                result.OpenPortCount =
                    openPorts.Count;

                result.OpenPorts =
                    openPorts.Count > 0
                        ? string.Join(
                            ", ",
                            openPorts)
                        : "-";
            }

            return result;
        }

        // ============================================================
        // ARP / MAC
        // ============================================================

        private static string TryGetMacAddress(
            IPAddress ip)
        {
            try
            {
                byte[] mac =
                    new byte[6];

                uint macLength =
                    (uint)mac.Length;

                uint address =
                    IpToUInt32(ip);

                int result =
                    NativeMethods.SendARP(
                        address,
                        0,
                        mac,
                        ref macLength);

                if (result != 0 ||
                    macLength < 6)
                {
                    return "-";
                }

                return string.Join(
                    "-",
                    mac
                        .Take(6)
                        .Select(
                            b => b.ToString("X2")));
            }
            catch
            {
                return "-";
            }
        }

        // ============================================================
        // REVERSE DNS
        // ============================================================

        private static async Task<string>
            ResolveHostnameAsync(
                IPAddress ip,
                CancellationToken cancellationToken)
        {
            try
            {
                using CancellationTokenSource timeoutCts =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken);

                timeoutCts.CancelAfter(1200);

                IPHostEntry entry =
                    await Dns.GetHostEntryAsync(
                        ip.ToString(),
                        timeoutCts.Token);

                if (!string.IsNullOrWhiteSpace(
                        entry.HostName))
                {
                    return entry.HostName;
                }
            }
            catch
            {
                // Không có reverse DNS
            }

            return "*";
        }

        // ============================================================
        // TCP PORT SCAN
        // ============================================================

        private static async Task<List<int>>
            ScanPortsAsync(
                IPAddress ip,
                IEnumerable<int> ports,
                int timeout,
                CancellationToken cancellationToken)
        {
            List<int> openPorts =
                new();

            // Không tạo quá nhiều socket cùng lúc
            using SemaphoreSlim semaphore =
                new(20);

            List<Task> tasks =
                new();

            object lockObject =
                new();

            foreach (int port in ports)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                int currentPort =
                    port;

                await semaphore.WaitAsync(
                    cancellationToken);

                Task task =
                    Task.Run(
                        async () =>
                        {
                            try
                            {
                                bool open =
                                    await IsTcpPortOpenAsync(
                                        ip,
                                        currentPort,
                                        timeout,
                                        cancellationToken);

                                if (open)
                                {
                                    lock (lockObject)
                                    {
                                        openPorts.Add(
                                            currentPort);
                                    }
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        },
                        cancellationToken);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            openPorts.Sort();

            return openPorts;
        }

        // ============================================================
        // TCP CONNECT
        // ============================================================

        private static async Task<bool>
            IsTcpPortOpenAsync(
                IPAddress ip,
                int port,
                int timeout,
                CancellationToken cancellationToken)
        {
            using TcpClient client =
                new();

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            timeoutCts.CancelAfter(
                timeout);

            try
            {
                await client.ConnectAsync(
                    ip,
                    port,
                    timeoutCts.Token);

                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        // IP CONVERSION
        // ============================================================

        private static uint IpToUInt32(
            IPAddress ip)
        {
            byte[] bytes =
                ip.GetAddressBytes();

            if (bytes.Length != 4)
                throw new ArgumentException(
                    "Chỉ hỗ trợ IPv4.");

            return
                ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];
        }

        private static IPAddress UInt32ToIp(
            uint value)
        {
            return new IPAddress(
                new byte[]
                {
                    (byte)(value >> 24),
                    (byte)(value >> 16),
                    (byte)(value >> 8),
                    (byte)value
                });
        }
    }
}