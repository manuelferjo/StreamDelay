namespace StreamDelay
{
	internal interface ILoginUi
	{
		bool Cancelled { get; }

		void ShowCode(string code, string url);

		void ShowBrowserWait(string url);
	}
}
