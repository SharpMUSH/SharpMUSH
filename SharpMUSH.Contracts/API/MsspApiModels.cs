namespace SharpMUSH.Library.API;

/// <summary>One variable of the MSSP report, with every value it carries, the default last.</summary>
public sealed record MsspReportedVariable(string Name, IReadOnlyList<string> Values);

/// <summary>
/// <c>GET api/mssp</c>: the report crawlers read now, and the administrator's settings it was made from.
/// </summary>
/// <param name="Report">Every variable reported, the server's own and the settings alike.</param>
/// <param name="Settings">The <c>mssp</c> option: the variables an administrator set.</param>
public sealed record MsspSettingsResponse(
	IReadOnlyList<MsspReportedVariable> Report,
	IReadOnlyDictionary<string, string[]> Settings);

/// <summary><c>PUT api/mssp</c>: the whole <c>mssp</c> option, replacing what was there.</summary>
public sealed record MsspSettingsRequest(Dictionary<string, string[]> Settings);
