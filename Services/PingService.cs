using System;
using System.Threading;
using System.Threading.Tasks;
using MyBasicNetworkTools.Native;

namespace MyBasicNetworkTools.Services
{
    public class PingService
    {
        private readonly IcmpHelper _icmpHelper;

        public PingService()
        {
            _icmpHelper = new IcmpHelper();
        }

        /// <summary>
        /// Gửi một ICMP Echo Request.
        /// </summary>
        public async Task<IcmpPingResult> PingAsync(
            string host,
            int bufferSize = 32,
            int timeout = 3000,
            int ttl = 128,
            bool dontFragment = false,
            CancellationToken cancellationToken = default)
        {
            // Kiểm tra yêu cầu dừng trước khi Ping
            cancellationToken.ThrowIfCancellationRequested();

            // IcmpSendEcho là API synchronous,
            // nên chạy trên background thread để không làm treo giao diện.
            IcmpPingResult result =
                await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        return _icmpHelper.Ping(
                            host,
                            bufferSize,
                            timeout,
                            ttl,
                            dontFragment);
                    },
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            return result;
        }
    }
}