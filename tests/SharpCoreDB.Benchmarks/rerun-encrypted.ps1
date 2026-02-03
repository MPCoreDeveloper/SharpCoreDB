# Re-run Encrypted Select Benchmark

# Build in Release mode
dotnet build -c Release

# Run only the PageBased_Encrypted_Select benchmark
dotnet run -c Release --no-build --filter *PageBased_Encrypted_Select*

# If that fails, run all Encrypted benchmarks
# dotnet run -c Release --no-build --filter *Encrypted*
