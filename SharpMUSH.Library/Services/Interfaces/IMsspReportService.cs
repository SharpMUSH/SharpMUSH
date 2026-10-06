using SharpMUSH.Library.API;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The MSSP report: what crawlers read about the game, through the MSSP telnet option and through
/// <c>MSSP-REQUEST</c> alike, so the two can never disagree.
/// </summary>
public interface IMsspReportService
{
	/// <summary>
	/// Every variable in the order the specification lists them: the ones the server knows itself
	/// (<see cref="Configuration.Mssp.MsspCatalog.ServerReported"/>), then the administrator's
	/// <c>mssp</c> settings, with names the catalog does not hold last.
	/// </summary>
	ValueTask<IReadOnlyList<MsspReportedVariable>> BuildAsync();
}
