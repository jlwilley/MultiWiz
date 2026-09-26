namespace MultiWiz.Core.Sessions;

/// <summary>Ties a client that was started outside MultiWiz to a saved account.</summary>
public interface ISessionLinking
{
    /// <summary>
    /// Makes the external session <paramref name="externalId"/> the session of <paramref name="accountId"/>: it is
    /// re-keyed to the account (and raised through <see cref="ISessionManager.SessionChanged"/> as an Exited snapshot
    /// under the old id, then a Running one under the account), so the account's name shows and Stop and Retype login
    /// work. The account must exist, play the same game and not have a client running. Returns null on success,
    /// otherwise a message for the user.
    /// </summary>
    string? LinkExternal(Guid externalId, Guid accountId);
}
