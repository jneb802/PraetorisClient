using System;

namespace PraetorisClient
{
    internal sealed class LinkRequest
    {
        public long Sender;
        public string RequestId = "";
        public string Code = "";
        public string PlayerId = "";
        public string PlayerName = "";
        public string Endpoint = "";
        public string PlatformDisplayName = "";
        public DateTime ReceivedAtUtc;
    }
}
