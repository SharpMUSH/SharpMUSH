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

			// The slot board names what is worn in each slot, and the table how each piece reads.
			var board = Rows(await Http("GET", "/descs/data?at=", "", fay), "slots");
			var face = board.Single(r => Field(r, "label") == "face");
			await Assert.That(Field(face, "value")).IsEqualTo("moody");
			await Assert.That(Field(face, "color")).IsEqualTo("success");
			await Assert.That(Field(board.First(), "label")).IsEqualTo("whole description");
			await Assert.That(board.Select(r => Field(r, "label"))).Contains("body");
			// outer hides top, so top is drawn under outer, in one stack named for it.
			var labels = board.Select(r => Field(r, "label")).ToList();
			await Assert.That(labels[labels.IndexOf("outer") + 1]).IsEqualTo("top");
			await Assert.That(board.Where(r => Field(r, "stack") == "outer").Select(r => Field(r, "label"))).IsEquivalentTo(["outer", "top"]);
			await Assert.That(Field(face, "stack")).IsEqualTo("");
			var slotOptions = Elements(main).Single(e => Field(e, "key") == "new_slot").GetProperty("options").EnumerateArray().Select(o => Field(o, "label")).ToArray();
			await Assert.That(slotOptions).Contains("body");
			await Assert.That(slotOptions).Contains("outer · hides top · one at a time");
			await Assert.That(Field(board.Single(r => Field(r, "label") == "outer"), "caption")).Contains("hides top");
			var moodyRow = Rows(await Http("GET", "/descs/data?at=", "", fay), "pieces").Single(r => Field(r, "name") == "moody");
			await Assert.That(Field(moodyRow, "looks")).IsEqualTo("Mood calm");

			// A piece's page says where it goes and whether it is on.
			var facts = Rows(await Http("GET", $"/descs/data?at=piece/{moody}", "", fay), "facts");
			await Assert.That(facts.Select(f => Field(f, "value"))).IsEquivalentTo(["face", "worn", "0"]);

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

			// A change whose new name is taken changes nothing, not even the text.
			var clash = await Act(fay, new { op = "update", target = $"piece/{winter}", name = "original", slot = "whole", text = "Wool", code = false });
			await Assert.That(Error(clash)).Contains("You already have something called 'original'.");
			await Assert.That(Value(await Http("GET", $"/descs/data?at=piece/{winter}", "", fay), "text")).IsEqualTo("Thin cotton");
			await Assert.That(await Described(fay)).IsEqualTo("Thin cotton\n\nMood calm");

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

			// A deleted id is not given out again, so a page left open on the deleted piece cannot reach a new one.
			await Act(fay, new { op = "save", target = "", new_name = "scarf", new_slot = "face", new_text = "A scarf", new_code = false });
			await Assert.That(await IdOf(fay, "pieces", "scarf")).IsNotEqualTo(moody);
			await Assert.That(Error(await Act(fay, new { op = "delete", target = $"piece/{moody}" }))).Contains("That piece or outfit is gone.");

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

			// The guide is open to anyone, and its example follows the game's slots.
			var guide = await Http("GET", "/descs/schema?at=guide", "", null);
			var pages = guide.GetProperty("pages").EnumerateArray().ToArray();
			await Assert.That(pages.Select(p => p.GetProperty("title").GetString()!)).IsEquivalentTo(["Pieces", "Slots", "Hiding and limits", "Wearing and outfits"]);
			await Assert.That(pages[0].ToString()).Contains("Tall and lean, with salt-weathered hands. Grey eyes, steady and patient.");
			await Assert.That(pages[0].ToString()).DoesNotContain("oilskin");
			await Assert.That(pages[2].ToString()).Contains("**shirt** stays on");
			await Assert.That(pages[2].ToString()).DoesNotContain("A linen shirt");
			var guideData = await Http("GET", "/descs/data?at=guide", "", null);
			await Assert.That(Rows(guideData, "guide_example").Select(r => Field(r, "name"))).DoesNotContain("oilskin");
			var outer = Rows(guideData, "guide_slots").Single(r => Field(r, "slot") == "outer");
			await Assert.That(Field(outer, "hides")).IsEqualTo("top");
			await Assert.That(Field(outer, "group")).IsEqualTo("clothes paragraph");
			await Assert.That(Field(outer, "max")).IsEqualTo("1");
			await Assert.That(Field(Rows(guideData, "guide_slots").Single(r => Field(r, "slot") == "top"), "max")).IsEqualTo("none");
		}
		finally
		{
			await UninstallAsync();
		}
	}
	[Test]
	public async Task ThePortalSlotsPageMovesAddsChangesAndResets()
	{
		try
		{
			await InstallAsync();
			var hal = await Player("MdHal", await RoomAsync("MdWebSlots"));
			var n = hal.DbRef.Number;

			// The game's slots, whole first in a group of its own and the rest in one that moves.
			var schema = await Http("GET", "/descs/schema?at=slots", "", hal);
			await Assert.That(schema.ToString()).Contains("You use the game's slots.");
			var rows = Rows(await Http("GET", "/descs/data?at=slots", "", hal), "slot_rows");
			await Assert.That(Field(rows.First(), "id")).IsEqualTo("whole");
			await Assert.That(Field(rows.First(), "group")).IsEqualTo("Comes first");
			await Assert.That(rows.Skip(1).All(r => Field(r, "group") == "In this order")).IsTrue();

			// Moving face to the first place after whole gives the player their own list.
			var moved = await Act(hal, new { op = "slotmove", target = "slots", item = "face", position = 1 });
			await Assert.That(Ok(moved)).IsTrue().Because(moved.ToString());
			await Assert.That(moved.GetProperty("message").GetString()).Contains("'face' now comes after 'whole'.");
			await Assert.That(Rows(moved.GetProperty("data"), "slot_rows").Select(r => Field(r, "id")).Take(3)).IsEquivalentTo(["whole", "face", "body"]);
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT)")).IsEqualTo("1");
			await Assert.That((await Http("GET", "/descs/schema?at=slots", "", hal)).ToString()).Contains("These are your own slots");

			// A new slot.
			var added = await Act(hal, new { op = "addslot", target = "slots", new_slot_name = "Scars", new_slot_after = "face" });
			await Assert.That(added.GetProperty("message").GetString()).Contains("Added the slot 'scars' after 'face'");

			// A slot's page saves what changed, checked together first.
			var page = await Http("GET", "/descs/data?at=slot/outer", "", hal);
			await Assert.That(Value(page, "slot_hides")).Contains("top");
			var refused = await Act(hal, new { op = "slotset", target = "slot/scars", slot_name = "scars", slot_after = "face", slot_paragraph = "body", slot_hides = new[] { "scars" }, slot_max = 500 });
			await Assert.That(Ok(refused)).IsFalse();
			await Assert.That(Error(refused)).Contains("A slot cannot hide itself.").And.Contains("Max is a number from 0 to 100");
			var set = await Act(hal, new { op = "slotset", target = "slot/scars", slot_name = "marks", slot_after = "face", slot_paragraph = "body", slot_hides = new[] { "hair" }, slot_max = 2 });
			await Assert.That(Ok(set)).IsTrue().Because(set.ToString());
			await Assert.That(set.GetProperty("redirect").GetString()).IsEqualTo("/apps/descs/slot/marks");
			await Assert.That(await Eval($"get(#{n}/MULTIDESC`SLOT`MARKS`COVERS)/[get(#{n}/MULTIDESC`SLOT`MARKS`MAX)]")).IsEqualTo("hair/2");
			var unchanged = await Act(hal, new { op = "slotset", target = "slot/marks", slot_name = "marks", slot_after = "face", slot_paragraph = "body", slot_hides = new[] { "hair" }, slot_max = 2 });
			await Assert.That(unchanged.GetProperty("message").GetString()).Contains("Nothing was changed.");

			// A slot that is gone, and taking one away.
			await Assert.That((await Http("GET", "/descs/schema?at=slot/scars", "", hal)).ToString()).Contains("That slot is not here.");
			var deleted = await Act(hal, new { op = "delslot", target = "slot/marks" });
			await Assert.That(deleted.GetProperty("redirect").GetString()).IsEqualTo("/apps/descs/slots");
			await Assert.That(Error(await Act(hal, new { op = "delslot", target = "slot/whole" }))).Contains("The whole description slot stays");

			// Back to the game's.
			var reset = await Act(hal, new { op = "resetslots", target = "slots" });
			await Assert.That(reset.GetProperty("message").GetString()).Contains("You use the game's slots again.");
			await Assert.That(await Eval($"hasattr(#{n},MULTIDESC`SLOT)")).IsEqualTo("0");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
