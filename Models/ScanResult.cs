namespace MyBasicNetworkTools.Models
{
    public class ScanResult
    {
        public string IPAddress { get; set; } = "";

        public string Status { get; set; } = "Dead";

        public string Hostname { get; set; } = "*";

        public long RTT { get; set; } = -1;

        public string MACAddress { get; set; } = "-";

        public string OpenPorts { get; set; } = "-";

        public bool IsAlive { get; set; }

        public int OpenPortCount { get; set; }
    }
}