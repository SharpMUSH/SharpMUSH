using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Every function a <c>mush-defs.json</c> entry sends the reader to has to exist. The portal's
/// softcode editor reads that file for its function drawer, and nothing generates it, so a
/// cross-reference in it survives the function it names being renamed or deleted — <c>oob()</c>'s
/// "See Also" pointed at <c>wsjson()</c> for as long as it took someone to notice.
/// </summary>
/// <remarks>
/// Only the <c>[name()]</c> form is checked. <c>[@NAME]</c> is a reference to a <em>help topic</em>,
/// not to a command: of the 151 distinct ones in the file, most resolve to topic continuations
/// (<c>@attribute2</c>, <c>@break2</c>) or to attribute names (<c>@afollow</c>, <c>@idescribe</c>),
/// none of which is in the command registry. Checking them wants a help index, not this test.
/// </remarks>
public partial class MushDefsCrossReferenceTests
{
	/// <summary>A cross-reference in the editor's data file: <c>[name()]</c>.</summary>
	[GeneratedRegex(@"\[(?<Name>[A-Za-z_][A-Za-z_0-9]*)\(\)\]")]
	private static partial Regex FunctionReference();

	private static string DefinitionsFile =>
		Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Client", "wwwroot", "data", "mush-defs.json");

	[Test]
	public async Task EveryFunctionCrossReferenceNamesARegisteredFunction()
	{
		// The aliases are registered names too: hostname(), mod(), pickrand(), replace(), stats() and
		// u() are each referenced by name and each exist only as a shipped alias.
		var registered = RegistryInventory.FunctionNamesWithAliases();

		var dangling = References()
			.Where(name => !registered.Contains(name))
			.Order(StringComparer.OrdinalIgnoreCase)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(dangling).IsEmpty();
	}

	/// <summary>The file has to be readable and have references at all, or the check above is vacuous.</summary>
	[Test]
	public async Task TheDefinitionsFileCarriesCrossReferences()
		=> await Assert.That(References().Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsGreaterThan(100);

	private static IEnumerable<string> References()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(DefinitionsFile));

		return Strings(document.RootElement)
			.SelectMany(text => FunctionReference().Matches(text).Select(match => match.Groups["Name"].Value))
			.ToList();
	}

	private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => [element.GetString()!],
		JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Strings(property.Value)),
		JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
		_ => []
	};
}
