namespace MyBasicNetworkTools.Models
{
    public class DnsRecord
    {
        public string RecordType { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public uint TTL { get; set; }
        public string Section { get; set; } = "";
    }
}