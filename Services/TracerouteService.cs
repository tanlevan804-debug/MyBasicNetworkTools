using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using MyBasicNetworkTools.Models;
using MyBasicNetworkTools.Native;

namespace MyBasicNetworkTools.Services
{
    public class TracerouteService
    {
        private readonly IcmpHelper _icmpHelper;

        public TracerouteService()
        {
            _icmpHelper = new IcmpHelper();
        }

        // =====================================================
        // TRACEROUTE
        // =====================================================

        public async Task TraceAsync(
            string host,
            int maxHops,
            int timeout,
            int probesPerHop,
            bool resolveHostnames,
            IProgress<TracerouteResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // =================================================
            // 1. VALIDATE INPUT
            // =================================================

            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException(
                    "Target Host không được để trống.");
            }

            if (maxHops < 1 || maxHops > 255)
            {
                throw new ArgumentException(
                    "Max Hops phải nằm trong khoảng 1 - 255.");
            }

            if (timeout < 100 || timeout > 60000)
            {
                throw new ArgumentException(
                    "Timeout phải nằm trong khoảng 100 - 60000 ms.");
            }

            if (probesPerHop < 1 || probesPerHop > 3)
            {
                throw new ArgumentException(
                    "Probes per Hop chỉ hỗ trợ 1 - 3.");
            }

            cancellationToken.ThrowIfCancellationRequested();


            // =================================================
            // 2. RESOLVE TARGET HOST -> IPv4
            // =================================================

            string targetIp =
                await ResolveIPv4Async(
                    host,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();


            // =================================================
            // 3. TRACE TỪNG HOP
            // =================================================

            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                cancellationToken.ThrowIfCancellationRequested();


                // =================================================
                // TẠO KẾT QUẢ CHO HOP
                // =================================================

                TracerouteResult hop =
                    new TracerouteResult
                    {
                        Hop = ttl,

                        IPAddress = "*",

                        Hostname = "*",

                        RTT1 = -1,

                        RTT2 = -1,

                        RTT3 = -1,

                        AverageRTT = -1,

                        Status = "Timed Out"
                    };


                List<long> rtts = new();


                bool destinationReached = false;

                bool networkUnreachable = false;

                bool hostUnreachable = false;

                bool ttlExpired = false;


                // =================================================
                // 4. GỬI PROBE
                // =================================================

                for (int probe = 1;
                     probe <= probesPerHop;
                     probe++)
                {
                    cancellationToken.ThrowIfCancellationRequested();


                    IcmpPingResult result;


                    try
                    {
                        result =
                            await Task.Run(
                                () =>
                                    _icmpHelper.Ping(
                                        targetIp,
                                        32,
                                        timeout,
                                        ttl,
                                        false),
                                cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Probe lỗi -> bỏ qua probe này
                        continue;
                    }


                    cancellationToken.ThrowIfCancellationRequested();


                    // =================================================
                    // 5. PHÂN TÍCH STATUS
                    // =================================================

                    string status =
                        GetTracerouteStatus(
                            result,
                            targetIp);


                    switch (status)
                    {
                        case "Destination Reached":

                            destinationReached = true;

                            break;


                        case "Network Unreachable":

                            networkUnreachable = true;

                            break;


                        case "Host Unreachable":

                            hostUnreachable = true;

                            break;


                        case "TTL Expired":

                            ttlExpired = true;

                            break;
                    }


                    // =================================================
                    // 6. XÁC ĐỊNH RESPONSE HỢP LỆ
                    // =================================================
                    //
                    // Success:
                    //      Destination phản hồi
                    //
                    // 11013:
                    //      TTL expired in transit
                    //
                    // 11014:
                    //      TTL expired during reassembly
                    //
                    // Đây đều là response hợp lệ đối với traceroute.
                    // =================================================

                    bool validResponse =
                        result.Success ||
                        result.IcmpStatus == 11013 ||
                        result.IcmpStatus == 11014;


                    // =================================================
                    // 7. LẤY IP ROUTER / DESTINATION
                    // =================================================
                    //
                    // Chỉ lấy IP nếu thật sự nhận được ICMP response.
                    //
                    // Timeout:
                    //      IP = *
                    //
                    // TTL Expired:
                    //      IP = IP của router trung gian
                    //
                    // Destination:
                    //      IP = IP đích
                    // =================================================

                    if (validResponse &&
                        !string.IsNullOrWhiteSpace(result.IPAddress) &&
                        result.IPAddress != "*")
                    {
                        hop.IPAddress =
                            result.IPAddress;
                    }


                    // =================================================
                    // 8. LẤY RTT
                    // =================================================

                    if (validResponse &&
                        result.RTT >= 0)
                    {
                        rtts.Add(result.RTT);


                        if (probe == 1)
                        {
                            hop.RTT1 =
                                result.RTT;
                        }
                        else if (probe == 2)
                        {
                            hop.RTT2 =
                                result.RTT;
                        }
                        else if (probe == 3)
                        {
                            hop.RTT3 =
                                result.RTT;
                        }
                    }
                }


                // =================================================
                // 9. TÍNH RTT AVERAGE
                // =================================================

                if (rtts.Count > 0)
                {
                    hop.AverageRTT =
                        Math.Round(
                            rtts.Average(),
                            1);
                }


                // =================================================
                // 10. XÁC ĐỊNH STATUS CUỐI CÙNG
                // =================================================

                if (destinationReached)
                {
                    hop.Status =
                        "Destination Reached";
                }
                else if (networkUnreachable)
                {
                    hop.Status =
                        "Network Unreachable";
                }
                else if (hostUnreachable)
                {
                    hop.Status =
                        "Host Unreachable";
                }
                else if (ttlExpired)
                {
                    hop.Status =
                        "TTL Expired";
                }
                else if (rtts.Count > 0)
                {
                    hop.Status =
                        "Success";
                }
                else
                {
                    hop.Status =
                        "Timed Out";
                }


                // =================================================
                // 11. REVERSE DNS
                // =================================================

                if (resolveHostnames &&
                    hop.IPAddress != "*" &&
                    !string.IsNullOrWhiteSpace(
                        hop.IPAddress))
                {
                    hop.Hostname =
                        await ResolveHostnameAsync(
                            hop.IPAddress,
                            cancellationToken);
                }


                // =================================================
                // 12. GỬI KẾT QUẢ VỀ UI
                // =================================================

                progress?.Report(hop);


                // =================================================
                // 13. ĐÃ TỚI DESTINATION
                // =================================================

                if (destinationReached)
                {
                    break;
                }


                // =================================================
                // 14. DESTINATION UNREACHABLE
                // =================================================

                if (networkUnreachable ||
                    hostUnreachable)
                {
                    break;
                }
            }
        }


