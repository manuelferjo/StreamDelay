namespace StreamDelay
{
	internal enum SinkState
	{
		Ready,
		Waiting,
		Connecting,
		Live,
		Retrying,
		NoKey
	}
}
