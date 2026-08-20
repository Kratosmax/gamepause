namespace GamePause.Core;

public sealed class ShellFailureTracker(int failureThreshold = 2)
{
    private readonly int _failureThreshold = failureThreshold > 0
        ? failureThreshold
        : throw new ArgumentOutOfRangeException(nameof(failureThreshold));

    public int ConsecutiveFailures { get; private set; }

    public bool Observe(bool shouldMonitor, bool shellResponsive)
    {
        if (!shouldMonitor || shellResponsive)
        {
            ConsecutiveFailures = 0;
            return false;
        }

        ConsecutiveFailures++;
        if (ConsecutiveFailures < _failureThreshold)
        {
            return false;
        }

        ConsecutiveFailures = 0;
        return true;
    }
}
