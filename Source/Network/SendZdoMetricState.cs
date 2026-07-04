namespace PraetorisClient
{
    internal readonly struct SendZdoMetricState
    {
        internal SendZdoMetricState(long peerUid, string playerName, int sentBefore, long startTicks)
        {
            PeerUid = peerUid;
            PlayerName = playerName;
            SentBefore = sentBefore;
            StartTicks = startTicks;
            IsValid = true;
        }

        internal long PeerUid { get; }
        internal string PlayerName { get; }
        internal int SentBefore { get; }
        internal long StartTicks { get; }
        internal bool IsValid { get; }
    }
}
