namespace StreamDelay
{
	internal class RtmpConn : IConn
	{
		private readonly RtmpClient c;

		public RtmpConn(RtmpClient c)
		{
			this.c = c;
		}

		public void Write(byte type, long ts, byte[] data)
		{
			c.WriteMedia(type, ts, data);
		}

		public void Close(bool polite)
		{
			c.Close(polite);
		}
	}
}
