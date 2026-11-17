namespace StreamDelay
{
	internal class FlvPacket
	{
		public byte Type;

		public long Ts;

		public byte[] Data;

		public long Arrival;

		public long OutTs;

		public bool Key;

		public bool Seq;

		public bool SeqStart;

		public int Track;

		public int SubType;

		public RtmpSession Src;
	}
}
