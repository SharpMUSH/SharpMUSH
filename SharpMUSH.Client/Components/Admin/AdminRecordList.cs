using Microsoft.AspNetCore.Components;
using MudBlazor;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Components.Admin;

/// <summary>What an <see cref="AdminRecordList{T}"/> selects when a reload finds nothing to keep selected.</summary>
public enum AdminReselect
{
	/// <summary>Leave nothing selected: the page shows its empty detail panel.</summary>
	Nothing,

	/// <summary>Select the first record, so the detail panel is never empty while the list is not.</summary>
	First,
}

/// <summary>The strings one confirm-delete shows, so each page keeps its own resource keys.</summary>
/// <param name="Title">The confirmation's title.</param>
/// <param name="Message">The confirmation's body, naming the record.</param>
/// <param name="Confirm">The button that deletes.</param>
/// <param name="Cancel">The button that backs out.</param>
/// <param name="Deleted">The success snackbar.</param>
/// <param name="Failed">The error snackbar, given why the server refused.</param>
public sealed record AdminDeleteText(
	string Title,
	string Message,
	string Confirm,
	string Cancel,
	string Deleted,
	Func<ApiFailure, string> Failed);

/// <summary>
/// The list-and-detail admin flow: load the records, keep one selected across reloads, open an edit
/// dialog and reload when it saves, confirm a delete and reload after it.
/// </summary>
/// <remarks>
/// The markup stays with each page (its cards and detail panel are its own); this holds only the
/// state and the sequence every such page used to write out by hand. A record is identified by
/// <c>keyOf</c>, compared ordinally, so a reload swaps the selected instance for its fresh copy.
/// </remarks>
/// <typeparam name="T">The record the page lists.</typeparam>
public sealed class AdminRecordList<T>(
	IDialogService dialogs,
	ISnackbar snackbar,
	Func<Task<IEnumerable<T>>> fetch,
	Func<T, string> keyOf,
	AdminReselect reselect = AdminReselect.Nothing) where T : class
{
	/// <summary>The records as last loaded.</summary>
	public IReadOnlyList<T> Items { get; private set; } = [];

	/// <summary>The record the detail panel shows, or <see langword="null"/> for none.</summary>
	public T? Selected { get; private set; }

	/// <summary>True until the first load completes, and again while any reload runs.</summary>
	public bool Loading { get; private set; } = true;

	/// <summary>
	/// Called whenever <see cref="Selected"/> is set, including after every reload re-resolves it, so a
	/// page with editable fields can refill them from the fresh record.
	/// </summary>
	public Action<T?>? SelectionChanged { get; init; }

	/// <summary>Whether <paramref name="item"/> is the selected record.</summary>
	public bool IsSelected(T item) =>
		Selected is not null && string.Equals(keyOf(Selected), keyOf(item), StringComparison.Ordinal);

	/// <summary>The loaded record with this key, if there is one.</summary>
	public T? Find(string key) =>
		Items.FirstOrDefault(item => string.Equals(keyOf(item), key, StringComparison.Ordinal));

	public void Select(T item) => SetSelected(item);

	public void Deselect() => SetSelected(null);

	/// <summary>
	/// Re-reads the records and keeps the selection on the record with the same key; when that record
	/// is gone (or nothing was selected) the <see cref="AdminReselect"/> policy decides.
	/// </summary>
	public async Task LoadAsync()
	{
		Loading = true;
		try
		{
			Items = [.. await fetch()];
		}
		finally
		{
			Loading = false;
		}

		var kept = Selected is null ? null : Find(keyOf(Selected));
		SetSelected(kept ?? (reselect == AdminReselect.First ? Items.FirstOrDefault() : null));
	}

	/// <summary>
	/// Opens <typeparamref name="TDialog"/> and, when it closes with a result, reloads. A dialog that
	/// closes with a record's key as its data has that record selected after the reload.
	/// </summary>
	/// <returns>Whether the dialog saved.</returns>
	public async Task<bool> EditAsync<TDialog>(string title, DialogParameters? parameters = null)
		where TDialog : IComponent
	{
		// FullWidth is required: the edit dialogs' bodies declare container-type, and inline-size
		// containment zeroes their contribution to the paper's shrink-to-fit width.
		var dialog = await dialogs.ShowAsync<TDialog>(title, parameters ?? new DialogParameters(), new DialogOptions { FullWidth = true });
		var result = await dialog.Result;
		if (result is null || result.Canceled) return false;

		await LoadAsync();
		if (result.Data is string key && Find(key) is { } saved) Select(saved);
		return true;
	}

	/// <summary>
	/// Asks before deleting <paramref name="item"/>; on confirmation runs <paramref name="delete"/>,
	/// reports the outcome, and after a success drops the selection if it was this record and reloads.
	/// </summary>
	/// <returns>Whether the record was deleted.</returns>
	public async Task<bool> DeleteAsync(T item, AdminDeleteText text, Func<Task<ApiResult<Success>>> delete)
	{
		var confirmed = await dialogs.ShowMessageBoxAsync(
			text.Title, text.Message, yesText: text.Confirm, cancelText: text.Cancel);
		if (confirmed != true) return false;

		switch (await delete())
		{
			case Success:
				snackbar.Add(text.Deleted, Severity.Success);
				if (IsSelected(item)) Deselect();
				await LoadAsync();
				return true;
			case ApiFailure failure:
				snackbar.Add(text.Failed(failure), Severity.Error);
				return false;
			default:
				return false;
		}
	}

	private void SetSelected(T? item)
	{
		Selected = item;
		SelectionChanged?.Invoke(item);
	}
}
