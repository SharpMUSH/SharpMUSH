using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// The recall window to render, or the <see cref="CallState"/> to return because there is none — the
/// error has already been reported.
/// </summary>
public partial union RecallSelection(ChannelRecall.RecallWindow, CallState);
