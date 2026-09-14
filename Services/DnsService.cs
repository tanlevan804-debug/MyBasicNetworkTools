using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyBasicNetworkTools.Models;

namespace MyBasicNetworkTools.Services
{
    public class DnsService
    {
        private const int DefaultPort = 53;

        // ============================================================
        // DNS RECORD TYPES
        // ============================================================

        private static readonly Dictionary<string, ushort> RecordTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 1,
                ["NS"] = 2,
                ["CNAME"] = 5,
                ["SOA"] = 6,
                ["PTR"] = 12,
                ["MX"] = 15,
                ["TXT"] = 16,
                ["AAAA"] = 28,
                ["SRV"] = 33,

                // Extended / commonly used records
                ["DNAME"] = 39,
                ["DS"] = 43,
                ["RRSIG"] = 46,
                ["NSEC"] = 47,
                ["DNSKEY"] = 48,
                ["CAA"] = 257,

                ["ANY"] = 255
            };

        // ============================================================
        // MAIN DNS QUERY
        // ============================================================

        public async Task<DnsQueryResult> QueryAsync(
            string domain,
            string dnsServer,
            string recordType,
            int timeout = 3000,
            int port = DefaultPort,
            CancellationToken cancellationToken = default)
        {
            ValidateInput(
                domain,
                dnsServer,
                recordType,
                timeout,
                port);

            recordType = recordType.Trim().ToUpperInvariant();

            IPAddress serverIp =
                IPAddress.Parse(dnsServer);

            string queryName;

            // ========================================================
            // PTR
            // ========================================================

            if (recordType == "PTR")
            {
                queryName = ConvertToPtrName(domain);
            }
            else
            {
                queryName = NormalizeDomain(domain);
            }

            // ========================================================
            // TRANSACTION ID
            // ========================================================

            ushort transactionId =
                (ushort)Random.Shared.Next(0, 65536);

            // ========================================================
            // BUILD DNS REQUEST
            // ========================================================

            byte[] request =
                BuildDnsRequest(
                    queryName,
                    RecordTypes[recordType],
                    transactionId);

            // ========================================================
            // UDP QUERY
            // ========================================================

            Stopwatch totalStopwatch =
                Stopwatch.StartNew();

            UdpReceiveResult response;

            using (UdpClient udpClient = new())
            using (CancellationTokenSource timeoutCts =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                timeoutCts.CancelAfter(timeout);

                CancellationToken token =
                    timeoutCts.Token;

                IPEndPoint serverEndPoint =
                    new(serverIp, port);

                try
                {
                    await udpClient.SendAsync(
                        request,
                        request.Length,
                        serverEndPoint);

                    UdpReceiveResult receiveResult =
                        await udpClient
                            .ReceiveAsync(token);

                    response = receiveResult;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw new TimeoutException(
                        $"DNS Server không phản hồi sau {timeout} ms.");
                }
                catch (SocketException ex)
                {
                    throw new Exception(
                        $"UDP DNS query thất bại: {ex.Message}",
                        ex);
                }
            }

            totalStopwatch.Stop();

            // ========================================================
            // CHECK RESPONSE SOURCE
            // ========================================================

            if (!response.RemoteEndPoint.Address.Equals(serverIp) ||
                response.RemoteEndPoint.Port != port)
            {
                throw new Exception(
                    "DNS Response đến từ DNS Server không đúng.");
            }

            byte[] responseBytes =
                response.Buffer;

            // ========================================================
            // PARSE UDP RESPONSE
            // ========================================================

            DnsQueryResult result =
                ParseDnsResponse(
                    responseBytes,
                    transactionId);

            result.DnsServer =
                $"{serverIp}:{port}";

            result.Protocol = "UDP";

            result.QueryTime =
                totalStopwatch.ElapsedMilliseconds;

            result.RawPacketHex =
                ConvertToHex(responseBytes);

            // ========================================================
            // TCP FALLBACK
            // ========================================================
            //
            // Nếu TC = 1:
            //
            // UDP response bị truncate.
            //
            // Khi đó phải query lại bằng TCP.
            // ========================================================

            if (result.Truncated)
            {
                DnsQueryResult tcpResult =
                    await QueryTcpAsync(
                        serverIp,
                        port,
                        request,
                        transactionId,
                        timeout,
                        result.QueryTime,
                        cancellationToken);

                return tcpResult;
            }

            return result;
        }

