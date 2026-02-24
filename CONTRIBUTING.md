# Contributing to AgentFlightRecorder.NET

Thank you for your interest in contributing! This document provides guidelines for contributing to the project.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a branch for your change
4. Make your changes
5. Run the tests: `dotnet test`
6. Push and open a pull request

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Any IDE with C# support (VS Code, Visual Studio, Rider)

## Building

```bash
dotnet build
```

## Running Tests

```bash
dotnet test
```

All tests must pass before a pull request can be merged.

## Code Style

- Follow existing patterns in the codebase
- Use `ArgumentNullException.ThrowIfNull` for public API parameter validation
- Add XML doc comments to all public types and members
- Keep `TreatWarningsAsErrors` enabled — fix all warnings before submitting

## Pull Request Guidelines

- Keep PRs focused — one logical change per PR
- Include tests for new functionality
- Update documentation if the public API changes
- Ensure the CI build passes

## Reporting Bugs

Open a GitHub issue with:
- A clear description of the problem
- Steps to reproduce
- Expected vs actual behavior
- .NET version and OS

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
