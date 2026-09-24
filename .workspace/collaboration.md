# Collaboration guide

## Before changing code

- Read the repository README and the relevant project, service, model, or test files.
- State the intended scope in a task brief and identify the smallest validation command.
- Preserve existing behavior unless the task explicitly changes it.

## While working

- Keep changes focused and avoid unrelated formatting or refactoring.
- Prefer existing helpers and patterns over new parallel abstractions.
- Keep user-facing destructive actions explicit, previewable, and logged.
- Never record credentials, personal paths, machine identifiers, or other sensitive data in notes or logs.

## Before handoff or review

- Run the relevant tests and build; record the exact commands and results.
- Inspect the diff for generated files, secrets, local configuration, and scope creep.
- Summarize changed files, remaining risks, and any follow-up needed.
- A reviewer should be able to reproduce the validation from the handoff alone.
