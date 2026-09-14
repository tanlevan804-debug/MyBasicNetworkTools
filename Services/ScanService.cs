using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MyBasicNetworkTools.Services
{
    public class ScanService
    {
        public async Task<ScanResultData> ScanHostAsync(
            string ip,
            int timeout = 1000)
        {
            try
            {
                using Ping ping = new();

                Stopwatch stopwatch =
                    Stopwatch.StartNew();

                PingReply reply =
                    await ping.SendPingAsync(
                        ip,
                        timeout);

                stopwatch.Stop();

                if (reply.Status == IPStatus.Success)
                {
                    return new ScanResultData
                    {
                        IP = ip,
                        Status = "Online",
                        ResponseTime =
                            stopwatch.ElapsedMilliseconds
                    };
                }

                return new ScanResultData
                {
                    IP = ip,
                    Status = "Offline",
                    ResponseTime = -1
                };
            }
            catch
            {
                return new ScanResultData
                {
                    IP = ip,
                    Status = "Offline",
                    ResponseTime = -1
                };
            }
        }
    }

    public class ScanResultData
    {
        public string IP { get; set; } = "";
        public string Status { get; set; } = "";
        public long ResponseTime { get; set; }
    }
}