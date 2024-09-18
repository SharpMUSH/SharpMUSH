using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class DBRefExtensions
{
	public static AnyOptionalSharpObject Get(this DBRef dbref, ISharpDatabase db) 
		=> db.GetObjectNode(dbref);

	public static AnyOptionalSharpContainer GetLocation(this DBRef dbref, ISharpDatabase db) 
		=> db.GetLocationAsync(dbref, 1).Result;

	public static async ValueTask<AnyOptionalSharpObject> GetAsync(this DBRef dbref, ISharpDatabase db) 
		=> await db.GetObjectNodeAsync(dbref);

	public static async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(this DBRef dbref, ISharpDatabase db) 
		=> await db.GetLocationAsync(dbref, 1);
}