# OutWit.Controller.Grid

An OmnibusCloud controller that provides distributed computing capabilities across multiple nodes.

## Overview

This controller enables workload distribution across multiple compute nodes in the OmnibusCloud network. It handles task partitioning, node selection based on capabilities and benchmarks, and result aggregation.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher) — for `Int` / `IntCollection` / `ProcessingOptions` types.

## Activities

### Grid.ForEach

Distributes iteration over a collection across available compute nodes.

```
Grid.ForEach(item in collection) => Transformer(item);
Grid.ForEach(item in collection, options) => Transformer(item);
```

**Parameters:**
- `item` - Iteration variable name
- `collection` - Collection to iterate over
- `options` - Optional `ProcessingOptions` for controlling distribution

**Returns:** Collection of transformed results

### Grid.Delegate

Runs a **single** activity on the **one most-suitable** (fastest compatible) node and
returns its result — `Grid.ForEach` without the iteration. Use it for an inherently
**sequential / heavy** step that cannot be fanned out (e.g. a physics-simulation bake)
and must run once on a capable worker before the rest of the job distributes.

```
result = Grid.Delegate => Activity(args);
result = Grid.Delegate(options) => Activity(args);
Grid.Delegate => Activity(args);            ~ procedural: no return value ~
```

**Parameters:**
- `options` - Optional `ProcessingOptions` (node selection)

**Returns:** the delegated activity's single result (or nothing, if it is not a function)

Like `Grid.ForEach`, node compatibility is keyed off the delegated activity's type, and
only the variables that activity actually references are forwarded to the node. The single
task is allocated to the fastest compatible node via the same `WitGridTaskAllocator`.

## How It Works

1. **Node Discovery**: Finds compatible nodes based on activity requirements
2. **Task Building**: Creates tasks for each item in the collection
3. **Task Allocation**: Distributes tasks across nodes using benchmark data
4. **Parallel Execution**: Runs tasks on all nodes simultaneously
5. **Result Aggregation**: Collects and combines results

## Usage Examples

### Basic Distributed Processing

```
Job:DistributedExample()
{
    IntCollection:numbers = IntRange(1, 1000);
    IntCollection:results;
    
    results = Grid.ForEach(n in numbers) => ExpensiveCalculation(n);
}
```

### With Processing Options

```
Job:ConfiguredDistribution()
{
    IntCollection:data = IntRange(1, 10000);
    IntCollection:results;
    
    ProcessingOptions:opts = ProcessingOptions();
    
    results = Grid.ForEach(item in data, opts) => ProcessItem(item);
}
```

### Processing Matrix Rows

```
Job:MatrixProcessing(MatrixSparse:matrix)
{
    IntCollection:rowIndices = IntRange(0, Matrix.RowCount(matrix));
    VectorSparseCollection:processedRows;

    processedRows = Grid.ForEach(idx in rowIndices) => Matrix.GetRow(matrix, idx);
}
```

## Processing Options

Control how work is distributed:

| Property | Description |
|----------|-------------|
| `Strategy` | `Balanced` (pre-allocate) or `Queued` (pull-based) |
| `MaxClients` | Maximum number of nodes to use |
| `CanRunInParallelOnClient` | Allow parallel execution within each node |

### Strategy: Balanced

Work is pre-calculated and distributed based on node benchmarks. Best for:
- Predictable workloads
- Stable node availability
- Minimizing total completion time

### Strategy: Queued

Nodes pull tasks from a central queue. Best for:
- Unpredictable task durations
- Dynamic node availability
- Self-balancing workloads

## Project Structure

```
OutWit.Controller.Grid/
  Activities/          - Grid.ForEach + Grid.Delegate DTOs
  Adapters/            - WitActivityAdapterGridForEach (fan-out) + WitActivityAdapterGridDelegate (single node)
  Builders/            - WitGridTaskBuilder — per-task variable scoping + work estimate (BuildTasks fan-out / BuildTask single)
  Interfaces/          - Internal marker interface shared with adapters
  Utils/               - Exception-message helpers
  Properties/          - Localized error strings (Resources.resx)
  build/               - Consumer-side MSBuild .targets shipped inside the nupkg
  WitControllerGridModule.cs - Plugin entry point (DI registrations)

OutWit.Controller.Grid.Model/
  WitGridTask.cs            - Per-task (activity + variables + estimated work)
  WitGridTaskGroup.cs       - A node's queue of tasks + rate / ETA accounting
  WitGridTaskAllocator.cs   - LPT-greedy schedule + "minimize node count" heuristic
```

Neither the Grid.ForEach nor the Grid.Delegate activity is MemoryPack-serialised — Grid is host-only (only `IWitControllerHost`, not `IWitControllerNode`), so these activities stay on the host and only their inner transformer crosses the wire per task. See the comment on `WitActivityGridForEach` for the design rationale.

## Architecture

```
Grid.ForEach
    |
    v
[Task Builder] --> Creates WitGridTask for each item
    |
    v
[Node Manager] --> Finds compatible nodes via IWitNodesManager
    |
    v
[Task Allocator] --> Distributes tasks based on benchmarks
    |
    v
[Parallel Execution] --> Sends tasks to nodes
    |
    v
[Result Aggregation] --> Combines results into output collection
```

## Requirements for Transformer Activities

Activities used with Grid.ForEach should:

1. Have `[Requirement*]` attributes if they need specific capabilities
2. Implement benchmarking for optimal task allocation
3. Be serializable for network transfer

## SDK Limitations

When using the dev-time OmnibusCloud SDK:
- Only local execution is supported (MAX_NODES = 1)
- Grid.ForEach will execute sequentially on the local machine
- Use the full OmnibusCloud runtime for actual distributed computing
