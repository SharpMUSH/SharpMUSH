namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// Which list the scene archive shows, as its address carries it: <c>/scenes</c> is every scene, newest
/// activity first; <c>?mine=1</c> the acting character's; <c>?scheduled=1</c> and <c>?finished=1</c>
/// the two other lists <c>/api/scenes</c> serves. The sidebar links to these and the page reads them.
/// </summary>
public static class ScenesFilter
{
	public const string All = "";
	public const string Mine = "mine";
	public const string Scheduled = "scheduled";
	public const string Finished = "finished";

	/// <summary>The list the schedule asks the API for: the scheduled scenes without the ones long past due.</summary>
	public const string Upcoming = "upcoming";

	/// <summary>How many scenes a list asks for; the API's own default.</summary>
	public const int PageSize = 50;

	public static string Href(string filter) => filter == All ? "/scenes" : $"/scenes?{filter}=1";

	/// <summary>The filter an address names; the first of <c>mine</c>, <c>scheduled</c>, <c>finished</c> set to 1.</summary>
	public static string FromUri(string uri)
	{
		var query = new Uri(uri).Query.TrimStart('?');
		foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = pair.IndexOf('=');
			if (eq < 0 || pair[(eq + 1)..] != "1") continue;
			switch (pair[..eq])
			{
				case Mine: return Mine;
				case Scheduled: return Scheduled;
				case Finished: return Finished;
			}
		}
		return All;
	}
}
