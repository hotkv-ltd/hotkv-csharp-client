# Contributing to the HotKV .NET Client

Thank you for your interest in improving this SDK. Bug reports, fixes and
documentation improvements are welcome.

## Reporting problems

- Bugs and feature requests: open an issue on the
  [issue tracker](https://github.com/hotkv-ltd/hotkv-csharp-client/issues) and fill in
  the template. Include the SDK version, your .NET version, the HotKV server
  version and a minimal way to reproduce the problem.
- Security vulnerabilities: do not open a public issue. Follow
  [SECURITY.md](SECURITY.md).

## Making a change

1. For anything larger than a small fix, open an issue first so the approach can
   be agreed before you spend time on it.
2. Fork the repository and create a branch from `main`.
3. Keep the change focused. Follow the style of the surrounding code (the formatting and nullable annotations the project already uses).
4. Add or update tests for the behavior you change.
5. Run the checks below and make sure they pass.
6. Open a pull request against `main` and describe what changed and why.

## Checks

These need no server:

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Integration tests need a running HotKV server and are skipped when none is
configured. See the Testing section of the [README](README.md) for how to point
them at a server. Run them only against a throwaway server, because they create
and delete keys.

## Source headers

Every source file starts with the project header:

```
Copyright (c) 2026 HotKV Ltd
Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
```

## License of contributions

This project is licensed under the Apache License, Version 2.0. By submitting a
contribution you agree that it is licensed under the same terms (section 5 of
the license).

## Conduct

Be respectful and constructive. Harassment and personal attacks are not
tolerated, and maintainers may remove comments or contributions that cross
that line.
