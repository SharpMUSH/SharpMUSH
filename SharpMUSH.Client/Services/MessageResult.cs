namespace SharpMUSH.Client.Services;

/// <summary>
/// The result of a call, or the message to show the user because it failed.
/// </summary>
public partial union MessageResult<T>(T, string);
