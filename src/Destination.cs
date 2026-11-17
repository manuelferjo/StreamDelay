namespace StreamDelay
{
	internal class Destination
	{
		public string Id;

		public string Name;

		public string Server = "";

		public string Key = "";

		public bool Enabled;

		public bool IsTwitch;

		public Destination(string id, string name, bool twitch)
		{
			Id = id;
			Name = name;
			IsTwitch = twitch;
		}
	}
}
