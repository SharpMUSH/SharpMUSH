using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Client.Services;

/// <summary>Localizes presentation while retaining stable step IDs, routes and literal commands.</summary>
public static class GuideLocalization
{
	public static IReadOnlyList<GuideLesson> Player(GuidePath path, IStringLocalizer<SharedResource> text)
		=> Translate(GuideCatalog.Player(path), path.ToString(), text);

	public static IReadOnlyList<GuideLesson> Administrator(IStringLocalizer<SharedResource> text)
		=> Translate(GuideCatalog.Administrator, "Administrator", text);

	private static IReadOnlyList<GuideLesson> Translate(IReadOnlyList<GuideLesson> lessons,
		string journey, IStringLocalizer<SharedResource> text)
		=> lessons.Select(lesson => lesson with
		{
			Title = text[$"GuideLesson_{journey}_{lesson.Id}_Title"],
			Body = text[$"GuideLesson_{journey}_{lesson.Id}_Body"],
			ActionLabel = lesson.ActionLabel is null ? null : text[$"GuideLesson_{journey}_{lesson.Id}_Action"].Value
		}).ToArray();
}
