// The headless UI session owns one dispatcher thread for the whole run; the logic tests are fast, so the classes run
// one after another rather than racing the session's start-up.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
