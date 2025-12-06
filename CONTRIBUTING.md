<!-- Copyright (c) 2024 MPCoreDeveloper. Licensed under the MIT License. -->
# Contributing to SharpCoreDB

We welcome contributions to SharpCoreDB! This document outlines the process for contributing.

## Code of Conduct

This project follows a code of conduct to ensure a welcoming environment for all contributors.

## How to Contribute

### Reporting Issues

- Use the GitHub issue tracker
- Provide detailed steps to reproduce
- Include your environment (OS, .NET version, etc.)

### Submitting Changes

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Add tests if applicable
5. Ensure all tests pass: `dotnet test`
6. Commit your changes: `git commit -m 'Add my feature'`
7. Push to the branch: `git push origin feature/my-feature`
8. Submit a pull request

### Development Setup

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB
dotnet restore
dotnet build
dotnet test
```

### Coding Standards

- Follow C# coding conventions
- Use nullable reference types
- Write unit tests for new features
- Update documentation as needed

### Testing

- All changes must include appropriate tests
- Run the full test suite before submitting
- Include benchmarks for performance changes

## License

By contributing, you agree that your contributions will be licensed under the MIT License.