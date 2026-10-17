using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Server.Services;

/// <summary>The ordinary package review payload shared by single and batch workflows.</summary>
public static class PackageReviewPresenter
{
	public static PlanResponse For(PackageManifest manifest, string commit, PackageChangeset changeset,
		IReadOnlyDictionary<string, string> answers, IReadOnlyList<string> warnings)
		=> new(manifest.Name, manifest.Version.ToString(), commit, changeset,
			manifest.Configure.Values.Select(c => new ConfigurePromptDto(c.Key, c.Label, c.Type.ToString().ToLowerInvariant(),
				c.Default, answers.ContainsKey(c.Key) || c.Default is not null)).ToArray(),
			changeset.Attributes.Select(a => new AttributeRenderDto(a.TargetRef, a.Attribute,
				a.BaseValue is null ? null : MushcodeHighlighter.ToHtml(a.BaseValue),
				a.LiveValue is null ? null : MushcodeHighlighter.ToHtml(a.LiveValue),
				a.NewValue is null ? null : MushcodeHighlighter.ToHtml(a.NewValue),
				MushcodeHighlighter.FindDangerousPatterns(a.MergedValue ?? a.NewValue ?? ""),
				a.MergedValue is null ? null : MushcodeHighlighter.ToHtml(a.MergedValue))).ToArray(), warnings,
			manifest.Kind, manifest.Binary);
}
