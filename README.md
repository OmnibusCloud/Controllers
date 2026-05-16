# WitEngine Controller Examples

This repository contains example controllers for the WitEngine distributed computing system. These controllers demonstrate how to build, test, and package custom plugins for WitCloud.

## Overview

WitEngine is a distributed computing platform that enables workload distribution across heterogeneous devices - from smart TVs to servers. This repository provides:

- Working controller implementations as reference examples
- Patterns for activity and variable development
- Test suites demonstrating how to validate controller behavior
- Benchmarking examples for performance-based load balancing

## Getting Started

### Prerequisites

- .NET 10 SDK
- Visual Studio 2022 or later / JetBrains Rider / VS Code

### Installation

All WitEngine SDK packages are available on NuGet:

```bash
dotnet add package OutWit.Engine.Sdk
dotnet add package OutWit.Engine.Data
dotnet add package OutWit.Engine.Interfaces
```

### Running Tests

```bash
dotnet test
```

## Repository Structure

```
  Variables/                    # Basic types controller
    OutWit.Controller.Variables/        # Int, String, Bool, Collections
    OutWit.Controller.Variables.Model/  # Shared models
    OutWit.Controller.Variables.Tests/  # Test suite

  Special/                      # Utility operations controller
    OutWit.Controller.Special/          # Trace, Return, conditionals
    OutWit.Controller.Special.Tests/

  Grid/                         # Distributed computing controller
    OutWit.Controller.Grid/             # Grid.ForEach for parallel processing
    OutWit.Controller.Grid.Model/
    OutWit.Controller.Grid.Tests/

  Matrices/                     # Matrix operations controller
    OutWit.Controller.Matrices/         # Sparse matrix multiplication
    OutWit.Controller.Matrices.Model/   # Matrix data structures
    OutWit.Controller.Matrices.Tests/
```

## Creating Your Own Controller

### 1. Create a New Project

```bash
dotnet new classlib -n OutWit.Controller.MyController
cd OutWit.Controller.MyController
dotnet add package OutWit.Engine.Sdk
dotnet add package OutWit.Engine.Data
```

### 2. Define an Activity

```csharp
[Activity("MyActivity")]
[MemoryPackable]
public partial class WitActivityMyActivity : WitActivityFunction
{
    public IWitParameter? InputValue { get; init; }
    
    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitActivityMyActivity other)
            return false;
        return base.Is(other, tolerance) && InputValue.Check(other.InputValue);
    }

    protected override WitActivityMyActivity InnerClone()
    {
        return new WitActivityMyActivity { InputValue = InputValue?.Clone() as IWitParameter };
    }
}
```

### 3. Create an Adapter

```csharp
internal class WitActivityAdapterMyActivity : WitActivityAdapterFunction<WitActivityMyActivity>
{
    public WitActivityAdapterMyActivity(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    protected override async Task<object?> ProcessInner(
        WitActivityMyActivity activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.InputValue, out int input))
            throw new InvalidOperationException("Failed to get input");

        // Your processing logic here
        return input * 2;
    }

    protected override WitActivityMyActivity CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException("Expected 1 parameter");

        return new WitActivityMyActivity { InputValue = parameters[0] };
    }
}
```

### 4. Register in Controller Module

```csharp
public class WitControllerMyController : IWitControllerHost, IWitControllerNode
{
    public void Initialize(IServiceCollection services)
    {
        services.AddActivityAdapter<WitActivityMyActivity, WitActivityAdapterMyActivity>();
    }
}
```

### 5. Write Tests

```csharp
[TestFixture]
public class MyActivityTests
{
    [OneTimeSetUp]
    public void Setup()
    {
        WitEngineSdk.Instance.Reload();
    }

    [Test]
    public async Task MyActivity_ProcessesCorrectly()
    {
        var job = WitEngineSdk.Instance.Compile(@"
            Job:Test()
            {
                Int:result = MyActivity(21);
            }
        ");

        var status = await WitEngineSdk.Instance.ScheduleAndWaitAsync(job);
        
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
    }
}
```

## SDK Limitations

The SDK version has built-in limits for development purposes:

| Limit | Value |
|-------|-------|
| Max activities per job | 50 |
| Max variables per job | 100 |
| Max execution time | 5 minutes |
| Max nodes | 1 (local only) |
| Max variable size | 100 MB |

For production workloads, use the full WitEngine on WitCloud infrastructure.

## Included Controllers

### Variables Controller

Basic data types and operations:
- Primitive types: `Int`, `Double`, `Bool`, `String`
- Collections: `IntCollection`, `DoubleCollection`, `StringCollection`
- Operations: arithmetic, string manipulation, collection utilities

### Special Controller

Utility operations:
- `Trace(message)` - logging during job execution
- `Return(values)` - returning values from jobs
- Conditional execution support

### Grid Controller

Distributed computing primitives:
- `Grid.ForEach(item in collection) => Transformer(item)` - parallel processing across nodes

### Matrices Controller

Sparse matrix operations:
- Gustavson sparse matrix multiplication
- Row-by-matrix operations for distributed computation
- Benchmarking for optimal work distribution

## Benchmarking

Controllers can implement benchmarks for intelligent load balancing:

```csharp
public override async Task<IWitBenchmarkResult> RunBenchmark(
    IWitBenchmarkOptions? options, 
    CancellationToken cancellationToken)
{
    // Warm up
    for (int i = 0; i < options.WarmupIterations; i++)
        PerformOperation();

    // Measure
    var stopwatch = Stopwatch.StartNew();
    long iterations = 0;
    
    do
    {
        PerformOperation();
        iterations++;
    } while (stopwatch.Elapsed < options.MinDuration);
    
    stopwatch.Stop();
    
    return new WitBenchmarkResult
    {
        Rate = iterations / stopwatch.Elapsed.TotalSeconds,
        Unit = "operations/sec",
        Elapsed = stopwatch.Elapsed,
        Iterations = iterations
    };
}
```

## Related Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| OutWit.Engine.Interfaces | Core interfaces and contracts | [Link](https://www.nuget.org/packages/OutWit.Engine.Interfaces) |
| OutWit.Engine.Data | Data models and base classes | [Link](https://www.nuget.org/packages/OutWit.Engine.Data) |
| OutWit.Engine.Parser | Job script parser | [Link](https://www.nuget.org/packages/OutWit.Engine.Parser) |
| OutWit.Engine.Sdk | Development engine | [Link](https://www.nuget.org/packages/OutWit.Engine.Sdk) |

## License

This software is licensed under the **Non-Commercial License (NCL)**.

- Free for personal, educational, and research purposes
- Commercial use requires a separate license agreement
- Contact licensing@ratner.io for commercial licensing inquiries

See the full [LICENSE](LICENSE) file for details.

