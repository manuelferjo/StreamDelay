using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal abstract class Platform
	{
		public string ClientId = "";

		public string ClientSecret = "";

		public bool Enabled = true;

		protected string access = "";

		protected string refresh = "";

		protected DateTime expires = DateTime.MinValue;

		public string Account = "";

		public List<Category> Results = new List<Category>();

		public Category Chosen;

		public string LastError;

		private readonly object tokenLock = new object();

		public abstract string Name { get; }

		public abstract string Key { get; }

		public abstract bool NeedsSecret { get; }

		public abstract string ConsoleUrl { get; }

		public virtual int TitleMax
		{
			get
			{
				return 140;
			}
		}

		public bool Configured
		{
			get
			{
				if (ClientId.Length > 0)
				{
					if (NeedsSecret)
					{
						return ClientSecret.Length > 0;
					}
					return true;
				}
				return false;
			}
		}

		public bool Connected
		{
			get
			{
				if (refresh.Length <= 0)
				{
					return access.Length > 0;
				}
				return true;
			}
		}

		public abstract void Login(ILoginUi ui);

		public abstract List<Category> Search(string q);

		public abstract void ReadCurrent(out string title, out Category cat);

		public abstract void Update(string title, Category cat);

		protected abstract void DoRefresh();

		protected abstract void LoadAccount();

		public void Logout()
		{
			access = (refresh = (Account = ""));
			expires = DateTime.MinValue;
			Chosen = null;
			Results.Clear();
		}

		protected void SetTokens(Dictionary<string, object> r)
		{
			string text = Http.Str(r, "access_token");
			if (text.Length == 0)
			{
				throw new ApiException(0, "la plataforma no devolvió el acceso");
			}
			access = text;
			string text2 = Http.Str(r, "refresh_token");
			if (text2.Length > 0)
			{
				refresh = text2;
			}
			int num = Http.Int(r, "expires_in", 3600);
			expires = DateTime.UtcNow.AddSeconds(Math.Max(60, num - 60));
		}

		protected string Token()
		{
			lock (tokenLock)
			{
				if (access.Length == 0 || DateTime.UtcNow >= expires)
				{
					if (refresh.Length == 0)
					{
						throw new ApiException(401, "sesión caducada: vuelve a conectar " + Name);
					}
					try
					{
						DoRefresh();
					}
					catch (ApiException ex)
					{
						if (ex.Status == 400 || ex.Status == 401)
						{
							Logout();
							throw new ApiException(401, "sesión caducada: vuelve a conectar " + Name);
						}
						throw;
					}
				}
				return access;
			}
		}

		protected Dictionary<string, object> Api(string method, string url, Dictionary<string, string> headers, object body)
		{
			string body2 = ((body == null) ? null : Http.ToJson(body));
			try
			{
				return Http.Send(method, url, Token(), headers, "application/json", body2);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 0 && method == "GET")
				{
					Thread.Sleep(700);
					return Http.Send(method, url, Token(), headers, "application/json", body2);
				}
				if (ex.Status != 401 || refresh.Length == 0)
				{
					throw;
				}
				lock (tokenLock)
				{
					expires = DateTime.MinValue;
				}
				return Http.Send(method, url, Token(), headers, "application/json", body2);
			}
		}

		public void FinishLogin()
		{
			LoadAccount();
		}

		public void Load(Func<string, string> get)
		{
			ClientId = get(Key + "_client");
			ClientSecret = Secret.Unprotect(get(Key + "_secret"));
			Enabled = get(Key + "_on") != "0";
			Account = get(Key + "_account");
			string text = Secret.Unprotect(get(Key + "_token"));
			string[] array = text.Split('\n');
			if (array.Length >= 3)
			{
				access = array[0];
				refresh = array[1];
				long result;
				expires = (long.TryParse(array[2], out result) ? new DateTime(result, DateTimeKind.Utc) : DateTime.MinValue);
			}
			string text2 = get(Key + "_cat_id");
			string text3 = get(Key + "_cat_name");
			if (text2.Length > 0 && text3.Length > 0)
			{
				Chosen = new Category(text2, text3);
			}
		}

		public void Save(List<string> lines)
		{
			lines.Add(Key + "_client=" + ClientId);
			lines.Add(Key + "_secret=" + Secret.Protect(ClientSecret));
			lines.Add(Key + "_on=" + (Enabled ? "1" : "0"));
			lines.Add(Key + "_account=" + Account.Replace("\n", " "));
			lines.Add(Key + "_token=" + (Connected ? Secret.Protect(access + "\n" + refresh + "\n" + expires.Ticks) : ""));
			lines.Add(Key + "_cat_id=" + ((Chosen != null) ? Chosen.Id : ""));
			lines.Add(Key + "_cat_name=" + ((Chosen != null) ? Chosen.Name.Replace("\n", " ") : ""));
		}

		public static Category Best(List<Category> list, string q)
		{
			if (list.Count == 0)
			{
				return null;
			}
			string text = Norm(q);
			foreach (Category item in list)
			{
				if (Norm(item.Name) == text)
				{
					return item;
				}
			}
			foreach (Category item2 in list)
			{
				if (Norm(item2.Name).StartsWith(text))
				{
					return item2;
				}
			}
			return list[0];
		}

		public static string Norm(string s)
		{
			StringBuilder stringBuilder = new StringBuilder();
			string text = (s ?? "").ToLowerInvariant();
			foreach (char c in text)
			{
				if (char.IsLetterOrDigit(c))
				{
					stringBuilder.Append(c);
				}
			}
			return stringBuilder.ToString();
		}

		protected Dictionary<string, object> PollDevice(ILoginUi ui, string tokenUrl, string[] form, int interval, int expiresIn)
		{
			DateTime dateTime = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
			int num = Math.Max(2, interval);
			while (DateTime.UtcNow < dateTime)
			{
				for (int i = 0; i < num * 10; i++)
				{
					if (ui.Cancelled)
					{
						throw new OperationCanceledException();
					}
					Thread.Sleep(100);
				}
				try
				{
					return Http.Send("POST", tokenUrl, null, null, "application/x-www-form-urlencoded", Http.Form(form));
				}
				catch (ApiException ex)
				{
					string text = ex.Message.ToLowerInvariant();
					if (text.Contains("authorization_pending") || text.Contains("pending"))
					{
						continue;
					}
					if (text.Contains("slow_down"))
					{
						num += 5;
						continue;
					}
					if (text.Contains("denied"))
					{
						throw new ApiException(ex.Status, "has rechazado el acceso");
					}
					if (text.Contains("expired"))
					{
						throw new ApiException(ex.Status, "el código ha caducado, vuelve a intentarlo");
					}
					throw;
				}
			}
			throw new ApiException(0, "el código ha caducado, vuelve a intentarlo");
		}
	}
}
