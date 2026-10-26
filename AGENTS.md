# ControlHub contributor instructions

## Project and commands

- The web application is `ControleEstoqueLoja.csproj`, targeting `net10.0`.
- Automated tests are in `tests.QA/tests.QA.csproj` (xUnit, SQLite test databases in temporary locations).
- Restore: `dotnet restore tests.QA/tests.QA.csproj`
- Build: `dotnet build ControleEstoqueLoja.csproj --configuration Release --no-restore`
- Test with TRX: `dotnet test tests.QA/tests.QA.csproj --configuration Release --logger "trx;LogFileName=controlhub-tests.trx" --results-directory TestResults`

## Repository rules

- Preserve existing working-tree changes. Inspect `git status --short` and `git diff` before editing.
- Do not use destructive Git commands, remove existing tests, rewrite applied migrations, commit, push, or publish without explicit authorization.
- Keep changes scoped. Do not reformat unrelated files or change the target framework and package versions without a documented technical reason.
- Use a disposable SQLite database for tests. Never use or recreate the operational `controleestoque.db` to validate changes.
- For schema changes, review the migration and model snapshot and preserve compatibility with existing data.
- Do not place credentials, private keys, or operational data in tracked files. Keep local databases, secrets, and generated test output out of Git.
- Preserve routes, HTTP contracts, and view-facing property names unless a required change is documented and covered by tests.
- Report actual test totals and limitations. Do not describe a feature as verified just because its class or test file exists.

## Implementation conventions

- Follow the existing ASP.NET Core MVC, EF Core, SQLite, Identity, and named authorization policy patterns.
- Validate business rules on the server; keep monetary persistence consistent with the existing integer-cent conversion.
- Add or update focused tests for behavior changes and run the relevant tests and build when the environment permits.
- Review the final diff for accidental changes, data exposure, and migration compatibility.
