# OutWit.Controller.Special

A WitEngine controller that provides control flow and execution management activities.

## Overview

This controller adds programming constructs like loops, conditionals, parallel execution, and debugging utilities to WitEngine jobs. It enables complex workflow logic within distributed computing tasks.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher)

## Activities

### Control Flow

| Activity | Description |
|----------|-------------|
| `If` | Conditional execution |
| `Loop` | Repeat block a specified number of times |
| `ForEach` | Iterate over a collection |
| `Break` | Exit current loop |
| `Continue` | Skip to next iteration |

### Parallel Execution

| Activity | Description |
|----------|-------------|
| `Parallel.Invoke` | Execute multiple activities in parallel |
| `Parallel.ForEach` | Process collection items in parallel |

### Transform Operations

| Activity | Description |
|----------|-------------|
| `Transform.ForEach` | Transform each item in a collection |
| `Zip` | Combine two collections element-wise |

### Execution Control

| Activity | Description |
|----------|-------------|
| `Invoke` | Execute a block of activities |
| `Delayed` | Execute after a specified delay |
| `Timer` | Execute repeatedly at intervals |
| `Pause` | Pause job execution |
| `Return` | Return values from a job |

### Debugging

| Activity | Description |
|----------|-------------|
| `Trace` | Output debug messages |

## Usage Examples

### Conditional Execution

```
Job:ConditionalExample()
{
    Int:value = 42;
    Boolean:result = false;
    
    If(value > 10)
    {
        result = true;
        Trace("Value is greater than 10", false);
    }
}
```

### Loop Iteration

```
Job:LoopExample()
{
    Int:sum = 0;
    
    Loop(10)
    {
        sum = sum + 1;
    }
}
```

### ForEach Over Collection

```
Job:ForEachExample()
{
    IntCollection:numbers = IntRange(1, 5);
    Int:total = 0;
    
    ForEach(item in numbers)
    {
        total = total + item;
    }
}
```

### Parallel Execution

```
Job:ParallelExample()
{
    Int:result1 = 0;
    Int:result2 = 0;
    
    Parallel.Invoke
    {
        result1 = ExpensiveCalculation1();
        result2 = ExpensiveCalculation2();
    }
}
```

### Transform with Arrow Syntax

```
Job:TransformExample()
{
    IntCollection:input = IntRange(1, 10);
    IntCollection:output;
    
    output = ForEach(x in input) => MultiplyByTwo(x);
}
```

### Delayed Execution

```
Job:DelayedExample()
{
    Trace("Starting", false);
    
    Delayed(5000)
    {
        Trace("Executed after 5 seconds", false);
    }
}
```

### Return Values

```
Job:ReturnExample(Int:input)
{
    Int:result = input * 2;
    
    Return(result);
}
```

## Project Structure

```
OutWit.Controller.Special/
  Activities/          - Activity DTOs (WitActivitySpecialIf, WitActivitySpecialLoop, ...)
                       Note: Invoke/ParallelInvoke DTOs live in OutWit.Engine.Data —
                       this controller ships only the adapters for them.
  Adapters/            - Activity adapters — parse params + run the work
  Interfaces/          - Internal marker interface shared across adapters
  Utils/               - Exception-message helpers (ExceptionsUtils)
  Properties/          - Localized error strings (Resources.resx)
  build/               - Consumer-side MSBuild .targets shipped inside the nupkg
  WitControllerSpecialModule.cs - Plugin entry point (DI registrations)
```

This controller has no companion Model package — control-flow activities don't
carry shared DTOs.

## Creating Custom Control Flow Activities

To create a custom control flow activity:

1. For simple activities: inherit from `WitActivityFunction` or `WitActivityCommand`
2. For composite activities (with body): inherit from `WitActivityComposite`
3. For transform activities (with arrow syntax): inherit from `WitActivityTransform`
4. Add the `[Activity("Name")]` attribute
5. Create an adapter implementing `IWitActivityAdapter<T>`
6. Register in the module with `services.AddActivityAdapter<Activity, Adapter>()`

See existing implementations for reference.
