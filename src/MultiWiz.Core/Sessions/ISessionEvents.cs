namespace MultiWiz.Core.Sessions;

/// <summary>Extra session notifications for the UI.</summary>
public interface ISessionEvents
{
    /// <summary>Raised after credentials were typed into a client (only when <see cref="Settings.LoginSettings.RefocusAfterLogin"/> is on).</summary>
    event EventHandler<ClientSession>? LoginCompleted;
}
