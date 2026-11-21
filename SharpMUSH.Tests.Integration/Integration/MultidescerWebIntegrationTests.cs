using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The routes the portal's Descriptions page reads and posts to, on the HTTP handler, called the way the portal
/// calls them: the schema and data for a sub-path, and the act action.
/// </summary>
public partial class MultidescerIntegrationTests
{
	private async Task<string> Objid(TestIsolationHelpers.TestPlayer player) => await God($"think [objid(#{player.DbRef.Number})]");

	private async Task<JsonElement> Http(string method, string path, string body, TestIsolationHelpers.TestPlayer? viewer)
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		DBRef? who = viewer is null ? null : DBRef.Parse(await Objid(viewer));
		var response = (await dispatcher.DispatchAsync(method, path, body, [], IHttpHandlerCommandDispatcher.UnknownAddress, who))
			.Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
		await Assert.That(response.ContentType).StartsWith("application/json").Because(response.Body);
		await WebAppFactoryArg.QueueBarrierAsync();
		return JsonDocument.Parse(response.Body).RootElement.Clone();
	}

	private Task<JsonElement> Act(TestIsolationHelpers.TestPlayer viewer, object body) =>
		Http("POST", "/descs/act", JsonSerializer.Serialize(body), viewer);

	private static JsonElement[] Rows(JsonElement data, string field) =>
		data.GetProperty("fields").GetProperty(field).GetProperty("value").EnumerateArray().ToArray();

	private static string Value(JsonElement data, string field) =>
		data.GetProperty("fields").GetProperty(field).GetProperty("value").ToString();

	private static string Field(JsonElement row, string name) =>
		row.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString() : string.Empty;

	private static JsonElement[] Elements(JsonElement schema) =>
		schema.GetProperty("pages")[0].GetProperty("sections").EnumerateArray()
			.SelectMany(s => s.GetProperty("elements").EnumerateArray()).ToArray();

	private static bool Ok(JsonElement answer) => answer.GetProperty("ok").GetBoolean();

	private static string Error(JsonElement answer) => answer.GetProperty("errors").GetProperty("_global").GetString()!;

	/// <summary>The id of the piece or outfit <paramref name="name"/> in the list's <paramref name="field"/> table.</summary>
	private async Task<string> IdOf(TestIsolationHelpers.TestPlayer viewer, string field, string name) =>
		Field(Rows(await Http("GET", "/descs/data?at=", "", viewer), field).Single(r => Field(r, "name") == name), "id");

	[Test]
	public async Task ThePortalPageSavesWearsChangesAndDeletes()
	{
		try
		{
			await InstallAsync();
			var fay = await Player("MdFay", await RoomAsync("MdWeb"));
			var n = fay.DbRef.Number;

			// The list, before anything is saved.
			var main = await Http("GET", "/descs/schema?at=", "", fay);
			await Assert.That(main.GetProperty("title").GetString()).IsEmpty();
			await Assert.That(Elements(main).Any(e => Field(e, "key") == "new_text")).IsTrue();
			await Assert.That(Rows(await Http("GET", "/descs/data?at=", "", fay), "pieces")).IsEmpty();

			// Text written on the page is kept as written: the brackets stay text.
			var saved = await Act(fay, new { op = "save", target = "", new_name = "winter", new_slot = "whole", new_text = "A coat [add(1,2)]", new_code = false });
			await Assert.That(Ok(saved)).IsTrue();
			await Assert.That(saved.GetProperty("message").GetString()).Contains("Saved the description 'winter'.");
			var winter = await IdOf(fay, "pieces", "winter");
			var piece = await Http("GET", $"/descs/data?at=piece/{winter}", "", fay);
			await Assert.That(Value(piece, "text")).IsEqualTo("A coat [add(1,2)]");
			await Assert.That(Value(piece, "code")).IsEqualTo("False");

			// A hand-written description is kept before the first wear, as with the commands.
			await God($"&DESCRIBE #{n}=Written by [lcstr(HAND)]");
			var worn = await Act(fay, new { op = "only", target = $"piece/{winter}" });
			await Assert.That(worn.GetProperty("message").GetString()).Contains("Your description is saved as 'original'.");
			await Assert.That(await Described(fay)).IsEqualTo("A coat [add(1,2)]");
			await Assert.That(Rows(await Http("GET", "/descs/data?at=", "", fay), "pieces").Select(r => Field(r, "name"))).Contains("original");

			// A kept description is code: the page shows it as written, with its code box ticked.
			var original = await Http("GET", $"/descs/data?at=piece/{await IdOf(fay, "pieces", "original")}", "", fay);
			await Assert.That(Value(original, "text")).IsEqualTo("Written by [lcstr(HAND)]");
			await Assert.That(Value(original, "code")).IsEqualTo("True");

			// A piece ticked as code runs when someone looks.
			await God($"&MOOD #{n}=calm");
			await Act(fay, new { op = "save", target = "", new_name = "moody", new_slot = "face", new_text = "Mood [v(MOOD)]", new_code = true });
			var moody = await IdOf(fay, "pieces", "moody");
			await Act(fay, new { op = "wear", target = $"piece/{moody}", noisy = false });
			await Assert.That(await Described(fay)).IsEqualTo("A coat [add(1,2)]\n\nMood calm");
			await Assert.That(Value(await Http("GET", $"/descs/data?at=piece/{moody}", "", fay), "text")).IsEqualTo("Mood [v(MOOD)]");

			// The page shows what is worn, evaluated as look would.
			var moodySchema = await Http("GET", $"/descs/schema?at=piece/{moody}", "", fay);
			await Assert.That(moodySchema.GetProperty("title").GetString()).IsEqualTo("moody");
			await Assert.That(Elements(moodySchema).Any(e => Field(e, "label") == "Take it off")).IsTrue();

			// A change: new text, a new slot and a new name, at once.
			var changed = await Act(fay, new { op = "update", target = $"piece/{winter}", name = "summer", slot = "whole", text = "Thin cotton", code = false });
			await Assert.That(Ok(changed)).IsTrue().Because(changed.ToString());
			await Assert.That(await Described(fay)).IsEqualTo("Thin cotton\n\nMood calm");
			await Assert.That(changed.GetProperty("schema").GetProperty("title").GetString()).IsEqualTo("summer");

			// A refusal comes back as the reason, and nothing changes.
			var taken = await Act(fay, new { op = "save", target = "", new_name = "summer", new_slot = "whole", new_text = "again", new_code = false });
			await Assert.That(Ok(taken)).IsFalse();
			await Assert.That(Error(taken)).Contains("You already have a piece called 'summer'.");
			var noSlot = await Act(fay, new { op = "save", target = "", new_name = "cape", new_slot = "cloak", new_text = "x", new_code = false });
			await Assert.That(Error(noSlot)).Contains("There is no slot called 'cloak'.");
			var unknown = await Act(fay, new { op = "wipe", target = "" });
			await Assert.That(Error(unknown)).IsEqualTo("This page cannot do that.");

			// Outfits: what is worn now, saved, and worn again.
			await Act(fay, new { op = "outfit", target = "", outfit_name = "day" });
			var day = await IdOf(fay, "outfits", "day");
			await Act(fay, new { op = "removeall", target = "" });
			await Assert.That(await Described(fay)).IsEqualTo(string.Empty);
			await Act(fay, new { op = "only", target = $"outfit/{day}" });
			await Assert.That(await Described(fay)).IsEqualTo("Thin cotton\n\nMood calm");

			// Deleting goes back to the list; a deleted piece's page is the list again.
			var deleted = await Act(fay, new { op = "delete", target = $"piece/{moody}" });
			await Assert.That(deleted.GetProperty("redirect").GetString()).IsEqualTo("/apps/descs");
			await Assert.That(await Described(fay)).IsEqualTo("Thin cotton");
			var gone = await Act(fay, new { op = "wear", target = $"piece/{moody}" });
			await Assert.That(Error(gone)).Contains("That piece or outfit is gone.");

			// Opened by its old address, the page says the piece is not there and shows the list.
			var goneSchema = await Http("GET", $"/descs/schema?at=piece/{moody}", "", fay);
			await Assert.That(Elements(goneSchema).Any(e => Field(e, "value").Contains("That piece or outfit is not here."))).IsTrue();
			await Assert.That(Elements(goneSchema).Any(e => Field(e, "key") == "new_text")).IsTrue();
			await Assert.That(Elements(main).Any(e => Field(e, "value").Contains("is not here"))).IsFalse();

			// The short description.
			await Act(fay, new { op = "short", target = "", shortdesc = "Tall, in grey" });
			await Assert.That(await Eval($"get(#{n}/SHORTDESC)")).IsEqualTo("Tall, in grey");
			await Assert.That(Value(await Http("GET", "/descs/data?at=", "", fay), "shortdesc")).IsEqualTo("Tall, in grey");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ThePortalPageAsksForASignIn()
	{
		try
		{
			await InstallAsync();
			var schema = await Http("GET", "/descs/schema?at=", "", null);
			await Assert.That(schema.GetProperty("kind").GetString()).IsEqualTo("view");
			await Assert.That(schema.ToString()).Contains("Sign in with a character");
			var refused = await Http("POST", "/descs/act", JsonSerializer.Serialize(new { op = "removeall", target = "" }), null);
			await Assert.That(refused.GetProperty("ok").GetBoolean()).IsFalse();
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
