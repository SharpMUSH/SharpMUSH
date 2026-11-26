using SharpMUSH.Client.Models.PlayInput;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Models;

/// <summary>Play's channel leads, escape character and bound keys (<see cref="InputShortcuts"/>, <see cref="KeyChord"/>).</summary>
public class InputShortcutsTests
{
	private static readonly InputShortcuts Leads = new([],
		[new ChannelLead(",", "Public"), new ChannelLead(",,", "OOC"), new ChannelLead("pub", "Public")], '/');

	[Test]
	[Arguments(",hello", "@chat Public=hello")]
	[Arguments(", hello", "@chat Public=hello")]
	[Arguments(",,hi there", "@chat OOC=hi there")]
	[Arguments(",:waves.", "@chat Public=:waves.")]
	[Arguments("pub hello", "@chat Public=hello")]
	[Arguments("PUB hello", "@chat Public=hello")]
	[Arguments("publish now", "publish now")]
	[Arguments("pub", "pub")]
	[Arguments(",", ",")]
	[Arguments(",,", ",,")]
	[Arguments(",, ", ",, ")]
	[Arguments("/,hello", ",hello")]
	[Arguments("/look", "look")]
	[Arguments("/", "/")]
	[Arguments(":waves.", ":waves.")]
	[Arguments("\"Hello.", "\"Hello.")]
	[Arguments("look", "look")]
	public async Task A_typed_line_goes_to_its_lead_s_channel_or_as_typed(string typed, string sent)
	{
		await Assert.That(Leads.Expand(typed)).IsEqualTo(sent);
	}

	[Test]
	public async Task Without_leads_the_escape_character_is_an_ordinary_character()
	{
		await Assert.That(InputShortcuts.None.Expand("/look")).IsEqualTo("/look");
	}

	[Test]
	[Arguments(":", LeadProblem.Reserved)]
	[Arguments(";x", LeadProblem.Reserved)]
	[Arguments("\"", LeadProblem.Reserved)]
	[Arguments("/x", LeadProblem.Escape)]
	[Arguments("a b", LeadProblem.Space)]
	[Arguments("", LeadProblem.Empty)]
	[Arguments("abcdefghi", LeadProblem.TooLong)]
	[Arguments("PUB", LeadProblem.Duplicate)]
	[Arguments("=", LeadProblem.None)]
	[Arguments("+", LeadProblem.CommandStart)]
	[Arguments("@@", LeadProblem.CommandStart)]
	[Arguments("+pub", LeadProblem.None)]
	public async Task A_lead_never_takes_a_pose_a_say_or_the_escape(string lead, LeadProblem problem)
	{
		await Assert.That(InputShortcuts.Check(lead, '/', Leads.Leads)).IsEqualTo(problem);
	}

	[Test]
	[Arguments('/', EscapeProblem.None)]
	[Arguments('!', EscapeProblem.None)]
	[Arguments('a', EscapeProblem.NotASymbol)]
	[Arguments('5', EscapeProblem.NotASymbol)]
	[Arguments(' ', EscapeProblem.NotASymbol)]
	[Arguments(':', EscapeProblem.Reserved)]
	[Arguments(',', EscapeProblem.StartsALead)]
	[Arguments('@', EscapeProblem.CommandStart)]
	[Arguments('&', EscapeProblem.CommandStart)]
	[Arguments('\\', EscapeProblem.CommandStart)]
	public async Task The_escape_is_a_symbol_no_lead_starts_with(char escape, EscapeProblem problem)
	{
		await Assert.That(InputShortcuts.CheckEscape(escape, Leads.Leads)).IsEqualTo(problem);
	}

	[Test]
	[Arguments("F2", true)]
	[Arguments("Shift+F2", true)]
	[Arguments("Ctrl+Alt+Shift+F10", true)]
	[Arguments("Numpad8", true)]
	[Arguments("Alt+NumpadAdd", true)]
	[Arguments("F5", false)]
	[Arguments("F6", false)]
	[Arguments("F11", false)]
	[Arguments("F12", false)]
	[Arguments("Ctrl+F4", false)]
	[Arguments("Alt+F4", false)]
	[Arguments("Ctrl+NumpadAdd", false)]
	[Arguments("Ctrl+Numpad0", false)]
	[Arguments("Ctrl+Numpad5", false)]
	[Arguments("Ctrl+NumpadMultiply", true)]
	[Arguments("Alt+Numpad5", true)]
	[Arguments("Ctrl+Digit1", false)]
	[Arguments("KeyA", false)]
	[Arguments("Shift+Ctrl+F2", false)]
	[Arguments("NumpadEnter", false)]
	public async Task Only_function_keys_and_the_number_pad_the_browser_leaves_alone_bind(string chord, bool allowed)
	{
		await Assert.That(KeyChord.Allowed(chord)).IsEqualTo(allowed);
	}

	[Test]
	public async Task A_chord_is_written_ctrl_alt_shift_then_the_code()
	{
		await Assert.That(KeyChord.Of("F3", ctrl: true, alt: true, shift: true)).IsEqualTo("Ctrl+Alt+Shift+F3");
		await Assert.That(KeyChord.Of("Numpad8", false, false, false)).IsEqualTo("Numpad8");
	}

	[Test]
	public async Task A_bound_key_finds_its_command()
	{
		var shortcuts = new InputShortcuts([new KeyShortcut("F2", "look")], [], '/');
		await Assert.That(shortcuts.CommandFor("F2") is string command ? command : null).IsEqualTo("look");
		await Assert.That(shortcuts.CommandFor("F3") is NotFound).IsTrue();
	}

	[Test]
	public async Task Stored_values_a_player_could_not_have_saved_are_dropped()
	{
		var read = InputShortcuts.Sanitize(
			[new KeyShortcut("F2", "look"), new KeyShortcut("F2", "score"), new KeyShortcut("F5", "x"),
				new KeyShortcut("F3", "  "), new KeyShortcut(null!, "x")],
			[new ChannelLead(",", " Public "), new ChannelLead(":", "OOC"), new ChannelLead(",", "Other"),
				new ChannelLead("=", "a=b"), new ChannelLead("!", "")],
			"ab");

		await Assert.That(read.Keys).IsEquivalentTo([new KeyShortcut("F2", "look")]);
		await Assert.That(read.Leads).IsEquivalentTo([new ChannelLead(",", "Public")]);
		await Assert.That(read.Escape).IsEqualTo(InputShortcuts.DefaultEscape);
	}

	[Test]
	public async Task A_stored_lead_that_starts_with_the_stored_escape_is_dropped()
	{
		var read = InputShortcuts.Sanitize([], [new ChannelLead("!x", "Public"), new ChannelLead(",", "Public")], "!");
		await Assert.That(read.Escape).IsEqualTo('!');
		await Assert.That(read.Leads).IsEquivalentTo([new ChannelLead(",", "Public")]);
	}
}