        // ============================================================
        // VALIDATE INPUT
        // ============================================================

        private static void ValidateInput(
            string domain,
            string dnsServer,
            string recordType,
            int timeout,
            int port)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new ArgumentException(
                    "Domain không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(dnsServer))
            {
                throw new ArgumentException(
                    "DNS Server không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(recordType))
            {
                throw new ArgumentException(
                    "Record Type không được để trống.");
            }

            recordType =
                recordType.Trim().ToUpperInvariant();

            if (!RecordTypes.ContainsKey(recordType))
            {
                throw new ArgumentException(
                    $"Record Type '{recordType}' không được hỗ trợ.");
            }

            if (!IPAddress.TryParse(
                    dnsServer,
                    out IPAddress? serverIp))
            {
                throw new ArgumentException(
                    "DNS Server phải là địa chỉ IP hợp lệ.");
            }

            if (serverIp.AddressFamily !=
                AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Hiện tại chương trình chỉ hỗ trợ DNS Server IPv4.");
            }

            if (timeout < 100)
            {
                throw new ArgumentException(
                    "Timeout tối thiểu là 100 ms.");
            }

            if (timeout > 60000)
            {
                throw new ArgumentException(
                    "Timeout tối đa là 60000 ms.");
            }

            if (port <= 0 || port > 65535)
            {
                throw new ArgumentException(
                    "Port phải nằm trong khoảng 1-65535.");
            }
        }

        // ============================================================
        // TCP FALLBACK
        // ============================================================

        private async Task<DnsQueryResult> QueryTcpAsync(
            IPAddress serverIp,
            int port,
            byte[] request,
            ushort transactionId,
            int timeout,
            long udpTime,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch =
                Stopwatch.StartNew();

            using TcpClient tcpClient =
                new();

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutCts.CancelAfter(timeout);

            CancellationToken token =
                timeoutCts.Token;

            try
            {
                // ====================================================
                // TCP CONNECT
                // ====================================================

                await tcpClient.ConnectAsync(
                    serverIp,
                    port,
                    token);

                using NetworkStream stream =
                    tcpClient.GetStream();

                // ====================================================
                // TCP DNS LENGTH PREFIX
                // ====================================================
                //
                // DNS over TCP:
                //
                // 2 bytes length
                // +
                // DNS message
                // ====================================================

                if (request.Length > ushort.MaxValue)
                {
                    throw new Exception(
                        "DNS request quá lớn cho TCP.");
                }

                ushort requestLength =
                    (ushort)request.Length;

                byte[] lengthPrefix =
                {
                    (byte)(requestLength >> 8),
                    (byte)(requestLength & 0xFF)
                };

                await stream.WriteAsync(
                    lengthPrefix,
                    token);

                await stream.WriteAsync(
                    request,
                    token);

                await stream.FlushAsync(
                    token);

                // ====================================================
                // READ RESPONSE LENGTH
                // ====================================================

                byte[] responseLengthBuffer =
                    new byte[2];

                await ReadExactAsync(
                    stream,
                    responseLengthBuffer,
                    token);

                int responseLength =
                    (responseLengthBuffer[0] << 8) |
                    responseLengthBuffer[1];

                if (responseLength <= 0)
                {
                    throw new Exception(
                        "DNS TCP response có kích thước không hợp lệ.");
                }

                // ====================================================
                // READ RESPONSE
                // ====================================================

                byte[] response =
                    new byte[responseLength];

                await ReadExactAsync(
                    stream,
                    response,
                    token);

                stopwatch.Stop();

                // ====================================================
                // PARSE
                // ====================================================

                DnsQueryResult result =
                    ParseDnsResponse(
                        response,
                        transactionId);

                result.DnsServer =
                    $"{serverIp}:{port}";

                result.Protocol = "TCP";

                result.QueryTime =
                    udpTime +
                    stopwatch.ElapsedMilliseconds;

                result.RawPacketHex =
                    ConvertToHex(response);

                // TCP response không được tiếp tục fallback
                result.Truncated = false;

                return result;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new TimeoutException(
                    $"DNS TCP timeout sau {timeout} ms.");
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"DNS TCP query thất bại: {ex.Message}",
                    ex);
            }
        }

        // ============================================================
        // READ EXACT BYTES
        // ============================================================

        private static async Task ReadExactAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int offset = 0;

            while (offset < buffer.Length)
            {
                int read =
                    await stream.ReadAsync(
                        buffer.AsMemory(
                            offset,
                            buffer.Length - offset),
                        cancellationToken);

                if (read == 0)
                {
                    throw new Exception(
                        "DNS TCP connection bị đóng trước khi nhận đủ dữ liệu.");
                }

                offset += read;
            }
        }

