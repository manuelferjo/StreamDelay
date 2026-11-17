// Conexión de salida hacia una plataforma: RtmpConn (directa) o FfmpegConn (modo clásico).
namespace StreamDelay
{
	internal interface IConn
	{
		void Write(byte type, long ts, byte[] data);

		void Close(bool polite);
	}
}
