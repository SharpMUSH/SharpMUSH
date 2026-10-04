using Microsoft.AspNetCore.Components;
using MudBlazor;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The list-and-detail flow behind the roles and applications admin pages: load, keep the selection
/// across reloads, edit through a dialog, confirm a delete. Each page used to write this out by hand.
/// </summary>
public class AdminRecordListTests
{
	private sealed record Row(string Key, int Version = 0);

	private sealed class EditDialog : ComponentBase;

	private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
	private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();
	private readonly List<Row> _server = [new("a"), new("b"), new("c")];
	private int _fetches;

	private AdminRecordList<Row> Create(AdminReselect reselect = AdminReselect.Nothing, Action<Row?>? changed = null) =>
		new(_dialogs, _snackbar, FetchAsync, row => row.Key, reselect) { SelectionChanged = changed };

	private Task<IEnumerable<Row>> FetchAsync()
	{
		_fetches++;
		return Task.FromResult<IEnumerable<Row>>([.. _server]);
	}

	private void ServerHas(params Row[] rows)
	{
		_server.Clear();
		_server.AddRange(rows);
	}

	private static AdminDeleteText Text(string key) => new(
		"Delete?", $"Delete {key}?", "Delete", "Cancel", $"deleted {key}", failure => $"failed: {failure.Message}");

	private void DialogCloses(DialogResult? result)
	{
		var reference = Substitute.For<IDialogReference>();
		reference.Result.Returns(Task.FromResult(result));
		_dialogs.ShowAsync<EditDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
			.Returns(Task.FromResult(reference));
	}