        // ============================================================
        // PTR
        // ============================================================

        private static string ConvertToPtrName(
            string ip)
        {
            if (!IPAddress.TryParse(
                    ip.Trim(),
                    out IPAddress? address))
            {
                throw new ArgumentException(
                    "PTR Query yêu cầu IPv4 hợp lệ.");
            }

            if (address.AddressFamily !=
                AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "PTR hiện chỉ hỗ trợ IPv4.");
            }

            byte[] bytes =
                address.GetAddressBytes();

            return
                $"{bytes[3]}." +
                $"{bytes[2]}." +
                $"{bytes[1]}." +
                $"{bytes[0]}." +
                "in-addr.arpa";
        }

        // ============================================================
        // NORMALIZE DOMAIN
        // ============================================================

        private static string NormalizeDomain(
            string domain)
        {
            string result =
                domain.Trim();

            result =
                result.TrimEnd('.');

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new ArgumentException(
                    "Tên miền không hợp lệ.");
            }

            if (result.Length > 253)
            {
                throw new ArgumentException(
                    "Tên miền không được vượt quá 253 ký tự.");
            }

            string[] labels =
                result.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string label in labels)
            {
                if (label.Length == 0 ||
                    label.Length > 63)
                {
                    throw new ArgumentException(
                        "Mỗi DNS label phải dài từ 1 đến 63 ký tự.");
                }
            }

            return result;
        }

        // ============================================================
        // BUILD DNS REQUEST
        // ============================================================

        private static byte[] BuildDnsRequest(
            string domain,
            ushort recordType,
            ushort transactionId)
        {
            List<byte> packet =
                new();

            // --------------------------------------------------------
            // Transaction ID
            // --------------------------------------------------------

            packet.Add(
                (byte)(transactionId >> 8));

            packet.Add(
                (byte)(transactionId & 0xFF));

            // --------------------------------------------------------
            // FLAGS
            //
            // QR = 0
            // Opcode = 0
            // RD = 1
            // --------------------------------------------------------

            packet.Add(0x01);
            packet.Add(0x00);

            // --------------------------------------------------------
            // QDCOUNT = 1
            // --------------------------------------------------------

            packet.Add(0x00);
            packet.Add(0x01);

            // --------------------------------------------------------
            // ANCOUNT
            // NSCOUNT
            // ARCOUNT
            // --------------------------------------------------------

            packet.Add(0x00);
            packet.Add(0x00);

            packet.Add(0x00);
            packet.Add(0x00);

            packet.Add(0x00);
            packet.Add(0x00);

            // --------------------------------------------------------
            // QNAME
            // --------------------------------------------------------

            string normalized =
                domain.Trim().TrimEnd('.');

            string[] labels =
                normalized.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string label in labels)
            {
                byte[] labelBytes =
                    Encoding.ASCII.GetBytes(label);

                if (labelBytes.Length == 0 ||
                    labelBytes.Length > 63)
                {
                    throw new ArgumentException(
                        "DNS label phải dài từ 1 đến 63 byte.");
                }

                packet.Add(
                    (byte)labelBytes.Length);

                packet.AddRange(
                    labelBytes);
            }

            // End QNAME
            packet.Add(0x00);

            // --------------------------------------------------------
            // QTYPE
            // --------------------------------------------------------

            packet.Add(
                (byte)(recordType >> 8));

            packet.Add(
                (byte)(recordType & 0xFF));

            // --------------------------------------------------------
            // QCLASS = IN
            // --------------------------------------------------------

            packet.Add(0x00);
            packet.Add(0x01);

            return packet.ToArray();
        }

        // ============================================================
        // PARSE DNS RESPONSE
        // ============================================================

        private static DnsQueryResult ParseDnsResponse(
            byte[] data,
            ushort expectedTransactionId)
        {
            if (data == null ||
                data.Length < 12)
            {
                throw new Exception(
                    "DNS response quá ngắn.");
            }

            int offset = 0;

            // ========================================================
            // HEADER
            // ========================================================

            ushort transactionId =
                ReadUInt16(
                    data,
                    ref offset);

            ushort flags =
                ReadUInt16(
                    data,
                    ref offset);

            ushort questionCount =
                ReadUInt16(
                    data,
                    ref offset);

            ushort answerCount =
                ReadUInt16(
                    data,
                    ref offset);

            ushort authorityCount =
                ReadUInt16(
                    data,
                    ref offset);

            ushort additionalCount =
                ReadUInt16(
                    data,
                    ref offset);

            // ========================================================
            // TRANSACTION ID
            // ========================================================

            if (transactionId !=
                expectedTransactionId)
            {
                throw new Exception(
                    "Transaction ID trong DNS Response không khớp.");
            }

            // ========================================================
            // FLAGS
            // ========================================================

            bool isResponse =
                (flags & 0x8000) != 0;

            int opcode =
                (flags >> 11) & 0x0F;

            bool authoritative =
                (flags & 0x0400) != 0;

            bool truncated =
                (flags & 0x0200) != 0;

            bool recursionDesired =
                (flags & 0x0100) != 0;

            bool recursionAvailable =
                (flags & 0x0080) != 0;

            int rcode =
                flags & 0x000F;

            if (!isResponse)
            {
                throw new Exception(
                    "DNS packet nhận được không phải Response.");
            }

            // ========================================================
            // RESULT
            // ========================================================

            DnsQueryResult result =
                new()
                {
                    TransactionId =
                        transactionId,

                    IsResponse =
                        isResponse,

                    Opcode =
                        opcode,

                    Status =
                        GetRcodeName(rcode),

                    RCode =
                        rcode,

                    AuthoritativeAnswer =
                        authoritative,

                    Truncated =
                        truncated,

                    RecursionDesired =
                        recursionDesired,

                    RecursionAvailable =
                        recursionAvailable,

                    QuestionCount =
                        questionCount,

                    AnswerCount =
                        answerCount,

                    AuthorityCount =
                        authorityCount,

                    AdditionalCount =
                        additionalCount
                };

            // ========================================================
            // QUESTIONS
            // ========================================================

            for (int i = 0;
                 i < questionCount;
                 i++)
            {
                ReadDnsName(
                    data,
                    ref offset);

                if (offset + 4 >
                    data.Length)
                {
                    throw new Exception(
                        "DNS Question không hợp lệ.");
                }

                // QTYPE
                offset += 2;

                // QCLASS
                offset += 2;
            }

            // ========================================================
            // ANSWER
            // ========================================================

            ParseRecords(
                data,
                ref offset,
                answerCount,
                "Answer",
                result.Records);

            // ========================================================
            // AUTHORITY
            // ========================================================

            ParseRecords(
                data,
                ref offset,
                authorityCount,
                "Authority",
                result.Records);

            // ========================================================
            // ADDITIONAL
            // ========================================================

            ParseRecords(
                data,
                ref offset,
                additionalCount,
                "Additional",
                result.Records);

            return result;
        }

        // ============================================================
        // PARSE RECORDS
        // ============================================================

        private static void ParseRecords(
            byte[] data,
            ref int offset,
            int count,
            string section,
            List<DnsRecord> records)
        {
            for (int i = 0;
                 i < count;
                 i++)
            {
                if (offset >= data.Length)
                {
                    throw new Exception(
                        "DNS Record vượt quá packet.");
                }

                string name =
                    ReadDnsName(
                        data,
                        ref offset);

                ushort type =
                    ReadUInt16(
                        data,
                        ref offset);

                // CLASS
                ushort dnsClass =
                    ReadUInt16(
                        data,
                        ref offset);

                uint ttl =
                    ReadUInt32(
                        data,
                        ref offset);

                ushort dataLength =
                    ReadUInt16(
                        data,
                        ref offset);

                if (offset + dataLength >
                    data.Length)
                {
                    throw new Exception(
                        "DNS RDATA vượt quá packet.");
                }

                int rdataOffset =
                    offset;

                string value =
                    ParseRecordValue(
                        data,
                        type,
                        rdataOffset,
                        dataLength);

                offset += dataLength;

                records.Add(
                    new DnsRecord
                    {
                        RecordType =
                            GetRecordTypeName(type),

                        Name =
                            name,

                        Value =
                            value,

                        TTL =
                            ttl,

                        Section =
                            section
                    });
            }
        }

        // ============================================================
        // PARSE RDATA
        // ============================================================

        private static string ParseRecordValue(
            byte[] data,
            ushort type,
            int offset,
            int length)
        {
            switch (type)
            {
                // ====================================================
                // A
                // ====================================================

                case 1:
                    if (length != 4)
                        return "Invalid IPv4";

                    return new IPAddress(
                        data
                            .Skip(offset)
                            .Take(4)
                            .ToArray())
                        .ToString();

                // ====================================================
                // NS
                // ====================================================

                case 2:
                    {
                        int tempOffset =
                            offset;

                        return ReadDnsName(
                            data,
                            ref tempOffset);
                    }

                // ====================================================
                // CNAME
                // ====================================================

                case 5:
                    {
                        int tempOffset =
                            offset;

                        return ReadDnsName(
                            data,
                            ref tempOffset);
                    }

                // ====================================================
                // SOA
                // ====================================================

                case 6:
                    return ParseSoaRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // PTR
                // ====================================================

                case 12:
                    {
                        int tempOffset =
                            offset;

                        return ReadDnsName(
                            data,
                            ref tempOffset);
                    }

                // ====================================================
                // MX
                // ====================================================

                case 15:
                    {
                        if (length < 3)
                            return "Invalid MX";

                        int tempOffset =
                            offset;

                        ushort priority =
                            ReadUInt16(
                                data,
                                ref tempOffset);

                        string exchange =
                            ReadDnsName(
                                data,
                                ref tempOffset);

                        return
                            $"Priority={priority}; " +
                            $"Server={exchange}";
                    }

                // ====================================================
                // TXT
                // ====================================================

                case 16:
                    return ParseTxtRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // AAAA
                // ====================================================

                case 28:
                    if (length != 16)
                        return "Invalid IPv6";

                    return new IPAddress(
                        data
                            .Skip(offset)
                            .Take(16)
                            .ToArray())
                        .ToString();

                // ====================================================
                // SRV
                // ====================================================

                case 33:
                    {
                        if (length < 7)
                            return "Invalid SRV";

                        int tempOffset =
                            offset;

                        ushort priority =
                            ReadUInt16(
                                data,
                                ref tempOffset);

                        ushort weight =
                            ReadUInt16(
                                data,
                                ref tempOffset);

                        ushort port =
                            ReadUInt16(
                                data,
                                ref tempOffset);

                        string target =
                            ReadDnsName(
                                data,
                                ref tempOffset);

                        return
                            $"Priority={priority}; " +
                            $"Weight={weight}; " +
                            $"Port={port}; " +
                            $"Target={target}";
                    }

                // ====================================================
                // DNAME
                // ====================================================

                case 39:
                    {
                        int tempOffset =
                            offset;

                        return ReadDnsName(
                            data,
                            ref tempOffset);
                    }

                // ====================================================
                // DS
                // ====================================================

                case 43:
                    return ParseDsRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // RRSIG
                // ====================================================

                case 46:
                    return ParseRrsigRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // NSEC
                // ====================================================

                case 47:
                    return ParseNsecRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // DNSKEY
                // ====================================================

                case 48:
                    return ParseDnsKeyRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // CAA
                // ====================================================

                case 257:
                    return ParseCaaRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // OPT
                // ====================================================

                case 41:
                    return ParseOptRecord(
                        data,
                        offset,
                        length);

                // ====================================================
                // UNKNOWN
                // ====================================================

                default:
                    return ConvertToHex(
                        data,
                        offset,
                        length);
            }
        }

        // ============================================================
        // SOA
        // ============================================================

        private static string ParseSoaRecord(
            byte[] data,
            int offset,
            int length)
        {
            int start =
                offset;

            int end =
                offset + length;

            if (end > data.Length)
                return "Invalid SOA";

            try
            {
                string primaryNameServer =
                    ReadDnsName(
                        data,
                        ref offset);

                string responsibleMailbox =
                    ReadDnsName(
                        data,
                        ref offset);

                uint serial =
                    ReadUInt32(
                        data,
                        ref offset);

                uint refresh =
                    ReadUInt32(
                        data,
                        ref offset);

                uint retry =
                    ReadUInt32(
                        data,
                        ref offset);

                uint expire =
                    ReadUInt32(
                        data,
                        ref offset);

                uint minimum =
                    ReadUInt32(
                        data,
                        ref offset);

                if (offset > end)
                    return "Invalid SOA";

                return
                    $"Primary NS={primaryNameServer}; " +
                    $"Responsible={responsibleMailbox}; " +
                    $"Serial={serial}; " +
                    $"Refresh={refresh}; " +
                    $"Retry={retry}; " +
                    $"Expire={expire}; " +
                    $"Minimum={minimum}";
            }
            catch
            {
                return
                    $"Invalid SOA RDATA " +
                    $"(offset={start}, length={length})";
            }
        }

        // ============================================================
        // TXT
        // ============================================================

        private static string ParseTxtRecord(
            byte[] data,
            int offset,
            int length)
        {
            int end =
                offset + length;

            if (end > data.Length)
                return "Invalid TXT";

            List<string> parts =
                new();

            int current =
                offset;

            while (current < end)
            {
                int textLength =
                    data[current++];

                if (current + textLength >
                    end)
                {
                    return "Invalid TXT";
                }

                string text =
                    Encoding.UTF8.GetString(
                        data,
                        current,
                        textLength);

                parts.Add(text);

                current +=
                    textLength;
            }

            return string.Join(
                " | ",
                parts);
        }

        // ============================================================
        // DS
        // ============================================================

        private static string ParseDsRecord(
            byte[] data,
            int offset,
            int length)
        {
            if (length < 4)
                return "Invalid DS";

            int current =
                offset;

            ushort keyTag =
                ReadUInt16(
                    data,
                    ref current);

            byte algorithm =
                data[current++];

            byte digestType =
                data[current++];

            int digestLength =
                length - 4;

            string digest =
                ConvertToHex(
                    data,
                    current,
                    digestLength);

            return
                $"KeyTag={keyTag}; " +
                $"Algorithm={algorithm}; " +
                $"DigestType={digestType}; " +
                $"Digest={digest}";
        }

        // ============================================================
        // DNSKEY
        // ============================================================

        private static string ParseDnsKeyRecord(
            byte[] data,
            int offset,
            int length)
        {
            if (length < 4)
                return "Invalid DNSKEY";

            int current =
                offset;

            ushort flags =
                ReadUInt16(
                    data,
                    ref current);

            byte protocol =
                data[current++];

            byte algorithm =
                data[current++];

            string publicKey =
                ConvertToHex(
                    data,
                    current,
                    length - 4);

            return
                $"Flags={flags}; " +
                $"Protocol={protocol}; " +
                $"Algorithm={algorithm}; " +
                $"PublicKey={publicKey}";
        }

        // ============================================================
        // CAA
        // ============================================================

        private static string ParseCaaRecord(
            byte[] data,
            int offset,
            int length)
        {
            if (length < 2)
                return "Invalid CAA";

            int current =
                offset;

            byte flags =
                data[current++];

            byte tagLength =
                data[current++];

            if (current + tagLength >
                offset + length)
            {
                return "Invalid CAA";
            }

            string tag =
                Encoding.ASCII.GetString(
                    data,
                    current,
                    tagLength);

            current +=
                tagLength;

            string value =
                Encoding.UTF8.GetString(
                    data,
                    current,
                    offset + length - current);

            return
                $"Flags={flags}; " +
                $"Tag={tag}; " +
                $"Value={value}";
        }

        // ============================================================
        // RRSIG
        // ============================================================

        private static string ParseRrsigRecord(
            byte[] data,
            int offset,
            int length)
        {
            if (length < 18)
                return "Invalid RRSIG";

            int current =
                offset;

            ushort typeCovered =
                ReadUInt16(
                    data,
                    ref current);

            byte algorithm =
                data[current++];

            byte labels =
                data[current++];

            uint originalTtl =
                ReadUInt32(
                    data,
                    ref current);

            uint expiration =
                ReadUInt32(
                    data,
                    ref current);

            uint inception =
                ReadUInt32(
                    data,
                    ref current);

            ushort keyTag =
                ReadUInt16(
                    data,
                    ref current);

            string signerName =
                ReadDnsName(
                    data,
                    ref current);

            return
                $"TypeCovered={GetRecordTypeName(typeCovered)}; " +
                $"Algorithm={algorithm}; " +
                $"Labels={labels}; " +
                $"OriginalTTL={originalTtl}; " +
                $"Expiration={expiration}; " +
                $"Inception={inception}; " +
                $"KeyTag={keyTag}; " +
                $"Signer={signerName}";
        }

        // ============================================================
        // NSEC
        // ============================================================

        private static string ParseNsecRecord(
            byte[] data,
            int offset,
            int length)
        {
            int end =
                offset + length;

            if (end > data.Length)
                return "Invalid NSEC";

            int current =
                offset;

            try
            {
                string nextDomain =
                    ReadDnsName(
                        data,
                        ref current);

                string bitmap =
                    ConvertToHex(
                        data,
                        current,
                        end - current);

                return
                    $"NextDomain={nextDomain}; " +
                    $"TypeBitmap={bitmap}";
            }
            catch
            {
                return "Invalid NSEC";
            }
        }

        // ============================================================
        // OPT
        // ============================================================

        private static string ParseOptRecord(
            byte[] data,
            int offset,
            int length)
        {
            if (length == 0)
                return "EDNS OPT";

            return
                $"EDNS Options={ConvertToHex(data, offset, length)}";
        }

        // ============================================================
        // DNS NAME DECODER
        // ============================================================

        private static string ReadDnsName(
            byte[] data,
            ref int offset)
        {
            StringBuilder name =
                new();

            int current =
                offset;

            bool jumped =
                false;

            int jumpOffset =
                -1;

            HashSet<int> visitedPointers =
                new();

            while (true)
            {
                if (current >= data.Length)
                {
                    throw new Exception(
                        "DNS name vượt quá packet.");
                }

                byte length =
                    data[current];

                // ====================================================
                // COMPRESSION POINTER
                // ====================================================

                if ((length & 0xC0) == 0xC0)
                {
                    if (current + 1 >=
                        data.Length)
                    {
                        throw new Exception(
                            "DNS compression pointer không hợp lệ.");
                    }

                    ushort pointer =
                        (ushort)(
                            ((length & 0x3F) << 8) |
                            data[current + 1]);

                    if (pointer >=
                        data.Length)
                    {
                        throw new Exception(
                            "DNS compression pointer vượt quá packet.");
                    }

                    if (!visitedPointers.Add(pointer))
                    {
                        throw new Exception(
                            "DNS compression pointer bị loop.");
                    }

                    if (!jumped)
                    {
                        jumpOffset =
                            current + 2;
                    }

                    current =
                        pointer;

                    jumped =
                        true;

                    continue;
                }

                // ====================================================
                // INVALID LABEL FORMAT
                // ====================================================

                if ((length & 0xC0) != 0)
                {
                    throw new Exception(
                        "DNS label có format không hợp lệ.");
                }

                current++;

                // ====================================================
                // END OF NAME
                // ====================================================

                if (length == 0)
                {
                    if (!jumped)
                    {
                        offset =
                            current;
                    }
                    else
                    {
                        offset =
                            jumpOffset;
                    }

                    break;
                }

                // DNS label max = 63 bytes
                if (length > 63)
                {
                    throw new Exception(
                        "DNS label vượt quá 63 byte.");
                }

                if (current + length >
                    data.Length)
                {
                    throw new Exception(
                        "DNS label vượt quá packet.");
                }

                string label =
                    Encoding.ASCII.GetString(
                        data,
                        current,
                        length);

                if (name.Length > 0)
                {
                    name.Append(".");
                }

                name.Append(
                    label);

                current +=
                    length;

                if (!jumped)
                {
                    offset =
                        current;
                }
            }

            if (name.Length == 0)
            {
                return ".";
            }

            return name.ToString();
        }

        // ============================================================
        // UINT16
        // ============================================================

        private static ushort ReadUInt16(
            byte[] data,
            ref int offset)
        {
            if (offset + 2 >
                data.Length)
            {
                throw new Exception(
                    "DNS packet thiếu UInt16.");
            }

            ushort value =
                (ushort)(
                    (data[offset] << 8) |
                    data[offset + 1]);

            offset +=
                2;

            return value;
        }

        // ============================================================
        // UINT32
        // ============================================================

        private static uint ReadUInt32(
            byte[] data,
            ref int offset)
        {
            if (offset + 4 >
                data.Length)
            {
                throw new Exception(
                    "DNS packet thiếu UInt32.");
            }

            uint value =
                ((uint)data[offset] << 24) |
                ((uint)data[offset + 1] << 16) |
                ((uint)data[offset + 2] << 8) |
                data[offset + 3];

            offset +=
                4;

            return value;
        }

        // ============================================================
        // RECORD TYPE
        // ============================================================

        private static string GetRecordTypeName(
            ushort type)
        {
            return type switch
            {
                1 => "A",
                2 => "NS",
                5 => "CNAME",
                6 => "SOA",
                12 => "PTR",
                15 => "MX",
                16 => "TXT",
                28 => "AAAA",
                33 => "SRV",
                39 => "DNAME",
                41 => "OPT",
                43 => "DS",
                46 => "RRSIG",
                47 => "NSEC",
                48 => "DNSKEY",
                255 => "ANY",
                257 => "CAA",
                _ => $"TYPE{type}"
            };
        }

        // ============================================================
        // RCODE
        // ============================================================

        private static string GetRcodeName(
            int rcode)
        {
            return rcode switch
            {
                0 => "NOERROR",
                1 => "FORMERR",
                2 => "SERVFAIL",
                3 => "NXDOMAIN",
                4 => "NOTIMP",
                5 => "REFUSED",
                6 => "YXDOMAIN",
                7 => "YXRRSET",
                8 => "NXRRSET",
                9 => "NOTAUTH",
                10 => "NOTZONE",
                _ => $"RCODE {rcode}"
            };
        }

        // ============================================================
        // HEX
        // ============================================================

        private static string ConvertToHex(
            byte[] data)
        {
            return ConvertToHex(
                data,
                0,
                data.Length);
        }

        private static string ConvertToHex(
            byte[] data,
            int offset,
            int length)
        {
            if (data == null ||
                length <= 0 ||
                offset < 0 ||
                offset >= data.Length)
            {
                return "";
            }

            int end =
                Math.Min(
                    offset + length,
                    data.Length);

            StringBuilder builder =
                new();

            for (int i = offset;
                 i < end;
                 i++)
            {
                builder.Append(
                    data[i].ToString("X2"));

                if (i < end - 1)
                {
                    builder.Append(" ");
                }
            }

            return builder.ToString();
        }
    }
}