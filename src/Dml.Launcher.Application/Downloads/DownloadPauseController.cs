namespace Dml.Launcher.Application.Downloads;

public sealed class DownloadPauseController
{
    private volatile TaskCompletionSource resumeSignal = CreateCompletedSignal();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        resumeSignal.TrySetResult();
    }

    public async ValueTask WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (IsPaused)
        {
            await resumeSignal.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource CreateCompletedSignal()
    {
        TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
