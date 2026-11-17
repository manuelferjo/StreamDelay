// Categoría o juego de una plataforma (su id y su nombre).
namespace StreamDelay
{
	internal class Category
	{
		public string Id;

		public string Name;

		public Category(string id, string name)
		{
			Id = id;
			Name = name;
		}

		public override string ToString()
		{
			return Name;
		}
	}
}
