namespace MyBasicNetworkTools.Models
{
    public class PingResult
    {
        // Số thứ tự packet
        public int Sequence { get; set; }

        // Địa chỉ IP phản hồi
        public string IPAddress { get; set; } = "";

        // Kích thước payload
        public int Bytes { get; set; }

        // Round Trip Time
        public long RTT { get; set; }

        // TTL của packet nhận được
        public int TTL { get; set; }

        // Trạng thái
        public string Status { get; set; } = "";
    }
}