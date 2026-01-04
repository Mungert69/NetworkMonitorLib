namespace NetworkMonitor.Utils
{
    public enum BleNoncePlacement
    {
        Start,
        End
    }

    public readonly struct BleCryptoOptions
    {
        public BleCryptoOptions(int nonceLength, int tagLength, BleNoncePlacement noncePlacement)
        {
            NonceLength = nonceLength;
            TagLength = tagLength;
            NoncePlacement = noncePlacement;
        }

        public int NonceLength { get; }
        public int TagLength { get; }
        public BleNoncePlacement NoncePlacement { get; }

        public static BleCryptoOptions Default => new(12, 16, BleNoncePlacement.Start);
    }
}
