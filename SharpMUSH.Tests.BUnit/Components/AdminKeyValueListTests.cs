using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The list behind the sitelock, banned-name and restriction config pages. Those four lists (the
/// restrictions page holds two) were the same load/add/delete triple written out four times; these
/// tests are what that duplication never had.
/// </summary>
public class AdminKeyValueListTests : TrackingBunitContext
{
	private static AdminKeyValueListText TextWithValues() => new(
		AddTitle: "Add rule",
		KeyLabel: "Host pattern",
		KeyPlaceholder: "*.example.com",
		AddButton: "Add",
		ListTitle: "Configured rules",
		Empty: "Nothing configured.",
		Added: key => $"added {key}",
		Removed: key => $"removed {key}",
		LoadFailed: failure => $"load failed: {failure.Message}",
		AddFailed: (key, failure) => $"add {key} failed: {failure.Message}",
		RemoveFailed: (key, failure) => $"remove {key} failed: {failure.Message}")
	{
		ValuesLabel = "Access rules",
		ValuesPlaceholder = "!connect, register",
		ValuesRequired = "At least one rule.",
	};

	private static AdminKeyValueListText KeyOnlyText() => TextWithValues() with
	{
		ValuesLabel = null,
		ValuesPlaceholder = null,
		ValuesRequired = null,
	};

	private void Register()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static Task<ApiResult<IReadOnlyDictionary<string, string[]>>> Entries(
		params (string Key, string[] Values)[] rows) =>
		Task.FromResult<ApiResult<IReadOnlyDictionary<string, string[]>>>(
			(IReadOnlyDictionary<string, string[]>)rows.ToDictionary(r => r.Key, r => r.Values));

	private IRenderedComponent<AdminKeyValueList> Render(
		AdminKeyValueListText text,
		Func<Task<ApiResult<IReadOnlyDictionary<string, string[]>>>> load,
		Func<string, string[], Task<ApiResult<Success>>>? add = null,
		Func<string, Task<ApiResult<Success>>>? delete = null)
	{
		Register();
		return Render<AdminKeyValueList>(parameters => parameters
			.Add(p => p.Text, text)
			.Add(p => p.Load, load)
			.Add(p => p.Add, add ?? ((_, _) => Task.FromResult<ApiResult<Success>>(new Success())))
			.Add(p => p.Delete, delete ?? (_ => Task.FromResult<ApiResult<Success>>(new Success()))));
	}

	[Test]
	public async Task EntriesRenderSortedWithTheirValues()
	{
		var component = Render(TextWithValues(),
			() => Entries(("zeta.example", ["register"]), ("alpha.example", ["!connect", "!create"])));

		var names = component.FindAll(".config-list-name").Select(n => n.TextContent).ToList();
		await Assert.That(names).IsEquivalentTo(new[] { "alpha.example", "zeta.example" });
		await Assert.That(component.Find(".config-list-sub").TextContent).IsEqualTo("!connect, !create");
	}

	[Test]
	public async Task AnEmptyListShowsItsOwnEmptyText()
	{
		var component = Render(TextWithValues(), () => Entries());

		await Assert.That(component.Find(".config-empty").TextContent).IsEqualTo("Nothing configured.");
	}

	/// <summary>
	/// The values input is what tells a key-only list from a key/value one, so a banned-name list
	/// must not render one — there is nothing to put in it and no label for it.
	/// </summary>
	[Test]
	public async Task AKeyOnlyListRendersNoValuesField()
	{
		var component = Render(KeyOnlyText(), () => Entries(("Vader", [])));

		await Assert.That(component.FindAll("input").Count).IsEqualTo(1);
		await Assert.That(component.FindAll(".config-list-sub")).IsEmpty();
	}

	[Test]
	public async Task AddSplitsTheValueListAndReloads()
	{
		string? addedKey = null;
		string[]? addedValues = null;
		var loads = 0;

		var component = Render(TextWithValues(),
			() => { loads++; return Entries(); },
			(key, values) =>
			{
				addedKey = key;
				addedValues = values;
				return Task.FromResult<ApiResult<Success>>(new Success());
			});

		component.FindAll("input")[0].Input("  *.example.com  ");
		component.FindAll("input")[1].Input("!connect , register ,");
		component.Find("button.config-primary-btn").Click();

		await Assert.That(addedKey).IsEqualTo("*.example.com");
		await Assert.That(addedValues).IsEquivalentTo(new[] { "!connect", "register" });
		await Assert.That(loads).IsEqualTo(2).Because("the list reloads after a successful add");
	}

	/// <summary>
	/// A failure keeps the typed text so the operator can correct it rather than retype it, and the
	/// list is not reloaded — nothing changed server-side.
	/// </summary>
	[Test]
	public async Task AFailedAddKeepsTheInputAndDoesNotReload()
	{
		var loads = 0;
		var component = Render(TextWithValues(),
			() => { loads++; return Entries(); },
			(_, _) => Task.FromResult<ApiResult<Success>>(
				new ApiFailure(ApiFailureKind.Forbidden, "Permission denied.")));

		component.FindAll("input")[0].Input("*.example.com");
		component.FindAll("input")[1].Input("!connect");
		component.Find("button.config-primary-btn").Click();

		await Assert.That(loads).IsEqualTo(1);
		await Assert.That(component.FindAll("input")[0].GetAttribute("value")).IsEqualTo("*.example.com");
		await Assert.That(Services.GetRequiredService<ISnackbar>().ShownSnackbars
			.Any(s => s.Severity == Severity.Error)).IsTrue();
	}

	[Test]
	public async Task DeletePassesTheRowKeyAndReloads()
	{
		string? deleted = null;
		var loads = 0;

		var component = Render(TextWithValues(),
			() => { loads++; return Entries(("alpha.example", ["!connect"])); },
			delete: key =>
			{
				deleted = key;
				return Task.FromResult<ApiResult<Success>>(new Success());
			});

		component.Find("button.config-icon-btn--danger").Click();

		await Assert.That(deleted).IsEqualTo("alpha.example");
		await Assert.That(loads).IsEqualTo(2);
	}

	/// <summary>
	/// The reason a failed read carries an <see cref="ApiFailure"/> rather than a bool: the page's
	/// message is built from the server's own text, which is what the bool-returning services
	/// discarded.
	/// </summary>
	[Test]
	public async Task AFailedLoadReportsTheServersOwnReason()
	{
		var component = Render(TextWithValues(),
			() => Task.FromResult<ApiResult<IReadOnlyDictionary<string, string[]>>>(
				new ApiFailure(ApiFailureKind.Unauthenticated, "Your session has expired.")));

		await Assert.That(component.Find(".config-empty").TextContent).IsEqualTo("Nothing configured.");
		await Assert.That(Services.GetRequiredService<ISnackbar>().ShownSnackbars
			.Any(s => s.Message?.Contains("Your session has expired.") == true)).IsTrue();
	}

	/// <summary>A value list that parses to nothing is refused before it reaches the server.</summary>
	[Test]
	public async Task AValueListOfOnlySeparatorsIsRefusedLocally()
	{
		var calls = 0;
		var component = Render(TextWithValues(), () => Entries(),
			(_, _) => { calls++; return Task.FromResult<ApiResult<Success>>(new Success()); });

		component.FindAll("input")[0].Input("*.example.com");
		component.FindAll("input")[1].Input(" , , ");
		component.Find("button.config-primary-btn").Click();

		await Assert.That(calls).IsEqualTo(0);
		await Assert.That(Services.GetRequiredService<ISnackbar>().ShownSnackbars
			.Any(s => s.Severity == Severity.Warning)).IsTrue();
	}
}
