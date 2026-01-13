
/// <summary>
/// Halts a specific task by PID.
/// </summary>
/// <param name="pid">Process ID</param>
/// <returns>True if task was found and halted, false otherwise</returns>
ValueTask<bool> HaltByPid(long pid);
}
