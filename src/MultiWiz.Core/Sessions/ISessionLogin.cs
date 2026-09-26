namespace MultiWiz.Core.Sessions;

/// <summary>Types an account's login into its already running client again (for when the first attempt came too early).</summary>
public interface ISessionLogin
{
    /// <summary>Returns null on success, otherwise a message for the user.</summary>
    Task<string?> RetypeLoginAsync(Guid accountId, CancellationToken cancellationToken = default);
}
