using Microsoft.Extensions.Time.Testing;

namespace MultiWiz.Core.Tests.Support;

internal static class FakeTimeDriver
{
    private static readonly TimeSpan DefaultStep = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Advances fake time in small steps until <paramref name="task"/> completes. Between steps it yields for a
    /// moment of real time so continuations queued to the thread pool can register their next timers.
    /// Throws if the task is still running after <paramref name="budget"/> of fake time.
    /// </summary>
    public static async Task<T> RunUntilCompleteAsync<T>(this FakeTimeProvider time, Task<T> task, TimeSpan? step = null, TimeSpan? budget = null)
    {
        await DriveAsync(time, task, step ?? DefaultStep, budget ?? DefaultBudget);
        return await task;
    }

    public static async Task RunUntilCompleteAsync(this FakeTimeProvider time, Task task, TimeSpan? step = null, TimeSpan? budget = null)
    {
        await DriveAsync(time, task, step ?? DefaultStep, budget ?? DefaultBudget);
        await task;
    }

    private static async Task DriveAsync(FakeTimeProvider time, Task task, TimeSpan step, TimeSpan budget)
    {
        var elapsed = TimeSpan.Zero;
        while (!task.IsCompleted)
        {
            if (elapsed >= budget)
            {
                throw new TimeoutException($"The task was still running after {budget} of fake time.");
            }

            time.Advance(step);
            elapsed += step;
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }
}
