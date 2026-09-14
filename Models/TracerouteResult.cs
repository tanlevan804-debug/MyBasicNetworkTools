namespace MyBasicNetworkTools.Models
{
    public class TracerouteResult
    {
        public int Hop { get; set; }

        public string IPAddress { get; set; } = "*";

        public string Hostname { get; set; } = "*";

        public long RTT1 { get; set; } = -1;

        public long RTT2 { get; set; } = -1;

        public long RTT3 { get; set; } = -1;

        public double AverageRTT { get; set; } = -1;

        public string Status { get; set; } = "Timed Out";
    }
}