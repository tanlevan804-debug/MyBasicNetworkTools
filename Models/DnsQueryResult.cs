using System.Collections.Generic;

namespace MyBasicNetworkTools.Models
{
    public class DnsQueryResult
    {
        // ============================================================
        // DNS HEADER
        // ============================================================

        public ushort TransactionId { get; set; }

        public bool IsResponse { get; set; }

        public int Opcode { get; set; }

        public bool AuthoritativeAnswer { get; set; }

        public bool Truncated { get; set; }

        public bool RecursionDesired { get; set; }

        public bool RecursionAvailable { get; set; }

        public int RCode { get; set; }

        public string Status { get; set; } = "";

        // ============================================================
        // COUNTS
        // ============================================================

        public int QuestionCount { get; set; }

        public int AnswerCount { get; set; }

        public int AuthorityCount { get; set; }

        public int AdditionalCount { get; set; }

        // ============================================================
        // CONNECTION INFO
        // ============================================================

        public string DnsServer { get; set; } = "";

        public string Protocol { get; set; } = "";

        // Thời gian query tổng quát
        public long QueryTime { get; set; }

        // Thời gian UDP
        public long UdpQueryTime { get; set; }

        // Thời gian TCP fallback
        public long TcpQueryTime { get; set; }

        // Tổng thời gian từ lúc bắt đầu đến khi hoàn tất
        public long TotalQueryTime { get; set; }

        // ============================================================
        // EDNS(0)
        // ============================================================

        // Có EDNS OPT record hay không
        public bool EdnsPresent { get; set; }

        // UDP payload size được quảng bá
        // Ví dụ: 4096
        public ushort EdnsUdpPayloadSize { get; set; }

        // Extended RCODE của EDNS
        public int EdnsExtendedRCode { get; set; }

        // EDNS version
        public int EdnsVersion { get; set; }

        // EDNS flags
        public ushort EdnsFlags { get; set; }

        // ============================================================
        // QUESTION SECTION
        // ============================================================

        public List<DnsQuestion> Questions { get; set; } = new();

        // ============================================================
        // ANSWER / AUTHORITY / ADDITIONAL
        // ============================================================

        public List<DnsRecord> Records { get; set; } = new();

        // ============================================================
        // RAW DNS PACKET
        // ============================================================

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
}