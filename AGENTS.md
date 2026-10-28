# Project instructions

## Working agreement

- Write clear, concise documentation and code comments. Explain architectural choices and trade-offs in conversation.
- Read this file before each implementation task. Inspect the current code rather than assuming an earlier chat describes its present state.
- Obtain explicit approval before changing implementation logic. Discussion, questions, proposed plans, and future milestones are not implementation approval.
- Work only within the approved scope. An approved step includes its necessary implementation and verification; do not ask again for routine choices within that scope.
- Propose and explain material changes to the agreed scope before implementing them. Do not implement later stages or perform unrelated refactoring automatically.
- Preserve the user's edits and configuration. Before finishing, review changes against these instructions and report what changed, why, and how it was verified.

## Design philosophy

Follow John Ousterhout's *A Philosophy of Software Design*. The goal is to reduce the complexity a human developer must understand, not to maximize abstraction or minimize line count.

- Prefer deep modules: simple interfaces that hide meaningful implementation detail. Keep decisions and knowledge localized behind the appropriate boundary.
- Design interfaces around what callers need. Avoid leaking storage, RPC, or configuration details unless they are necessary to the contract.
- Avoid shallow wrappers, pass-through methods, speculative extension points, and layers that add navigation without hiding complexity.
- Do not split methods merely to make them short. Keep related logic together when that makes the behaviour easier to understand; extract a method when it provides a useful abstraction or removes meaningful duplication.
- Prefer explicit, consistent code and precise names. Avoid cleverness and unexplained abbreviations.
- Make invariants, failure behaviour, and important distinctions clear. In this project, an unavailable transaction, a failed transaction, and a successful transaction with no supported transfers are different situations.
- Use established .NET facilities when they simplify the design, such as configuration providers and the Options pattern. Do not introduce a framework or dependency without a concrete benefit.
- Keep the first implementation small while preserving the agreed project boundaries. Separate projects do not justify adding unnecessary interfaces or classes.

## Comments and documentation

- Write comments that explain information the code does not express well: intent, assumptions, constraints, reasoning, and non-obvious consequences.
- For important public interfaces, document the caller's contract: what the operation does, required inputs, units, returned meaning, side effects, and relevant errors or edge cases. Do not expose implementation details in interface comments unnecessarily.
- Use implementation comments to explain why an approach is necessary or how a subtle algorithm works. Do not narrate obvious statements or repeat a method's name.
- Do not rely on “self-documenting code” as a reason to omit useful explanations. Equally, do not require comments on every member.
- Keep comments accurate when behaviour changes. Fix misleading names or structure rather than using comments to excuse confusing code.
- Use examples when they clarify domain rules, especially instruction indexes, pagination, checkpoints, and transaction outcomes.

## Project boundaries and current scope

The solution is `SolanaTracker.sln`, targeting .NET 10:

- `SolanaTracker.Domain`: plain domain records, independent of RPC, console output, and persistence.
- `SolanaTracker.Application`: application contracts and, as needed, ingestion workflows; references Domain.
- `SolanaTracker.Infrastructure`: Solana JSON-RPC, transaction JSON extraction, and PostgreSQL connectivity; references Application and Domain.
- `SolanaTracker.ConsoleApp`: executable exploration tool, Options configuration, composition, and console presentation; references Application and Infrastructure.
- `SolanaTracker.Tests`: xUnit tests and offline JSON fixtures, including configuration and console workflow tests.

The current console app makes one pass: get the finalized slot, list recent wallet signatures, retrieve the newest transaction, print its instructions, and extract supported transfers. Settings are bound from the `Explorer` configuration section and validated at host startup. Preserve the positional wallet argument and named/environment overrides unless a change is approved.

Supported transfers are successful, parsed, top-level System Program instructions with type exactly `transfer`, involving a monitored wallet. Preserve original instruction indexes, case-sensitive addresses, and exact integer lamport amounts. Failed transactions do not produce completed transfer records. Inner transfers, tokens, vote withdrawals, and other instruction types are outside the current extraction scope.

PostgreSQL persistence, pagination, wallet checkpoints, continuous polling, recovery, retries, and the REST API are planned stages, not automatically authorized work. Do not add a broker or full event-sourcing architecture without an agreed need.

## Verification

Run commands from the directory containing this file:

```sh
dotnet build SolanaTracker.sln
dotnet test SolanaTracker.sln
dotnet run --project SolanaTracker.ConsoleApp/SolanaTracker.ConsoleApp.csproj
```

- Add tests for meaningful behaviour and failure boundaries, not implementation details or trivial wrappers.
- Keep unit tests deterministic and offline. Use saved transaction fixtures and fake HTTP responses where appropriate.
- When persistence is introduced, use real PostgreSQL integration tests for database constraints, atomic writes, deduplication, and recovery; do not substitute an in-memory database for those guarantees.
- Treat live Devnet checks as separate verification, not a prerequisite for ordinary unit tests. Never require a private key or seed phrase for read-only indexing.
- Run relevant tests after code changes. Run the full existing suite when changes cross project boundaries or affect shared behaviour. Report blocked or unverified checks honestly.
- Documentation-only edits do not require running the application or tests.

## Instruction discovery

Keep this file concise and at the project root. If an IDE agent does not report it among its loaded instructions, explicitly open it before implementation. Automatic discovery depends on the integration and workspace root; do not promise that file presence guarantees compliance.
