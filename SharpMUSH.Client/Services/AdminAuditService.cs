using System.Globalization;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the audit log API (<c>api/admin/audit</c>).</summary>
public class AdminAuditService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<AuditPage>> ListAsync(AuditFilter filter) =>
		Client.GetApiAsync<AuditPage>(
			ApiQuery.Build("api/admin/audit",
				("action", filter.Action),
				("actor", filter.Actor),
				("from", filter.From?.ToString("O", CultureInfo.InvariantCulture)),
				("to", filter.To?.ToString("O", CultureInfo.InvariantCulture)),
				("text", filter.Text),
				("before", filter.Before),
				("limit", filter.Limit.ToString(CultureInfo.InvariantCulture))),
			"The server returned no audit entries.");

	public Task<ApiResult<AuditActionsResponse>> ActionsAsync() =>
		Client.GetApiAsync<AuditActionsResponse>("api/admin/audit/actions", "The server returned no actions.");
}
