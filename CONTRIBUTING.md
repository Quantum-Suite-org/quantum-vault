# Contributing to Quantum Vault

Thank you for your interest in contributing.

## Before You Contribute

Quantum Vault was initially developed with substantial AI assistance. If you
submit a contribution, your patch represents human-authored work. Please
ensure you have the right to submit it under the MIT License.

## How to Contribute

1. Open an issue first for anything non-trivial — bugs, features, design changes.
2. Fork the repository and create a branch: `git checkout -b fix/your-fix`
3. Make your change. Keep it focused and small.
4. Run the test suite before submitting: `pytest src/tests/ -v`
5. Submit a pull request with a clear description of what changed and why.

## Developer Certificate of Origin

By submitting a pull request, you certify that:

- The contribution is your own original work, or you have the right to
  submit it under the MIT License.
- You understand the contribution will be included in a MIT-licensed project.

We do not currently require a formal CLA. A DCO sign-off (`git commit -s`)
is appreciated but not enforced at this stage.

## Code Style

- **C++**: Follow existing style in `qvault_core.cpp`. MSVC `/W3` clean.
- **C#**: Standard .NET naming conventions. No third-party UI frameworks.
- **Python**: PEP 8. No mandatory third-party dependencies in `qvault/`.
- **Tests**: Every functional change should include or update a test.

## What We Will Not Accept

- Dependencies that expand the footprint significantly without strong justification.
- Cloud sync, telemetry, authentication, or advertisement features.
- Breaking changes to the C ABI without a versioning plan.
- Code that removes or weakens encryption of user data.

## Reporting Security Issues

Do not open a public issue for security vulnerabilities. Email the maintainers
directly (see repository contact) or use GitHub private vulnerability reporting.
