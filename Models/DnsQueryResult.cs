using System.Collections.Generic;

namespace MyBasicNetworkTools.Models
{
    public class DnsQueryResult
    {
        // =========================
        // DNS HEADER
        // =========================

        public ushort TransactionId { get; set; }

        public bool IsResponse { get; set; }

        public int Opcode { get; set; }

        public bool AuthoritativeAnswer { get; set; }

        public bool Truncated { get; set; }

        public bool RecursionDesired { get; set; }

        public bool RecursionAvailable { get; set; }

        public int RCode { get; set; }

        public string Status { get; set; } = "";

        // =========================
        // COUNTS
        // =========================

        public int QuestionCount { get; set; }

        public int AnswerCount { get; set; }

        public int AuthorityCount { get; set; }

        public int AdditionalCount { get; set; }

        // =========================
        // CONNECTION INFO
        // =========================

        public string DnsServer { get; set; } = "";

        public string Protocol { get; set; } = "";

        public long QueryTime { get; set; }

        // =========================
        // RECORDS
        // =========================

        public List<DnsRecord> Records { get; set; } = new();

        // =========================
        // RAW PACKET
        // =========================

        public string RawPacketHex { get; set; } = "";
    }
}