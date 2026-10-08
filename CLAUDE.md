# Job Tracker

Windows desktop app (WPF, .NET 10) that saves a pasted job page and resume into `JobApplications\<Company>\<date>_<Role>\`, with keyword search,
starred bullets and in-app updates from GitHub Releases. Read `README.md` for the user-facing picture.

## Commands

```
dotnet run --project src/JobTracker.App
dotnet test                                  # all tests (112 at last count)
dotnet build JobTracker.slnx -c Release      # use -c Release when the user has the Debug exe running
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -Version 1.0.0 -Installer
```

- A running app locks its DLLs (MSB3021). Do not kill the user's process; verify with `-c Release`.

## Layout

`src/JobTracker.Core` (no UI: `ApplicationStore`, `JdExtractor`, `JdCleaner`, `BulletText.Structure`, `KeywordSearchProvider`, `BulletStore`, `Updates/`),
`src/JobTracker.App` (WPF, MVVM, CommunityToolkit.Mvvm), `tests/JobTracker.Core.Tests`, `installer/JobTracker.iss`, `tools/`, `.github/workflows/`.

## Rules for working here

- **Tests must pass.** Add or update tests with every behaviour change in Core.
- **Files on disk are the source of truth.** No database. Writes go through `AtomicFile` (temp file, then move).
- **The JD cleaner is a heuristic.** It keeps every line it does not remove exactly as pasted, and the original is saved as `jd.original.txt`. Do not make it rewrite or re-flow text.
- **Never change the installer `AppId`** (`installer\JobTracker.iss`) or `InstallationInfo.UninstallKey`; they must match.
- **Styles on themed controls need an explicit `Foreground`** (`{DynamicResource TextFillColorPrimaryBrush}`) when they replace the template, or dark mode shows black text. Use alpha-based brushes for cards and highlights so both themes read.
- **Keep user-visible wording plain** (no internals). No real names, resumes or paths in samples, tests, screenshots or docs; use invented data.
- Environment variables: `JOBTRACKER_DATA_ROOT` (use another data folder for this run only; never saved) and `JOBTRACKER_NO_UPDATE_CHECK=1`.
- Tooling on this machine: in the Bash tool, heredocs containing apostrophes fail; write files with the file tool or a script file.
- Commit one coherent change at a time: imperative subject (`<area>: <what>`, about 60 characters) and a body saying what and why. The subjects become the release notes.
  Do not push or tag without the user's say-so.
