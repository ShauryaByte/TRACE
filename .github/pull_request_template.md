## Description

Briefly describe the changes introduced by this pull request. Explain the rationale and what problem it solves.

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds local instrument functionality)
- [ ] Documentation update
- [ ] Performance optimization or refactor

## Verification Checklist

Please verify that you have completed the following steps:

- [ ] Solution builds cleanly with **0 errors and 0 warnings** in Debug (`dotnet build -c Debug`).
- [ ] Solution builds cleanly with **0 errors and 0 warnings** in Release (`dotnet build -c Release`).
- [ ] Tested in standard unelevated user mode.
- [ ] Tested in elevated Administrator mode (if telemetry or ETW paths were touched).
- [ ] Verified that no persistent activity logging or network telemetry data is written to disk.
- [ ] Verified that no external network calls, analytics, or cloud dependencies were introduced.
- [ ] Application launches cleanly and exits cleanly without hanging background threads.

## Screenshots / Evidence (if applicable)

Attach before/after screenshots or recordings demonstrating the change.
