using SharpMUSH.Database.SurrealDB;
using SurrealDb.Net.Models;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins how <see cref="SurrealDatabase.ExpandParameters"/> inlines <c>$name</c> parameters into SurrealQL:
/// quoted literals in the statement body, raw text inside a <c>⟨…⟩</c> record id, longest name first, and
/// never a second pass over a value that was just substituted.
/// </summary>
public class SurrealParameterExpansionTests
{
	private static string Expand(string query, params (string Name, object? Value)[] parameters)
		=> SurrealDatabase.ExpandParameters(query, parameters.ToDictionary(p => p.Name, p => p.Value));

	[Test]
	public async Task StringsAreQuotedAndEscaped_OutsideARecordId()
	{
		var expanded = Expand("UPDATE object:$key SET name = $name", ("key", 5), ("name", "it's \\ done"));
		await Assert.That(expanded).IsEqualTo("UPDATE object:5 SET name = 'it\\'s \\\\ done'");
	}

	[Test]
	public async Task StringsAreRaw_InsideARecordId()
	{
		var expanded = Expand("SELECT * FROM attribute:⟨$key⟩ WHERE name = $key", ("key", "5_FOO`BAR"));
		await Assert.That(expanded).IsEqualTo("SELECT * FROM attribute:⟨5_FOO`BAR⟩ WHERE name = '5_FOO`BAR'");
	}

	[Test]
	public async Task TheLongerOfTwoPrefixedNamesWins()
	{
		var expanded = Expand("$key $keyName $key", ("key", 1), ("keyName", "n"));
		await Assert.That(expanded).IsEqualTo("1 'n' 1");
	}

	[Test]
	public async Task ASubstitutedValueIsNotRescanned()
	{
		// A MUSH attribute value routinely starts with `$` ("$key *:@emit ...") and would otherwise be
		// spliced with whichever parameter happened to share the name.
		var expanded = Expand("UPDATE attribute:⟨$key⟩ SET value = $value", ("key", "5_CMD"), ("value", "$key *:@emit hi"));
		await Assert.That(expanded).IsEqualTo("UPDATE attribute:⟨5_CMD⟩ SET value = '$key *:@emit hi'");
	}

	[Test]
	public async Task SurrealQlsOwnVariablesAreLeftAlone()
	{
		var expanded = Expand("LET $candidates = (SELECT VALUE in FROM has_home WHERE out = room:$key); SELECT * FROM $parent.ids",
			("key", 7));
		await Assert.That(expanded).IsEqualTo("LET $candidates = (SELECT VALUE in FROM has_home WHERE out = room:7); SELECT * FROM $parent.ids");
	}

	[Test]
	public async Task NullArraysAndRecordIdsSerializeAsSurrealQlValues()
	{
		var expanded = Expand("$none $tags $id $flag",
			("none", null), ("tags", new[] { "a", "b" }), ("id", new StringRecordId("account:3")), ("flag", true));
		await Assert.That(expanded).IsEqualTo("NONE ['a', 'b'] account:3 true");
	}

	[Test]
	public async Task AQueryWithoutParametersIsReturnedAsIs()
	{
		const string query = "SELECT * FROM object WHERE type = 'PLAYER'";
		await Assert.That(Expand(query)).IsSameReferenceAs(query);
	}
}