        // =====================================================
        // RESOLVE DOMAIN -> IPv4
        // =====================================================

        private static async Task<string> ResolveIPv4Async(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();


            // -------------------------------------------------
            // Nếu nhập IPv4 trực tiếp
            // -------------------------------------------------

            if (IPAddress.TryParse(
                    host,
                    out IPAddress? address))
            {
                if (address.AddressFamily ==
                    AddressFamily.InterNetwork)
                {
                    return address.ToString();
                }

                throw new Exception(
                    "Chỉ hỗ trợ địa chỉ IPv4.");
            }


            // -------------------------------------------------
            // Nếu nhập domain
            // -------------------------------------------------

            try
            {
                IPAddress[] addresses =
                    await Dns.GetHostAddressesAsync(
                        host);

                cancellationToken.ThrowIfCancellationRequested();


                foreach (IPAddress ip in addresses)
                {
                    if (ip.AddressFamily ==
                        AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Không thể phân giải '{host}': {ex.Message}");
            }


            throw new Exception(
                $"Không tìm thấy IPv4 cho '{host}'.");
        }


        // =====================================================
        // REVERSE DNS
        // =====================================================

        private static async Task<string> ResolveHostnameAsync(
            string ip,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();


                // -------------------------------------------------
                // Gửi Reverse DNS
                // -------------------------------------------------

                Task<IPHostEntry> dnsTask =
                    Dns.GetHostEntryAsync(ip);


                // -------------------------------------------------
                // Timeout 1.5 giây
                // -------------------------------------------------

                Task timeoutTask =
                    Task.Delay(
                        1500,
                        cancellationToken);


                Task completedTask =
                    await Task.WhenAny(
                        dnsTask,
                        timeoutTask);


                cancellationToken.ThrowIfCancellationRequested();


                // -------------------------------------------------
                // DNS quá lâu
                // -------------------------------------------------

                if (completedTask != dnsTask)
                {
                    return "*";
                }


                IPHostEntry entry =
                    await dnsTask;


                cancellationToken.ThrowIfCancellationRequested();


                string hostname =
                    entry.HostName?.Trim() ?? "";


                // -------------------------------------------------
                // Không có hostname
                // -------------------------------------------------

                if (string.IsNullOrWhiteSpace(
                        hostname))
                {
                    return "*";
                }


                // -------------------------------------------------
                // DNS trả lại chính IP
                // -------------------------------------------------

                if (string.Equals(
                        hostname,
                        ip,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "*";
                }


                return hostname;
            }
            catch (OperationCanceledException)
            {
                // STOP phải được truyền ra ngoài
                throw;
            }
            catch
            {
                // Không có PTR hoặc DNS lỗi
                return "*";
            }
        }


        // =====================================================
        // PHÂN TÍCH ICMP STATUS
        // =====================================================

        private static string GetTracerouteStatus(
            IcmpPingResult result,
            string targetIp)
        {
            // =================================================
            // DESTINATION REACHED
            // =================================================

            if (result.Success)
            {
                if (string.Equals(
                        result.IPAddress,
                        targetIp,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "Destination Reached";
                }

                return "Success";
            }


            // =================================================
            // ICMP STATUS
            // =================================================

            switch (result.IcmpStatus)
            {
                // ---------------------------------------------
                // TTL EXPIRED
                // ---------------------------------------------

                case 11013:
                case 11014:

                    return "TTL Expired";


                // ---------------------------------------------
                // NETWORK UNREACHABLE
                // ---------------------------------------------

                case 11002:

                    return "Network Unreachable";


                // ---------------------------------------------
                // HOST UNREACHABLE
                // ---------------------------------------------

                case 11003:

                    return "Host Unreachable";


                // ---------------------------------------------
                // TIMEOUT
                // ---------------------------------------------

                case 11010:

                    return "Timed Out";


                // ---------------------------------------------
                // BAD DESTINATION
                // ---------------------------------------------

                case 11018:

                    return "Bad Destination";


                // ---------------------------------------------
                // PARAMETER PROBLEM
                // ---------------------------------------------

                case 11015:

                    return "Parameter Problem";


                // ---------------------------------------------
                // GENERAL FAILURE
                // ---------------------------------------------

                case 11050:

                    return "General Failure";


                // ---------------------------------------------
                // UNKNOWN
                // ---------------------------------------------

                default:

                    return "Timed Out";
            }
        }
    }
}