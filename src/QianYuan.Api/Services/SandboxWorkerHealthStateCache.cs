using System.Collections.Concurrent;

namespace QianYuan.Api.Services;

public sealed class SandboxWorkerHealthStateCache
{
    private readonly ConcurrentDictionary<string, WorkerHealthState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable(string workerId, DateTimeOffset now)
    {
        if (!_states.TryGetValue(workerId, out var state)) return true;
        if (state.CircuitOpenUntil is null) return state.IsHealthy;
        return state.CircuitOpenUntil <= now && state.IsHealthy;
    }

    public void MarkProbeResult(string workerId, bool isHealthy)
    {
        _states.AddOrUpdate(
            workerId,
            _ => new WorkerHealthState { IsHealthy = isHealthy, ConsecutiveFailures = isHealthy ? 0 : 1 },
            (_, current) =>
            {
                current.IsHealthy = isHealthy;
                current.ConsecutiveFailures = isHealthy ? 0 : Math.Max(1, current.ConsecutiveFailures);
                if (isHealthy) current.CircuitOpenUntil = null;
                return current;
            });
    }

    public void MarkExecutionSuccess(string workerId)
    {
        _states.AddOrUpdate(
            workerId,
            _ => new WorkerHealthState { IsHealthy = true, ConsecutiveFailures = 0 },
            (_, current) =>
            {
                current.IsHealthy = true;
                current.ConsecutiveFailures = 0;
                current.CircuitOpenUntil = null;
                return current;
            });
    }

    public void MarkExecutionFailure(string workerId, int failureThreshold, TimeSpan openDuration, DateTimeOffset now)
    {
        _states.AddOrUpdate(
            workerId,
            _ => BuildFailedState(1, failureThreshold, openDuration, now),
            (_, current) =>
            {
                var nextFailures = current.ConsecutiveFailures + 1;
                return BuildFailedState(nextFailures, failureThreshold, openDuration, now);
            });
    }

    private static WorkerHealthState BuildFailedState(int failures, int failureThreshold, TimeSpan openDuration, DateTimeOffset now)
    {
        var state = new WorkerHealthState
        {
            IsHealthy = false,
            ConsecutiveFailures = failures,
        };

        if (failures >= Math.Max(1, failureThreshold))
            state.CircuitOpenUntil = now + openDuration;

        return state;
    }

    private sealed class WorkerHealthState
    {
        public bool IsHealthy { get; set; } = true;
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? CircuitOpenUntil { get; set; }
    }
}
