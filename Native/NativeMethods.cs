using System;
using System.Runtime.InteropServices;

namespace MyBasicNetworkTools.Native
{
    public static class NativeMethods
    {
        [DllImport(
            "iphlpapi.dll",
            ExactSpelling = true)]
        public static extern int SendARP(
            uint DestIP,
            uint SrcIP,
            byte[] pMacAddr,
            ref uint PhyAddrLen);
    }
}