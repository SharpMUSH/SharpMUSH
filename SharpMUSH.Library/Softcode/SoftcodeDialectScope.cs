namespace SharpMUSH.Library.Softcode;

/// <summary>
/// The dialect of the built-in function or command running now, for the attribute names it reads and writes. The
/// parser enters it around each built-in it calls (<see cref="SoftcodeDialects.InForce"/>), so <c>get(%#/kill)</c> in
/// TinyMUX code reads <c>DEATH</c>, and the attribute service, which sees a name and not the code it came from,
/// resolves the name by it (<see cref="AttributeName"/>).
/// </summary>
public static class SoftcodeDialectScope
{
	private static readonly AsyncLocal<SoftcodeDialect?> Ambient = new();

	/// <summary>The dialect in force for the running built-in, or <c>null</c> for SharpMUSH's own.</summary>
	public static SoftcodeDialect? Current => Ambient.Value;

	/// <summary>Puts <paramref name="dialect"/> in force until the returned scope is disposed.</summary>
	public static Scope Enter(SoftcodeDialect? dialect)
	{
		var previous = Ambient.Value;
		if (ReferenceEquals(previous, dialect))
		{
			return default;
		}

		Ambient.Value = dialect;
		return new Scope(previous, true);
	}

	/// <summary>The SharpMUSH attribute <paramref name="name"/> means in the dialect in force: its alias's, else itself.</summary>
	public static string AttributeName(string name)
		=> Ambient.Value?.AttributeFor(name) ?? name;

	/// <summary>Restores the dialect that was in force before <see cref="Enter"/>.</summary>
	public readonly struct Scope(SoftcodeDialect? previous, bool entered) : IDisposable
	{
		public void Dispose()
		{
			if (entered)
			{
				Ambient.Value = previous;
			}
		}
	}
}
