using System;

namespace StreamDelay
{
	internal class ApiException : Exception
	{
		public int Status;

		public ApiException(int status, string msg)
			: base(msg)
		{
			Status = status;
		}
	}
}
