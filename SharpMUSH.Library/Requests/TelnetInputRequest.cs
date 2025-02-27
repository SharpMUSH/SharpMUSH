﻿using Mediator;

namespace SharpMUSH.Library.Requests;

public record TelnetInputRequest(string Handle, string Input) : INotification;