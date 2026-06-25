// The auth-enabled tests toggle a process-global environment variable that the
// mock host reads at build time. ReportingTestHost serializes the build window,
// but disabling test-collection parallelism removes the race entirely and keeps
// the suite deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
