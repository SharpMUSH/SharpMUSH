using OneOf;
using SharpMUSH.Database.Types;

namespace SharpMUSH.Database
{
	public interface ISharpDatabase
	{
		Task Migrate();

		Task<int> CreatePlayer(string name, string passwordHash, OneOf<SharpPlayer, SharpRoom, SharpThing> location);

		Task<int> CreateRoom(string name);

		Task<int> CreateThing(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> location);

		Task<int> CreateExit(string name, OneOf<SharpPlayer, SharpRoom, SharpThing> source);

		Task<SharpAttribute[]?> GetAttribute(int dbref, string[] attribute);

		Task<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>?> GetObjectNode(int dbref, int? createdsecs = null, int? createdmsecs = null);

	}
}