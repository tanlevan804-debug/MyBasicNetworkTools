using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyBasicNetworkTools.Services
{
    public class DnsService
    {
        // ============================================================
        // DNS RECORD TYPES
        // ============================================================

        private static readonly Dictionary<string, ushort> RecordTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "A", 1 },
                { "NS", 2 },
                { "CNAME", 5 },
                { "SOA", 6 },
                { "PTR", 12 },
                { "MX", 15 },
                { "TXT", 16 },
                { "AAAA", 28 },
                { "SRV", 33 },
                { "DNAME", 39 },
                { "OPT", 41 },
                { "DS", 43 },
                { "RRSIG", 46 },
                { "NSEC", 47 },
                { "DNSKEY", 48 },
                { "ANY", 255 }
            };

        private const ushort DNS_CLASS_IN = 1;

        // EDNS(0)
        private const ushort EDNS_TYPE_OPT = 41;

        // 4096 bytes is the advertised UDP payload size.
        private const ushort EDNS_UDP_PAYLOAD_SIZE = 4096;

        // ============================================================
        // PUBLIC API
        // ============================================================

        public async Task<DnsQueryResult> QueryAsync(
            string domain,
            string dnsServer,
            string recordType,
            int timeout = 5000,
            int port = 53,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("Domain không được để trống.");

            if (string.IsNullOrWhiteSpace(dnsServer))
                throw new ArgumentException("DNS Server không được để trống.");

            if (!IPAddress.TryParse(dnsServer, out IPAddress? dnsIp) ||
                dnsIp.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "DNS Server phải là địa chỉ IPv4 hợp lệ.");
            }

            if (!RecordTypes.TryGetValue(recordType.Trim(), out ushort qtype))
            {
                throw new ArgumentException(
                    $"Record type không được hỗ trợ: {recordType}");
            }

            if (timeout < 100)
                timeout = 100;

            if (timeout > 60000)
                timeout = 60000;

            if (port < 1 || port > 65535)
                throw new ArgumentException("Port phải nằm trong khoảng 1-65535.");

            // --------------------------------------------------------
            // IDN / PUNYCODE
            // --------------------------------------------------------

            string normalizedDomain = NormalizeDomain(domain);

            // PTR:
            // Input:
            // 8.8.8.8
            //
            // Query:
            // 8.8.8.8.in-addr.arpa
            //
            if (qtype == 12 &&
                IPAddress.TryParse(normalizedDomain, out IPAddress? ptrIp))
            {
                normalizedDomain = IPv4ToPtr(ptrIp);
            }

            // --------------------------------------------------------
            // TRANSACTION ID
            // --------------------------------------------------------

            ushort transactionId =
                (ushort)Random.Shared.Next(0, ushort.MaxValue + 1);

            // --------------------------------------------------------
            // BUILD DNS REQUEST
            // --------------------------------------------------------

            byte[] request = BuildDnsRequest(
                normalizedDomain,
                qtype,
                transactionId);

            Stopwatch totalWatch = Stopwatch.StartNew();

            // ========================================================
            // UDP QUERY
            // ========================================================

            DnsQueryResult udpResult;

            Stopwatch udpWatch = Stopwatch.StartNew();

            try
            {
                udpResult = await QueryUdpAsync(
                    request,
                    dnsIp,
                    port,
                    timeout,
                    transactionId,
                    cancellationToken);
            }
            finally
            {
                udpWatch.Stop();
            }

            long udpTime = udpWatch.ElapsedMilliseconds;

            udpResult.UdpQueryTime = udpTime;
            udpResult.TcpQueryTime = 0;
            udpResult.TotalQueryTime = udpTime;
            udpResult.QueryTime = udpTime;

            // ========================================================
            // TCP FALLBACK
            // ========================================================

            if (udpResult.Truncated)
            {
                Stopwatch tcpWatch = Stopwatch.StartNew();

                DnsQueryResult tcpResult;

                try
                {
                    tcpResult = await QueryTcpAsync(
                        request,
                        dnsIp,
                        port,
                        timeout,
                        transactionId,
                        cancellationToken);
                }
                finally
                {
                    tcpWatch.Stop();
                }

                long tcpTime = tcpWatch.ElapsedMilliseconds;

                totalWatch.Stop();

                tcpResult.UdpQueryTime = udpTime;
                tcpResult.TcpQueryTime = tcpTime;
                tcpResult.TotalQueryTime = totalWatch.ElapsedMilliseconds;

                // QueryTime = tổng thời gian thực tế
                tcpResult.QueryTime = tcpResult.TotalQueryTime;

                return tcpResult;
            }

            totalWatch.Stop();

            udpResult.TotalQueryTime =
                totalWatch.ElapsedMilliseconds;

            udpResult.QueryTime =
                udpResult.TotalQueryTime;

            return udpResult;
        }

        // ============================================================
        // UDP QUERY
        // ============================================================

        private async Task<DnsQueryResult> QueryUdpAsync(
            byte[] request,
            IPAddress dnsServer,
            int port,
            int timeout,
            ushort expectedTransactionId,
            CancellationToken cancellationToken)
        {
            using UdpClient client =
                new(AddressFamily.InterNetwork);

            IPEndPoint serverEndPoint =
                new(dnsServer, port);

            using CancellationTokenSource timeoutCts =
                new(timeout);

            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                await client.SendAsync(
                    request,
                    request.Length,
                    serverEndPoint);

                UdpReceiveResult response =
                    await client.ReceiveAsync(
                        linkedCts.Token);

                watch.Stop();

                // ----------------------------------------------------
                // SOURCE VALIDATION
                // ----------------------------------------------------

                if (!response.RemoteEndPoint.Address.Equals(dnsServer) ||
                    response.RemoteEndPoint.Port != port)
                {
                    throw new InvalidOperationException(
                        "DNS response đến từ server không hợp lệ.");
                }

                if (response.Buffer.Length < 12)
                {
                    throw new InvalidOperationException(
                        "DNS response quá ngắn: thiếu DNS Header.");
                }

                DnsQueryResult result =
                    ParseDnsResponse(
                        response.Buffer,
                        expectedTransactionId);

                result.DnsServer = dnsServer.ToString();
                result.Protocol = "UDP";
                result.RawPacketHex =
                    BytesToHex(response.Buffer);

                result.UdpQueryTime =
                    watch.ElapsedMilliseconds;

                return result;
            }
            catch (OperationCanceledException)
            {
                watch.Stop();

                if (cancellationToken.IsCancellationRequested)
                    throw;

                throw new TimeoutException(
                    $"DNS UDP request timeout sau {timeout} ms.");
            }
            catch (SocketException ex)
            {
                watch.Stop();

                throw new InvalidOperationException(
                    $"DNS UDP socket error: {ex.Message}",
                    ex);
            }
        }

        // ============================================================
        // TCP QUERY
        // ============================================================

        private async Task<DnsQueryResult> QueryTcpAsync(
            byte[] request,
            IPAddress dnsServer,
            int port,
            int timeout,
            ushort expectedTransactionId,
            CancellationToken cancellationToken)
        {
            using TcpClient client =
                new(AddressFamily.InterNetwork);

            using CancellationTokenSource timeoutCts =
                new(timeout);

            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                await client.ConnectAsync(
                    dnsServer,
                    port,
                    linkedCts.Token);

                NetworkStream stream =
                    client.GetStream();

                // ----------------------------------------------------
                // TCP DNS:
                //
                // 2 bytes length
                // + DNS message
                // ----------------------------------------------------

                if (request.Length > ushort.MaxValue)
                {
                    throw new InvalidOperationException(
                        "DNS request quá lớn cho TCP DNS.");
                }

                byte[] tcpRequest =
                    new byte[2 + request.Length];

                tcpRequest[0] =
                    (byte)(request.Length >> 8);

                tcpRequest[1] =
                    (byte)(request.Length & 0xFF);

                Buffer.BlockCopy(
                    request,
                    0,
                    tcpRequest,
                    2,
                    request.Length);

                await stream.WriteAsync(
                    tcpRequest,
                    linkedCts.Token);

                await stream.FlushAsync(
                    linkedCts.Token);

                // ----------------------------------------------------
                // READ 2-BYTE LENGTH
                // ----------------------------------------------------

                byte[] lengthBuffer = new byte[2];

                await ReadExactAsync(
                    stream,
                    lengthBuffer,
                    linkedCts.Token);

                int responseLength =
                    (lengthBuffer[0] << 8) |
                    lengthBuffer[1];

                if (responseLength < 12)
                {
                    throw new InvalidOperationException(
                        "DNS TCP response quá ngắn.");
                }

                byte[] response =
                    new byte[responseLength];

                await ReadExactAsync(
                    stream,
                    response,
                    linkedCts.Token);

                watch.Stop();

                DnsQueryResult result =
                    ParseDnsResponse(
                        response,
                        expectedTransactionId);

                result.DnsServer =
                    dnsServer.ToString();

                result.Protocol = "TCP";

                result.RawPacketHex =
                    BytesToHex(response);

                result.TcpQueryTime =
                    watch.ElapsedMilliseconds;

                return result;
            }
            catch (OperationCanceledException)
            {
                watch.Stop();

                if (cancellationToken.IsCancellationRequested)
                    throw;

                throw new TimeoutException(
                    $"DNS TCP request timeout sau {timeout} ms.");
            }
            catch (SocketException ex)
            {
                watch.Stop();

                throw new InvalidOperationException(
                    $"DNS TCP socket error: {ex.Message}",
                    ex);
            }
        }

        // ============================================================
        // READ EXACT NUMBER OF BYTES
        // ============================================================

        private static async Task ReadExactAsync(
            NetworkStream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            int totalRead = 0;

            while (totalRead < buffer.Length)
            {
                int read =
                    await stream.ReadAsync(
                        buffer.AsMemory(
                            totalRead,
                            buffer.Length - totalRead),
                        cancellationToken);

                if (read == 0)
                {
                    throw new InvalidOperationException(
                        "DNS TCP connection bị đóng trước khi nhận đủ dữ liệu.");
                }

                totalRead += read;
            }
        }

        // ============================================================
        // BUILD DNS REQUEST + EDNS(0)
        // ============================================================

        private static byte[] BuildDnsRequest(
            string domain,
            ushort qtype,
            ushort transactionId)
        {
            List<byte> packet = new();

            // --------------------------------------------------------
            // DNS HEADER
            // --------------------------------------------------------

            WriteUInt16(packet, transactionId);

            // Flags:
            //
            // QR = 0
            // Opcode = 0
            // AA = 0
            // TC = 0
            // RD = 1
            // RA = 0
            //
            ushort flags = 0x0100;

            WriteUInt16(packet, flags);

            // QDCOUNT = 1
            WriteUInt16(packet, 1);

            // ANCOUNT = 0
            WriteUInt16(packet, 0);

            // NSCOUNT = 0
            WriteUInt16(packet, 0);

            // ARCOUNT = 1
            //
            // Vì có EDNS OPT.
            //
            WriteUInt16(packet, 1);

            // --------------------------------------------------------
            // QUESTION
            // --------------------------------------------------------

            WriteDnsName(packet, domain);

            // QTYPE
            WriteUInt16(packet, qtype);

            // QCLASS = IN
            WriteUInt16(packet, DNS_CLASS_IN);

            // --------------------------------------------------------
            // EDNS(0) OPT RECORD
            //
            // NAME = root = 0
            // TYPE = 41
            // CLASS = UDP payload size
            // TTL = extended RCODE/version/flags
            // RDLEN = 0
            // --------------------------------------------------------

            packet.Add(0x00);

            // TYPE = OPT (41)
            WriteUInt16(packet, EDNS_TYPE_OPT);

            // CLASS = UDP payload size
            WriteUInt16(
                packet,
                EDNS_UDP_PAYLOAD_SIZE);

            // Extended RCODE = 0
            // Version = 0
            // Flags = 0
            WriteUInt32(packet, 0);

            // RDLEN = 0
            WriteUInt16(packet, 0);

            return packet.ToArray();
        }

        // ============================================================
        // PARSE DNS RESPONSE
        // ============================================================

        private static DnsQueryResult ParseDnsResponse(
            byte[] packet,
            ushort expectedTransactionId)
        {
            if (packet == null || packet.Length < 12)
            {
                throw new InvalidOperationException(
                    "DNS packet không hợp lệ: thiếu Header.");
            }

            int offset = 0;

            // --------------------------------------------------------
            // HEADER
            // --------------------------------------------------------

            ushort transactionId =
                ReadUInt16(packet, ref offset);

            ushort flags =
                ReadUInt16(packet, ref offset);

            ushort questionCount =
                ReadUInt16(packet, ref offset);

            ushort answerCount =
                ReadUInt16(packet, ref offset);

            ushort authorityCount =
                ReadUInt16(packet, ref offset);

            ushort additionalCount =
                ReadUInt16(packet, ref offset);

            // --------------------------------------------------------
            // TRANSACTION ID
            // --------------------------------------------------------

            if (transactionId != expectedTransactionId)
            {
                throw new InvalidOperationException(
                    $"Transaction ID không khớp. " +
                    $"Expected=0x{expectedTransactionId:X4}, " +
                    $"Received=0x{transactionId:X4}");
            }

            // --------------------------------------------------------
            // FLAGS
            // --------------------------------------------------------

            bool isResponse =
                (flags & 0x8000) != 0;

            int opcode =
                (flags >> 11) & 0x0F;

            bool authoritativeAnswer =
                (flags & 0x0400) != 0;

            bool truncated =
                (flags & 0x0200) != 0;

            bool recursionDesired =
                (flags & 0x0100) != 0;

            bool recursionAvailable =
                (flags & 0x0080) != 0;

            int rcode =
                flags & 0x000F;

            // --------------------------------------------------------
            // QR CHECK
            // --------------------------------------------------------

            if (!isResponse)
            {
                throw new InvalidOperationException(
                    "DNS packet không phải Response (QR=0).");
            }

            // --------------------------------------------------------
            // OPCODE
            // --------------------------------------------------------

            if (opcode != 0)
            {
                throw new InvalidOperationException(
                    $"DNS Opcode không được hỗ trợ: {opcode}");
            }

            DnsQueryResult result = new()
            {
                TransactionId = transactionId,

                IsResponse = isResponse,

                Opcode = opcode,

                AuthoritativeAnswer =
                    authoritativeAnswer,

                Truncated = truncated,

                RecursionDesired =
                    recursionDesired,

                RecursionAvailable =
                    recursionAvailable,

                RCode = rcode,

                Status = GetRCodeDescription(rcode),

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
            // QUESTION SECTION
            // ========================================================

            for (int i = 0; i < questionCount; i++)
            {
                string questionName =
                    ReadDnsName(packet, ref offset);

                EnsureBytesAvailable(
                    packet,
                    offset,
                    4,
                    "Question QTYPE/QCLASS");

                ushort qtype =
                    ReadUInt16(packet, ref offset);

                ushort qclass =
                    ReadUInt16(packet, ref offset);

                if (qclass != DNS_CLASS_IN)
                {
                    throw new InvalidOperationException(
                        $"DNS Class không được hỗ trợ: {qclass}");
                }

                result.Questions.Add(
                    new DnsQuestion
                    {
                        Name = questionName,
                        Type = GetRecordTypeName(qtype),
                        TypeCode = qtype,
                        Class = qclass
                    });
            }

            // ========================================================
            // ANSWER
            // ========================================================

            ParseRecords(
                packet,
                ref offset,
                answerCount,
                "Answer",
                result);

            // ========================================================
            // AUTHORITY
            // ========================================================

            ParseRecords(
                packet,
                ref offset,
                authorityCount,
                "Authority",
                result);

            // ========================================================
            // ADDITIONAL
            // ========================================================

            ParseRecords(
                packet,
                ref offset,
                additionalCount,
                "Additional",
                result);

            return result;
        }

        // ============================================================
        // PARSE RECORDS
        // ============================================================

        private static void ParseRecords(
            byte[] packet,
            ref int offset,
            ushort count,
            string section,
            DnsQueryResult result)
        {
            for (int i = 0; i < count; i++)
            {
                if (offset >= packet.Length)
                {
                    throw new InvalidOperationException(
                        $"DNS packet bị thiếu dữ liệu ở {section} section.");
                }

                string name =
                    ReadDnsName(packet, ref offset);

                EnsureBytesAvailable(
                    packet,
                    offset,
                    10,
                    $"{section} Record Header");

                ushort type =
                    ReadUInt16(packet, ref offset);

                ushort dnsClass =
                    ReadUInt16(packet, ref offset);

                uint ttl =
                    ReadUInt32(packet, ref offset);

                ushort rdLength =
                    ReadUInt16(packet, ref offset);

                EnsureBytesAvailable(
                    packet,
                    offset,
                    rdLength,
                    $"{section} RDATA");

                int rdataOffset = offset;

                string value;

                // ----------------------------------------------------
                // EDNS OPT
                // ----------------------------------------------------

                if (type == EDNS_TYPE_OPT)
                {
                    value =
                        ParseOptRecord(
                            packet,
                            rdataOffset,
                            rdLength,
                            dnsClass,
                            ttl);
                }
                else
                {
                    value =
                        ParseRecordValue(
                            packet,
                            rdataOffset,
                            rdLength,
                            type);
                }

                // Move offset after RDATA
                offset += rdLength;

                DnsRecord record = new()
                {
                    RecordType =
                        GetRecordTypeName(type),

                    RecordTypeCode = type,

                    Name = name,

                    Value = value,

                    TTL = ttl,

                    Section = section,

                    Class = dnsClass
                };

                result.Records.Add(record);

                // ----------------------------------------------------
                // Save EDNS information
                // ----------------------------------------------------

                if (type == EDNS_TYPE_OPT)
                {
                    result.EdnsPresent = true;

                    result.EdnsUdpPayloadSize =
                        dnsClass;

                    result.EdnsExtendedRCode =
                        (int)((ttl >> 24) & 0xFF);

                    result.EdnsVersion =
                        (int)((ttl >> 16) & 0xFF);

                    result.EdnsFlags =
                        (ushort)(ttl & 0xFFFF);
                }
            }
        }

        // ============================================================
        // PARSE EDNS OPT
        // ============================================================

        private static string ParseOptRecord(
            byte[] packet,
            int offset,
            int length,
            ushort udpPayloadSize,
            uint ttl)
        {
            int extendedRCode =
                (int)((ttl >> 24) & 0xFF);

            int version =
                (int)((ttl >> 16) & 0xFF);

            ushort flags =
                (ushort)(ttl & 0xFFFF);

            List<string> parts = new()
            {
                $"UDP Payload={udpPayloadSize}",
                $"Extended RCODE={extendedRCode}",
                $"Version={version}",
                $"Flags=0x{flags:X4}"
            };

            // --------------------------------------------------------
            // Parse EDNS options
            //
            // Option:
            // 2 bytes code
            // 2 bytes length
            // N bytes data
            // --------------------------------------------------------

            int end = offset + length;

            while (offset < end)
            {
                if (end - offset < 4)
                {
                    throw new InvalidOperationException(
                        "EDNS OPT option bị thiếu header.");
                }

                ushort optionCode =
                    ReadUInt16(packet, ref offset);

                ushort optionLength =
                    ReadUInt16(packet, ref offset);

                if (optionLength >
                    end - offset)
                {
                    throw new InvalidOperationException(
                        "EDNS OPT option có length vượt quá packet.");
                }

                byte[] optionData =
                    new byte[optionLength];

                Buffer.BlockCopy(
                    packet,
                    offset,
                    optionData,
                    0,
                    optionLength);

                offset += optionLength;

                parts.Add(
                    $"Option {optionCode}: {BytesToHex(optionData)}");
            }

            return string.Join("; ", parts);
        }

        // ============================================================
        // PARSE RDATA
        // ============================================================

        private static string ParseRecordValue(
            byte[] packet,
            int offset,
            int length,
            ushort type)
        {
            switch (type)
            {
                // ----------------------------------------------------
                // A
                // ----------------------------------------------------

                case 1:
                    if (length != 4)
                    {
                        return $"Invalid A RDATA length={length}";
                    }

                    return new IPAddress(
                        packet.Skip(offset)
                              .Take(4)
                              .ToArray())
                        .ToString();

                // ----------------------------------------------------
                // NS
                // ----------------------------------------------------

                case 2:
                    {
                        int nameOffset = offset;

                        return ReadDnsName(
                            packet,
                            ref nameOffset);
                    }

                // ----------------------------------------------------
                // CNAME
                // ----------------------------------------------------

                case 5:
                    {
                        int nameOffset = offset;

                        return ReadDnsName(
                            packet,
                            ref nameOffset);
                    }

                // ----------------------------------------------------
                // SOA
                // ----------------------------------------------------

                case 6:
                    return ParseSoaRecord(
                        packet,
                        offset,
                        length);

                // ----------------------------------------------------
                // PTR
                // ----------------------------------------------------

                case 12:
                    {
                        int nameOffset = offset;

                        return ReadDnsName(
                            packet,
                            ref nameOffset);
                    }

                // ----------------------------------------------------
                // MX
                // ----------------------------------------------------

                case 15:
                    return ParseMxRecord(
                        packet,
                        offset,
                        length);

                // ----------------------------------------------------
                // TXT
                // ----------------------------------------------------

                case 16:
                    return ParseTxtRecord(
                        packet,
                        offset,
                        length);

                // ----------------------------------------------------
                // AAAA
                // ----------------------------------------------------

                case 28:
                    if (length != 16)
                    {
                        return $"Invalid AAAA RDATA length={length}";
                    }

                    return new IPAddress(
                        packet.Skip(offset)
                              .Take(16)
                              .ToArray())
                        .ToString();

                // ----------------------------------------------------
                // SRV
                // ----------------------------------------------------

                case 33:
                    return ParseSrvRecord(
                        packet,
                        offset,
                        length);

                // ----------------------------------------------------
                // DNAME
                // ----------------------------------------------------

                case 39:
                    {
                        int nameOffset = offset;

                        return ReadDnsName(
                            packet,
                            ref nameOffset);
                    }

                // ----------------------------------------------------
                // ANY
                // ----------------------------------------------------

                case 255:
                    return BytesToHex(
                        packet,
                        offset,
                        length);

                // ----------------------------------------------------
                // Unknown
                // ----------------------------------------------------

                default:
                    return BytesToHex(
                        packet,
                        offset,
                        length);
            }
        }

        // ============================================================
        // SOA
        // ============================================================

        private static string ParseSoaRecord(
            byte[] packet,
            int offset,
            int length)
        {
            int start = offset;
            int end = offset + length;

            if (end > packet.Length)
            {
                throw new InvalidOperationException(
                    "SOA RDATA vượt quá packet.");
            }

            int nameOffset = offset;

            string primaryNs =
                ReadDnsName(
                    packet,
                    ref nameOffset);

            offset = nameOffset;

            string responsible =
                ReadDnsName(
                    packet,
                    ref offset);

            if (end - offset < 20)
            {
                throw new InvalidOperationException(
                    "SOA RDATA thiếu 5 trường số.");
            }

            uint serial =
                ReadUInt32(packet, ref offset);

            uint refresh =
                ReadUInt32(packet, ref offset);

            uint retry =
                ReadUInt32(packet, ref offset);

            uint expire =
                ReadUInt32(packet, ref offset);

            uint minimum =
                ReadUInt32(packet, ref offset);

            return
                $"Primary NS={primaryNs}; " +
                $"Responsible={responsible}; " +
                $"Serial={serial}; " +
                $"Refresh={refresh}; " +
                $"Retry={retry}; " +
                $"Expire={expire}; " +
                $"Minimum={minimum}";
        }

        // ============================================================
        // MX
        // ============================================================

        private static string ParseMxRecord(
            byte[] packet,
            int offset,
            int length)
        {
            if (length < 3)
            {
                return "Invalid MX RDATA";
            }

            int end = offset + length;

            ushort preference =
                ReadUInt16(packet, ref offset);

            string exchange =
                ReadDnsName(
                    packet,
                    ref offset);

            if (offset > end)
            {
                throw new InvalidOperationException(
                    "MX RDATA vượt quá giới hạn.");
            }

            return
                $"Preference={preference}; " +
                $"Exchange={exchange}";
        }

        // ============================================================
        // TXT
        // ============================================================

        private static string ParseTxtRecord(
            byte[] packet,
            int offset,
            int length)
        {
            int end = offset + length;

            List<string> strings = new();

            while (offset < end)
            {
                int stringLength =
                    packet[offset++];

                if (stringLength >
                    end - offset)
                {
                    throw new InvalidOperationException(
                        "TXT string length vượt quá RDATA.");
                }

                string text =
                    Encoding.UTF8.GetString(
                        packet,
                        offset,
                        stringLength);

                strings.Add(text);

                offset += stringLength;
            }

            return string.Join(" ", strings);
        }

        // ============================================================
        // SRV
        // ============================================================

        private static string ParseSrvRecord(
            byte[] packet,
            int offset,
            int length)
        {
            if (length < 7)
            {
                return "Invalid SRV RDATA";
            }

            int end = offset + length;

            ushort priority =
                ReadUInt16(packet, ref offset);

            ushort weight =
                ReadUInt16(packet, ref offset);

            ushort port =
                ReadUInt16(packet, ref offset);

            string target =
                ReadDnsName(
                    packet,
                    ref offset);

            if (offset > end)
            {
                throw new InvalidOperationException(
                    "SRV RDATA vượt quá giới hạn.");
            }

            return
                $"Priority={priority}; " +
                $"Weight={weight}; " +
                $"Port={port}; " +
                $"Target={target}";
        }

        // ============================================================
        // SAFE DNS NAME PARSER
        //
        // Supports:
        //   Normal labels
        //   Compression pointer
        //
        // Protects against:
        //   Invalid offset
        //   Label > 63
        //   Name > 255
        //   Pointer outside packet
        //   Pointer loop
        //   Excessive pointer jumps
        //   Malformed termination
        // ============================================================

        private static string ReadDnsName(
            byte[] packet,
            ref int offset)
        {
            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            if (offset < 0 ||
                offset >= packet.Length)
            {
                throw new InvalidOperationException(
                    "DNS name offset nằm ngoài packet.");
            }

            int originalOffset = offset;

            int currentOffset = offset;

            bool jumped = false;

            int consumedBytes = 0;

            int pointerSteps = 0;

            const int MAX_POINTER_STEPS = 128;

            HashSet<int> visitedPointers = new();

            List<string> labels = new();

            int totalNameLength = 0;

            while (true)
            {
                if (currentOffset < 0 ||
                    currentOffset >= packet.Length)
                {
                    throw new InvalidOperationException(
                        "DNS name trỏ ra ngoài packet.");
                }

                if (pointerSteps > MAX_POINTER_STEPS)
                {
                    throw new InvalidOperationException(
                        "DNS compression pointer vượt quá giới hạn.");
                }

                byte lengthByte =
                    packet[currentOffset];

                // ----------------------------------------------------
                // ROOT
                // ----------------------------------------------------

                if (lengthByte == 0)
                {
                    if (!jumped)
                    {
                        consumedBytes++;
                    }

                    currentOffset++;

                    break;
                }

                // ----------------------------------------------------
                // COMPRESSION POINTER
                //
                // First two bits = 11
                // ----------------------------------------------------

                if ((lengthByte & 0xC0) == 0xC0)
                {
                    if (currentOffset + 1 >= packet.Length)
                    {
                        throw new InvalidOperationException(
                            "DNS compression pointer bị thiếu byte thứ hai.");
                    }

                    ushort pointer =
                        (ushort)(
                            ((lengthByte & 0x3F) << 8) |
                            packet[currentOffset + 1]);

                    if (pointer >= packet.Length)
                    {
                        throw new InvalidOperationException(
                            $"DNS compression pointer {pointer} " +
                            $"nằm ngoài packet.");
                    }

                    // ------------------------------------------------
                    // LOOP DETECTION
                    // ------------------------------------------------

                    if (!visitedPointers.Add(pointer))
                    {
                        throw new InvalidOperationException(
                            "Phát hiện DNS compression pointer loop.");
                    }

                    pointerSteps++;

                    if (!jumped)
                    {
                        consumedBytes += 2;

                        // offset phải trỏ tới byte sau pointer
                        offset =
                            currentOffset + 2;

                        jumped = true;
                    }

                    currentOffset = pointer;

                    continue;
                }

                // ----------------------------------------------------
                // RESERVED LABEL TYPE
                // ----------------------------------------------------

                if ((lengthByte & 0xC0) != 0)
                {
                    throw new InvalidOperationException(
                        "DNS label có kiểu bit không hợp lệ.");
                }

                int labelLength = lengthByte;

                // DNS label tối đa 63 bytes
                if (labelLength > 63)
                {
                    throw new InvalidOperationException(
                        $"DNS label dài {labelLength} bytes, vượt quá 63.");
                }

                // ----------------------------------------------------
                // CHECK DATA
                // ----------------------------------------------------

                if (currentOffset + 1 + labelLength >
                    packet.Length)
                {
                    throw new InvalidOperationException(
                        "DNS label vượt quá packet.");
                }

                if (!jumped)
                {
                    consumedBytes +=
                        1 + labelLength;
                }

                // ----------------------------------------------------
                // CHECK TOTAL NAME LENGTH
                //
                // DNS full name <= 255 octets.
                // ----------------------------------------------------

                totalNameLength +=
                    labelLength + 1;

                if (totalNameLength > 255)
                {
                    throw new InvalidOperationException(
                        "DNS name vượt quá 255 bytes.");
                }

                string label =
                    Encoding.ASCII.GetString(
                        packet,
                        currentOffset + 1,
                        labelLength);

                labels.Add(label);

                currentOffset +=
                    1 + labelLength;
            }

            if (!jumped)
            {
                offset =
                    originalOffset + consumedBytes;
            }

            if (offset < 0 ||
                offset > packet.Length)
            {
                throw new InvalidOperationException(
                    "DNS name parser tạo offset không hợp lệ.");
            }

            if (labels.Count == 0)
                return ".";

            return string.Join(".", labels);
        }

        // ============================================================
        // WRITE DNS NAME
        // ============================================================

        private static void WriteDnsName(
            List<byte> packet,
            string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException(
                    "Domain không hợp lệ.");

            if (domain == ".")
            {
                packet.Add(0);
                return;
            }

            string trimmed =
                domain.Trim().TrimEnd('.');

            string[] labels =
                trimmed.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string label in labels)
            {
                byte[] bytes =
                    Encoding.ASCII.GetBytes(label);

                if (bytes.Length > 63)
                {
                    throw new ArgumentException(
                        $"DNS label '{label}' vượt quá 63 bytes.");
                }

                packet.Add((byte)bytes.Length);

                packet.AddRange(bytes);
            }

            // Root terminator
            packet.Add(0);
        }

        // ============================================================
        // IDN / PUNYCODE
        // ============================================================

        private static string NormalizeDomain(
            string domain)
        {
            string value =
                domain.Trim();

            if (value.Length == 0)
                throw new ArgumentException(
                    "Domain không hợp lệ.");

            // Root
            if (value == ".")
                return ".";

            // Remove final dot
            value =
                value.TrimEnd('.');

            if (value.Length == 0)
                return ".";

            System.Globalization.IdnMapping idn =
                new();

            string[] labels =
                value.Split(
                    '.',
                    StringSplitOptions.None);

            List<string> asciiLabels = new();

            foreach (string label in labels)
            {
                if (string.IsNullOrWhiteSpace(label))
                {
                    throw new ArgumentException(
                        "Domain chứa label rỗng.");
                }

                string asciiLabel;

                try
                {
                    asciiLabel =
                        idn.GetAscii(label);
                }
                catch (ArgumentException ex)
                {
                    throw new ArgumentException(
                        $"Domain/IDN không hợp lệ: {label}",
                        ex);
                }

                if (asciiLabel.Length > 63)
                {
                    throw new ArgumentException(
                        $"DNS label '{label}' vượt quá 63 ký tự.");
                }

                asciiLabels.Add(
                    asciiLabel.ToLowerInvariant());
            }

            string asciiDomain =
                string.Join(
                    ".",
                    asciiLabels);

            if (asciiDomain.Length > 253)
            {
                throw new ArgumentException(
                    "Domain vượt quá 253 ký tự.");
            }

            return asciiDomain;
        }

        // ============================================================
        // IPv4 -> PTR
        // ============================================================

        private static string IPv4ToPtr(
            IPAddress address)
        {
            byte[] bytes =
                address.GetAddressBytes();

            if (bytes.Length != 4)
            {
                throw new ArgumentException(
                    "PTR hiện chỉ hỗ trợ IPv4.");
            }

            return
                $"{bytes[3]}." +
                $"{bytes[2]}." +
                $"{bytes[1]}." +
                $"{bytes[0]}." +
                "in-addr.arpa";
        }

        // ============================================================
        // RCODE
        // ============================================================

        private static string GetRCodeDescription(
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

                _ => $"RCODE={rcode}"
            };
        }

        // ============================================================
        // RECORD TYPE NAME
        // ============================================================

        private static string GetRecordTypeName(
            ushort type)
        {
            foreach (var pair in RecordTypes)
            {
                if (pair.Value == type)
                    return pair.Key;
            }

            return $"TYPE{type}";
        }

        // ============================================================
        // READ UINT16
        // ============================================================

        private static ushort ReadUInt16(
            byte[] packet,
            ref int offset)
        {
            EnsureBytesAvailable(
                packet,
                offset,
                2,
                "UInt16");

            ushort value =
                (ushort)(
                    (packet[offset] << 8) |
                    packet[offset + 1]);

            offset += 2;

            return value;
        }

        // ============================================================
        // READ UINT32
        // ============================================================

        private static uint ReadUInt32(
            byte[] packet,
            ref int offset)
        {
            EnsureBytesAvailable(
                packet,
                offset,
                4,
                "UInt32");

            uint value =
                ((uint)packet[offset] << 24) |
                ((uint)packet[offset + 1] << 16) |
                ((uint)packet[offset + 2] << 8) |
                packet[offset + 3];

            offset += 4;

            return value;
        }

        // ============================================================
        // WRITE UINT16
        // ============================================================

        private static void WriteUInt16(
            List<byte> packet,
            ushort value)
        {
            packet.Add(
                (byte)(value >> 8));

            packet.Add(
                (byte)(value & 0xFF));
        }

        // ============================================================
        // WRITE UINT32
        // ============================================================

        private static void WriteUInt32(
            List<byte> packet,
            uint value)
        {
            packet.Add(
                (byte)(value >> 24));

            packet.Add(
                (byte)(value >> 16));

            packet.Add(
                (byte)(value >> 8));

            packet.Add(
                (byte)value);
        }

        // ============================================================
        // CHECK BUFFER
        // ============================================================

        private static void EnsureBytesAvailable(
            byte[] packet,
            int offset,
            int required,
            string section)
        {
            if (offset < 0 ||
                required < 0 ||
                offset > packet.Length - required)
            {
                throw new InvalidOperationException(
                    $"DNS packet bị thiếu dữ liệu tại {section}.");
            }
        }

        // ============================================================
        // HEX
        // ============================================================

        private static string BytesToHex(
            byte[] data)
        {
            return BytesToHex(
                data,
                0,
                data.Length);
        }

        private static string BytesToHex(
            byte[] data,
            int offset,
            int length)
        {
            if (data == null)
                return string.Empty;

            if (offset < 0 ||
                length < 0 ||
                offset > data.Length - length)
            {
                throw new ArgumentOutOfRangeException();
            }

            StringBuilder sb =
                new(length * 3);

            for (int i = 0; i < length; i++)
            {
                if (i > 0)
                    sb.Append(' ');

                sb.Append(
                    data[offset + i]
                        .ToString("X2"));
            }

            return sb.ToString();
        }
    }

    // ================================================================
    // DNS QUERY RESULT
    // ================================================================

    public class DnsQueryResult
    {
        public ushort TransactionId { get; set; }

        public bool IsResponse { get; set; }

        public int Opcode { get; set; }

        public int RCode { get; set; }

        public string Status { get; set; } = "";

        public bool AuthoritativeAnswer { get; set; }

        public bool Truncated { get; set; }

        public bool RecursionDesired { get; set; }

        public bool RecursionAvailable { get; set; }

        public ushort QuestionCount { get; set; }

        public ushort AnswerCount { get; set; }

        public ushort AuthorityCount { get; set; }

        public ushort AdditionalCount { get; set; }

        // ------------------------------------------------------------
        // SERVER / PROTOCOL
        // ------------------------------------------------------------

        public string DnsServer { get; set; } = "";

        public string Protocol { get; set; } = "";

        // ------------------------------------------------------------
        // TIMING
        // ------------------------------------------------------------

        public long QueryTime { get; set; }

        public long UdpQueryTime { get; set; }

        public long TcpQueryTime { get; set; }

        public long TotalQueryTime { get; set; }

        // ------------------------------------------------------------
        // EDNS
        // ------------------------------------------------------------

        public bool EdnsPresent { get; set; }

        public ushort EdnsUdpPayloadSize { get; set; }

        public int EdnsExtendedRCode { get; set; }

        public int EdnsVersion { get; set; }

        public ushort EdnsFlags { get; set; }

        // ------------------------------------------------------------
        // QUESTION
        // ------------------------------------------------------------

        public List<DnsQuestion> Questions { get; set; } = new();

        // ------------------------------------------------------------
        // RECORDS
        // ------------------------------------------------------------

        public List<DnsRecord> Records { get; set; } = new();

        // ------------------------------------------------------------
        // RAW PACKET
        // ------------------------------------------------------------

        public string RawPacketHex { get; set; } = "";
    }

    // ================================================================
    // DNS QUESTION
    // ================================================================

    public class DnsQuestion
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";

        public ushort TypeCode { get; set; }

        public ushort Class { get; set; }
    }

    // ================================================================
    // DNS RECORD
    // ================================================================

    public class DnsRecord
    {
        public string RecordType { get; set; } = "";

        public ushort RecordTypeCode { get; set; }

        public string Name { get; set; } = "";

        public string Value { get; set; } = "";

        public uint TTL { get; set; }

        public string Section { get; set; } = "";

        public ushort Class { get; set; }
    }
}