namespace SharpMUSH.Client.Services;

/// <summary>
/// The result of a call, or the message to show the user because it failed.
/// </summary>
public union MessageResult<T>(T, string);
