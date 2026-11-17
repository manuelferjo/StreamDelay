// Estado de un destino mientras el servicio está en marcha (se ve en la franja de la pestaña Directo).
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
