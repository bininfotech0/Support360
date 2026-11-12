using Xunit;

// Agent tests mutate process-wide state (HTTP proxy environment variables,
// HttpListener ports) and include multi-second recovery scenarios, so they run
// sequentially to avoid cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
