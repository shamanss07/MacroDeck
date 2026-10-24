# Contributing to MacroDeck

[English](CONTRIBUTING.md) · [Português (Brasil)](CONTRIBUTING.pt-BR.md)

MacroDeck accepts focused changes that clearly describe the behavior they add or fix.

## Development environment

- Windows
- .NET Framework 4.8 Developer Pack
- Visual Studio or the .NET Framework C# compiler

Build with `Source/build.ps1` or open `Source/MacroDeck.csproj` in Visual Studio. Before submitting a change, review the diff and confirm that it builds on Windows.

## Versioning and releases

The public repository receives only changes selected and reviewed for a release. Local development can continue without publishing a new public version for each adjustment. Releases use SemVer (`MAJOR.MINOR.PATCH`): compatible feature updates increment MINOR; compatible fixes increment PATCH; breaking changes increment MAJOR. Each release should update `CHANGELOG.md`, the assembly version, and the Git tag. The published binary targets Windows.

## Current scope

The program depends on Windows Forms and Windows input APIs. Do not describe support for other operating systems as available without implementing and validating it.

---

[Project README](README.md) · [Português (Brasil)](CONTRIBUTING.pt-BR.md)
