# Job Tracker

Windows desktop app (WPF, .NET 10) that saves a pasted job page and resume into `JobApplications\<Company>\<date>_<Role>\`, with keyword search,
starred bullets and in-app updates from GitHub Releases. Read `README.md` for the user-facing picture.

## Commands

```
dotnet run --project src/JobTracker.App
dotnet test                                  # all tests (486 at last count)
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
- **Resume files are read in `src/JobTracker.Core/Resumes`** (PdfPig for PDF, the Open XML SDK for Word). The tests run against genuine files made by Word and Edge from invented resumes (`tests/JobTracker.Core.Tests/Fixtures`, regenerate with `tools/make-resume-fixtures.ps1`), not synthetic ones: keep it that way, and add a real fixture for each new shape that goes wrong. When a script drives Word, use its own process and never touch the user's open one. There is no OCR.
- **The JD cleaner is a heuristic.** It keeps every line it does not remove exactly as pasted, and the original is saved as `jd.original.txt`. Do not make it rewrite or re-flow text.
- **Never change the installer `AppId`** (`installer\JobTracker.iss`) or `InstallationInfo.UninstallKey`; they must match.
- **Styles on themed controls need an explicit `Foreground`** (`{DynamicResource TextFillColorPrimaryBrush}`) when they replace the template, or dark mode shows black text. Use alpha-based brushes for cards and highlights so both themes read.
- **Tailoring (`src/JobTracker.Core/Tailoring`, `Ai`) is "the model proposes, code verifies".** Skills come from the fixed list in `skills.txt` (add a line to teach it one), the match and the percentage are counted by code, and a model's bullet is only shown if `BulletVerifier` passes it: no number, skill, name or bigger role that is not in the bullet, the person's notes or their resume. Never loosen those checks to get more suggestions through, and never send more than the picked bullet, the job description, the chosen skills and the notes. The API key lives only in Windows Credential Manager (`ISecretStore`), never in a file or a log, and each service is confirmed once before anything is sent. `resume.md` is never rewritten by tailoring; versions are `resume.vN.md`, and their Word files `resume.vN.docx` (`resume.docx` for the resume as it is). Word export (`Core/Export`) edits a copy of `resume.original.docx` only when every change can be placed with certainty (`DocxResumePatcher`), otherwise it lays out a clean document (`DocxResumeWriter`); both are read back and compared with the text (`ResumeDocxExporter`), and the original file is never written. While a dev copy of the app is running from `bin\Debug`, build with `-p:OutputPath=<another folder>` instead of stopping it. Tests use a fake HTTP handler and a fake model: never a real key or a real call.
- **Pay comes from `Core/Pay` (`PayExtractor`), worked out from the job description each time; the only thing kept is the list's shortcut, `PayCache` (`.jobtracker\pay-cache.json`), which is trusted only while a description's size and last-changed time are unchanged and is safe to delete.** It reads the saved description first, then the original paste (clean-up can drop a "Pay" box). Keep the not-pay list (revenue, funding, bonus, stipend, 401(k), equity) and the plausibility limits per unit; a wrong "pay" is worse than "Not available". Add a test line for every posting wording that goes wrong.
- **The resume built from scratch (`Core/Builder`) is one `ResumeProfile` kept in `<data folder>\.jobtracker\resume-builder\profile.json`, laid out as resume text by `ResumeProfileText`, and made into Word by the same exporter as every other resume.** The AI helper there is `JobBulletWriter` with the person's typed answers as the only facts (no job description, and only the answers and that job's bullets are sent: never the name or contact details). The folder is part of the data: `DataRootMover` copies it, checks it, never overwrites one already in the new folder, and `HasMovableData` makes a change of folder ask even when there are no applications. Never add a way to fill a bullet from nothing.
- **The Settings model drop-down is `ModelCatalog` (the service's own list; ":batch" variants hidden; Claude first; `Fallback` when offline).** It asks only when Settings opens, the service changes, a key is saved, or Refresh is clicked, and Anthropic only with a saved key. The usual model is shown in the box but stored as "none", so it follows the default.
- **Keep user-visible wording plain** (no internals). No real names, resumes or paths in samples, tests, screenshots or docs; use invented data.
- **Every release needs a section in `src/JobTracker.Core/WhatsNew.md`** (`## 1.4.0`, one `- line` per change, written for users, no commit-speak). It is embedded in the app (What's new), becomes the GitHub release notes, and `tools/publish.ps1` refuses to build a version without one. Write it before tagging.
- **The default data folder is smart** (`DataLocations.Recommended`): an existing `JobApplications` in Documents or on the Desktop wins, otherwise Documents if OneDrive syncs it, otherwise the Desktop. Never move anyone's existing data behind their back. The installer page makes the same suggestion (keep the two in step).
- **Moving the data folder must never lose data** (`DataRootMover`): copy everything, verify, switch the setting, and only then delete the originals; roll the copies back on any failure. Keep the rollback tests passing. The installer's folder page only writes `install-defaults.json` when the choice differs from the Desktop default (so an all-users install does not hand one person's Desktop to everyone).
- Environment variables: `JOBTRACKER_DATA_ROOT` (another data folder for this run only; never saved), `JOBTRACKER_SETTINGS_FILE` (another settings file: **always set it when a script or test saves settings**, or it changes the real one) and `JOBTRACKER_NO_UPDATE_CHECK=1`.
- Tooling on this machine: in the Bash tool, heredocs containing apostrophes fail; write files with the file tool or a script file.
- Commit one coherent change at a time: imperative subject (`<area>: <what>`, about 60 characters) and a body saying what and why. The subjects become the release notes.
  Do not push or tag without the user's say-so.
