# Contributing

Contributions are welcome through pull requests. Please describe the user-visible or security impact of a change and include focused tests where practical.

Before submitting:

- run `dotnet build PlcRecipeStudio.slnx --configuration Release`;
- run `dotnet test PlcRecipe.Tests/PlcRecipe.Tests.csproj`;
- do not connect to or write to a physical PLC in tests;
- use temporary directories and mock PLC implementations;
- do not commit databases, settings files, API keys, passwords, publish output, or IDE caches.

Protocol changes must document the address/byte-order assumptions and include vectors or simulator tests. UI changes should check both light and dark themes and narrow-window layout behavior.
