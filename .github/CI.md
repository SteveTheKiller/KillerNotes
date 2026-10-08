# Pull request CI

The local workflow `workflows/pr-build-test.yml` restores and builds the application and runs the
existing regression suites on GitHub-hosted Windows 2025. Publication is separately authorized;
these local definitions have not run on GitHub. Logs and test results are retained for seven days.
Official checkout, setup-dotnet, and upload-artifact actions are pinned to immutable commit hashes.

It uses only `pull_request`, `contents: read`, and checkout with persisted credentials disabled.
There is no publishing, deployment, signing, secret input, or privileged self-hosted runner.
Interactive desktop testing is separate; passing these jobs does not certify a user's full window,
keyboard, screen reader, or hardware.

The application and KillerNotes.Tests xUnit project each have a separate job. The build SDK is .NET 10;
these projects continue targeting net48. The repository owns its existing dependency and reference
assembly versions. No release script is called by this workflow.
