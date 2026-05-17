# OutWit.Controller.Grid

A WitEngine controller that provides distributed computing capabilities across multiple nodes.

## Overview

This controller enables workload distribution across multiple compute nodes in the WitCloud network. It handles task partitioning, node selection based on capabilities and benchmarks, and result aggregation.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher)
- `OutWit.Controller.Special` (version 1.0.0 or higher)

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
Job:MatrixProcessing()
{
    MatrixSparse:matrix = LoadMatrix("data.smat");
    IntCollection:rowIndices = IntRange(0, MatrixRowCount(matrix));
    VectorSparseCollection:processedRows;
    
    processedRows = Grid.ForEach(idx in rowIndices) => ProcessRow(matrix, idx);
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
  Activities/          - Grid activity definitions
  Adapters/            - Activity adapters
  Builders/            - Task building utilities
  Utils/               - Processing and exception utilities
  Properties/          - Localized resources
  WitControllerSpecialGrid.cs - Plugin entry point
```

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

When using the SDK version of WitEngine:
- Only local execution is supported (MAX_NODES = 1)
- Grid.ForEach will execute sequentially on the local machine
- Use the full WitEngine for actual distributed computing