	private void ConfirmAnswers(bool? answer) =>
		_dialogs.ShowMessageBoxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DialogOptions>())
			.Returns(Task.FromResult(answer));

	[Test]
	public async Task IsLoadingUntilTheFirstLoadCompletes()
	{
		var list = Create();
		await Assert.That(list.Loading).IsTrue();

		await list.LoadAsync();

		await Assert.That(list.Loading).IsFalse();
		await Assert.That(list.Items.Select(r => r.Key)).IsEquivalentTo(["a", "b", "c"]);
	}

	[Test]
	public async Task Reload_KeepsTheSelectionOnTheFreshCopyOfTheSameRecord()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[1]);
		ServerHas(new("a"), new("b", Version: 2), new("c"));

		await list.LoadAsync();

		await Assert.That(list.Selected).IsEqualTo(new Row("b", 2));
	}

	[Test]
	public async Task Reload_WithNothingPolicy_DropsASelectionThatVanished_AndSelectsNothingByDefault()
	{
		var list = Create(AdminReselect.Nothing);
		await list.LoadAsync();
		await Assert.That(list.Selected).IsNull();

		list.Select(list.Items[1]);
		ServerHas(new("a"), new("c"));
		await list.LoadAsync();

		await Assert.That(list.Selected).IsNull();
	}

	[Test]
	public async Task Reload_WithFirstPolicy_FallsBackToTheFirstRecord()
	{
		var list = Create(AdminReselect.First);
		await list.LoadAsync();
		await Assert.That(list.Selected?.Key).IsEqualTo("a");

		list.Select(list.Items[2]);
		ServerHas(new("b"), new("a"));
		await list.LoadAsync();

		await Assert.That(list.Selected?.Key).IsEqualTo("b");
	}

	[Test]
	public async Task SelectionChanged_FiresOnSelect_AndAgainWithTheFreshRecordAfterAReload()
	{
		var seen = new List<Row?>();
		var list = Create(changed: seen.Add);
		await list.LoadAsync();
		list.Select(list.Items[0]);
		ServerHas(new Row("a", Version: 5));

		await list.LoadAsync();

		await Assert.That(seen).IsEquivalentTo(new Row?[] { null, new Row("a"), new Row("a", 5) });
	}

	[Test]
	public async Task IsSelected_ComparesByKey()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[0]);

		await Assert.That(list.IsSelected(new Row("a", 9))).IsTrue();
		await Assert.That(list.IsSelected(new Row("A"))).IsFalse();
	}

	[Test]
	public async Task Edit_Canceled_DoesNotReload()
	{
		var list = Create();
		await list.LoadAsync();
		DialogCloses(DialogResult.Cancel());

		var saved = await list.EditAsync<EditDialog>("New");

		await Assert.That(saved).IsFalse();
		await Assert.That(_fetches).IsEqualTo(1);
	}

	[Test]
	public async Task Edit_OpensTheDialogFullWidth_WithItsTitleAndParameters()
	{
		var list = Create();
		var parameters = new DialogParameters { { "Existing", "a" } };
		DialogCloses(DialogResult.Cancel());

		await list.EditAsync<EditDialog>("Edit a", parameters);

		await _dialogs.Received(1).ShowAsync<EditDialog>("Edit a", parameters,
			Arg.Is<DialogOptions>(o => o.FullWidth == true));
	}

	[Test]
	public async Task Edit_SavedWithAKey_ReloadsAndSelectsThatRecord()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[0]);
		ServerHas(new("a"), new("b"), new("c"), new("d"));
		DialogCloses(DialogResult.Ok("d"));

		var saved = await list.EditAsync<EditDialog>("New");

		await Assert.That(saved).IsTrue();
		await Assert.That(_fetches).IsEqualTo(2);
		await Assert.That(list.Selected?.Key).IsEqualTo("d");
	}

	[Test]
	public async Task Edit_SavedWithoutAKey_ReloadsAndKeepsTheSelection()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[1]);
		DialogCloses(DialogResult.Ok(true));

		await list.EditAsync<EditDialog>("Edit b");

		await Assert.That(_fetches).IsEqualTo(2);
		await Assert.That(list.Selected?.Key).IsEqualTo("b");
	}

	[Test]
	public async Task Delete_NotConfirmed_NeverCallsTheServer()
	{
		var list = Create();
		await list.LoadAsync();
		ConfirmAnswers(null);
		var called = false;

		var deleted = await list.DeleteAsync(list.Items[0], Text("a"), () =>
		{
			called = true;
			return Task.FromResult<ApiResult<Success>>(new Success());
		});

		await Assert.That(deleted).IsFalse();
		await Assert.That(called).IsFalse();
		await _dialogs.Received(1).ShowMessageBoxAsync("Delete?", "Delete a?", "Delete", Arg.Any<string>(), "Cancel", Arg.Any<DialogOptions>());
	}

	[Test]
	public async Task Delete_Succeeded_ReportsIt_DropsTheSelection_AndReloads()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[0]);
		ConfirmAnswers(true);
		ServerHas(new("b"), new("c"));

		var deleted = await list.DeleteAsync(list.Items[0], Text("a"),
			() => Task.FromResult<ApiResult<Success>>(new Success()));

		await Assert.That(deleted).IsTrue();
		_snackbar.Received(1).Add("deleted a", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
		await Assert.That(list.Selected).IsNull();
		await Assert.That(list.Items.Select(r => r.Key)).IsEquivalentTo(["b", "c"]);
	}

	[Test]
	public async Task Delete_OfAnUnselectedRecord_KeepsTheSelection()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[1]);
		ConfirmAnswers(true);

		await list.DeleteAsync(list.Items[0], Text("a"), () => Task.FromResult<ApiResult<Success>>(new Success()));

		await Assert.That(list.Selected?.Key).IsEqualTo("b");
	}

	[Test]
	public async Task Delete_Refused_ReportsWhy_AndNeitherReloadsNorDeselects()
	{
		var list = Create();
		await list.LoadAsync();
		list.Select(list.Items[0]);
		ConfirmAnswers(true);

		var deleted = await list.DeleteAsync(list.Items[0], Text("a"),
			() => Task.FromResult<ApiResult<Success>>(new ApiFailure(ApiFailureKind.Forbidden, "system role")));

		await Assert.That(deleted).IsFalse();
		_snackbar.Received(1).Add("failed: system role", Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
		await Assert.That(_fetches).IsEqualTo(1);
		await Assert.That(list.Selected?.Key).IsEqualTo("a");
	}
}
