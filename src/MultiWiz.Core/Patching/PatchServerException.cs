namespace MultiWiz.Core.Patching;

/// <summary>
/// A problem talking to KingsIsle's patch services or with the file list they returned. <see cref="Exception.Message"/>
/// is written for the user.
/// </summary>
public sealed class PatchServerException : Exception
{
    public PatchServerException(string message)
        : base(message)
    {
    }

    public PatchServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
