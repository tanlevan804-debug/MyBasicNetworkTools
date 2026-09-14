using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MyBasicNetworkTools.Native
{
    public class IcmpHelper
    {
        // =========================================================
        // WINDOWS IP HELPER API
        // =========================================================

        [DllImport(
            "iphlpapi.dll",
            SetLastError = true)]
        private static extern IntPtr IcmpCreateFile();


        [DllImport(
            "iphlpapi.dll",
            SetLastError = true)]
        private static extern bool IcmpCloseHandle(
            IntPtr IcmpHandle);


        [DllImport(
            "iphlpapi.dll",
            SetLastError = true)]
        private static extern uint IcmpSendEcho(
            IntPtr IcmpHandle,
            uint DestAddress,
            IntPtr RequestData,
            ushort RequestSize,
            ref IP_OPTION_INFORMATION RequestOptInfo,
            IntPtr ReplyBuffer,
            uint ReplySize,
            uint Timeout);


        // =========================================================
        // ICMP STATUS CODES
        // =========================================================

        private const uint IP_SUCCESS = 0;

        private const uint IP_DEST_NET_UNREACHABLE = 11002;

        private const uint IP_DEST_HOST_UNREACHABLE = 11003;

        private const uint IP_REQ_TIMED_OUT = 11010;

        private const uint IP_TTL_EXPIRED_TRANSIT = 11013;

        private const uint IP_TTL_EXPIRED_REASSEM = 11014;

        private const uint IP_PARAM_PROBLEM = 11015;

        private const uint IP_SOURCE_QUENCH = 11016;

        private const uint IP_BAD_DESTINATION = 11018;

        private const uint IP_GENERAL_FAILURE = 11050;


        // =========================================================
        // IP FLAGS
        // =========================================================

        // Don't Fragment
        private const byte IP_FLAG_DF = 0x02;


        // =========================================================
        // IP_OPTION_INFORMATION
        // =========================================================

        [StructLayout(LayoutKind.Sequential)]
        private struct IP_OPTION_INFORMATION
        {
            public byte Ttl;

            public byte Tos;

            public byte Flags;

            public byte OptionsSize;

            public IntPtr OptionsData;
        }


        // =========================================================
        // ICMP_ECHO_REPLY
        // =========================================================

        [StructLayout(LayoutKind.Sequential)]
        private struct ICMP_ECHO_REPLY
        {
            public uint Address;

            public uint Status;

            public uint RoundTripTime;

            public ushort DataSize;

            public ushort Reserved;

            public IntPtr Data;

            public IP_OPTION_INFORMATION Options;
        }


        // =========================================================
        // MAIN PING FUNCTION
        // =========================================================

        public IcmpPingResult Ping(
            string host,
            int bufferSize = 32,
            int timeout = 3000,
            int ttl = 128,
            bool dontFragment = false)
        {
            // -----------------------------------------------------
            // 1. VALIDATE INPUT
            // -----------------------------------------------------

            if (string.IsNullOrWhiteSpace(host))
            {
                return new IcmpPingResult
                {
                    Success = false,
                    Status = "Invalid Host"
                };
            }


            // Buffer Size

            if (bufferSize < 1)
            {
                bufferSize = 32;
            }

            if (bufferSize > 65000)
            {
                bufferSize = 65000;
            }


            // Timeout

            if (timeout < 1)
            {
                timeout = 3000;
            }


            // TTL

            if (ttl < 1)
            {
                ttl = 128;
            }

            if (ttl > 255)
            {
                ttl = 255;
            }


            // -----------------------------------------------------
            // 2. RESOLVE HOST
            // -----------------------------------------------------

            IPAddress? ipAddress =
                ResolveIPv4(host);

            if (ipAddress == null)
            {
                return new IcmpPingResult
                {
                    Success = false,
                    Status = "Unable to resolve host"
                };
            }


            // -----------------------------------------------------
            // 3. CONVERT IP ADDRESS
            // -----------------------------------------------------

            byte[] ipBytes =
                ipAddress.GetAddressBytes();

            uint destinationAddress =
                BitConverter.ToUInt32(
                    ipBytes,
                    0);


            // -----------------------------------------------------
            // 4. CREATE ICMP HANDLE
            // -----------------------------------------------------

            IntPtr icmpHandle =
                IcmpCreateFile();

            if (icmpHandle == IntPtr.Zero ||
                icmpHandle == new IntPtr(-1))
            {
                int error =
                    Marshal.GetLastWin32Error();

                return new IcmpPingResult
                {
                    Success = false,

                    IPAddress =
                        ipAddress.ToString(),

                    Status =
                        $"Unable to create ICMP handle. Error: {error}",

                    IcmpStatus =
                        (uint)error
                };
            }


            IntPtr requestData =
                IntPtr.Zero;

            IntPtr replyBuffer =
                IntPtr.Zero;


            try
            {
                // =================================================
                // 5. CREATE PAYLOAD
                // =================================================

                byte[] payload =
                    new byte[bufferSize];


                for (int i = 0;
                     i < payload.Length;
                     i++)
                {
                    payload[i] =
                        (byte)(i % 256);
                }


                // =================================================
                // 6. ALLOCATE REQUEST BUFFER
                // =================================================

                requestData =
                    Marshal.AllocHGlobal(
                        payload.Length);


                Marshal.Copy(
                    payload,
                    0,
                    requestData,
                    payload.Length);


                // =================================================
                // 7. CREATE ICMP OPTIONS
                // =================================================

                IP_OPTION_INFORMATION options =
                    new IP_OPTION_INFORMATION
                    {
                        Ttl = (byte)ttl,

                        Tos = 0,

                        Flags =
                            dontFragment
                                ? IP_FLAG_DF
                                : (byte)0,

                        OptionsSize = 0,

                        OptionsData =
                            IntPtr.Zero
                    };


                // =================================================
                // 8. CREATE REPLY BUFFER
                // =================================================

                int replySize =
                    Marshal.SizeOf<ICMP_ECHO_REPLY>()
                    + bufferSize
                    + 256;


                replyBuffer =
                    Marshal.AllocHGlobal(
                        replySize);


                // =================================================
                // 9. SEND ICMP ECHO REQUEST
                // =================================================

                uint replyCount =
                    IcmpSendEcho(
                        icmpHandle,

                        destinationAddress,

                        requestData,

                        (ushort)bufferSize,

                        ref options,

                        replyBuffer,

                        (uint)replySize,

                        (uint)timeout);


                // =================================================
                // 10. NO REPLY
                // =================================================

                if (replyCount == 0)
                {
                    int error =
                        Marshal.GetLastWin32Error();


                    return new IcmpPingResult
                    {
                        Success = false,

                        IPAddress =
                            ipAddress.ToString(),

                        Status =
                            ConvertWin32Error(error),

                        IcmpStatus =
                            (uint)error
                    };
                }


                // =================================================
                // 11. READ ICMP REPLY
                // =================================================

                ICMP_ECHO_REPLY reply =
                    Marshal.PtrToStructure<ICMP_ECHO_REPLY>(
                        replyBuffer);


                // =================================================
                // 12. CONVERT REPLY IP
                // =================================================

                string replyIp =
                    new IPAddress(
                        reply.Address)
                    .ToString();


                // =================================================
                // 13. RETURN RESULT
                // =================================================

                return new IcmpPingResult
                {
                    Success =
                        reply.Status ==
                        IP_SUCCESS,

                    IPAddress =
                        replyIp,

                    Bytes =
                        reply.DataSize,

                    RTT =
                        reply.RoundTripTime,

                    TTL =
                        reply.Options.Ttl,

                    Status =
                        ConvertIcmpStatus(
                            reply.Status),

                    IcmpStatus =
                        reply.Status
                };
            }
            finally
            {
                // =================================================
                // 14. FREE MEMORY
                // =================================================

                if (requestData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(
                        requestData);
                }


                if (replyBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(
                        replyBuffer);
                }


                // =================================================
                // 15. CLOSE ICMP HANDLE
                // =================================================

                IcmpCloseHandle(
                    icmpHandle);
            }
        }


        // =========================================================
        // RESOLVE DOMAIN → IPv4
        // =========================================================

        private static IPAddress? ResolveIPv4(
            string host)
        {
            // -----------------------------------------------------
            // Nếu người dùng nhập IPv4
            // -----------------------------------------------------

            if (IPAddress.TryParse(
                    host,
                    out IPAddress? address))
            {
                if (address.AddressFamily ==
                    AddressFamily.InterNetwork)
                {
                    return address;
                }

                return null;
            }


            // -----------------------------------------------------
            // Nếu người dùng nhập Domain
            // -----------------------------------------------------

            try
            {
                IPAddress[] addresses =
                    Dns.GetHostAddresses(host);


                return addresses.FirstOrDefault(
                    address =>
                        address.AddressFamily ==
                        AddressFamily.InterNetwork);
            }
            catch
            {
                return null;
            }
        }


        // =========================================================
        // CONVERT ICMP STATUS
        // =========================================================

        private static string ConvertIcmpStatus(
            uint status)
        {
            return status switch
            {
                IP_SUCCESS =>
                    "Success",

                IP_DEST_NET_UNREACHABLE =>
                    "Destination Network Unreachable",

                IP_DEST_HOST_UNREACHABLE =>
                    "Destination Host Unreachable",

                IP_REQ_TIMED_OUT =>
                    "Request timed out",

                IP_TTL_EXPIRED_TRANSIT =>
                    "TTL expired in transit",

                IP_TTL_EXPIRED_REASSEM =>
                    "TTL expired during reassembly",

                IP_PARAM_PROBLEM =>
                    "Parameter problem",

                IP_SOURCE_QUENCH =>
                    "Source quench",

                IP_BAD_DESTINATION =>
                    "Bad destination",

                IP_GENERAL_FAILURE =>
                    "General failure",

                _ =>
                    $"ICMP Error ({status})"
            };
        }


        // =========================================================
        // CONVERT WINDOWS ERROR
        // =========================================================

        private static string ConvertWin32Error(
            int error)
        {
            return error switch
            {
                11002 =>
                    "Destination Network Unreachable",

                11003 =>
                    "Destination Host Unreachable",

                11010 =>
                    "Request timed out",

                11013 =>
                    "TTL expired in transit",

                11014 =>
                    "TTL expired during reassembly",

                11015 =>
                    "Parameter problem",

                11016 =>
                    "Source quench",

                11018 =>
                    "Bad destination",

                11050 =>
                    "General failure",

                _ =>
                    $"ICMP Error ({error})"
            };
        }
    }


    // =============================================================
    // PING RESULT
    // =============================================================

    public class IcmpPingResult
    {
        // Ping thành công hay không
        public bool Success { get; set; }


        // IP của máy trả lời
        public string IPAddress { get; set; } = "";


        // Kích thước payload
        public int Bytes { get; set; }


        // Round Trip Time
        public long RTT { get; set; }


        // TTL nhận được
        public int TTL { get; set; }


        // Trạng thái
        public string Status { get; set; } = "";


        // ICMP / Windows status code
        public uint IcmpStatus { get; set; }
    }
}