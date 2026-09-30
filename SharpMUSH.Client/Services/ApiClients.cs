namespace SharpMUSH.Client.Services;

/// <summary>Names of the <see cref="IHttpClientFactory"/> clients Program.cs registers.</summary>
public static class ApiClients
{
	/// <summary>The server API without <see cref="AccountSessionBearerHandler"/>: requests on it never wait
	/// for the stored session to be restored, and never carry it.</summary>
	public const string Anonymous = "api-anonymous";
}
