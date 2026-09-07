using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.ILayoutRegistryService"/>: admin-customized portal layouts.
/// Not ported yet — every member throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public Task UpsertLayoutAsync(string scope, LayoutConfiguration layout)
		=> throw new NotImplementedException();

	public Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<string>> GetCustomizedScopesAsync()
		=> throw new NotImplementedException();

	public Task RemoveLayoutAsync(string scope)
		=> throw new NotImplementedException();
}
