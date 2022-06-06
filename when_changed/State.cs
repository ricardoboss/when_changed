namespace when_changed
{
    internal enum State
    {
        Watching,
        WaitingToExecute,
        Executing,
        ExecutingDirty,
    }
}