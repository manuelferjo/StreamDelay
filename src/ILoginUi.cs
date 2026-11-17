// Lo que necesita una plataforma para enseñar el inicio de sesión (lo pone la ventana "Conectar").
namespace StreamDelay
{
	internal interface ILoginUi
	{
		bool Cancelled { get; }

		void ShowCode(string code, string url);

		void ShowBrowserWait(string url);
	}
}
