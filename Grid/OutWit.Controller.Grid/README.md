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

#### What a node's answer is held to

A node is not trusted to answer only what it was asked, so Grid checks every answer before a value
joins the results. The rules are the same under both strategies; under `Balanced` an answer is a
node's batch, under `Queued` it is one task.

- **Asked names.** A task asks its node for one value: the one under the return slot Grid gave the
  task's own activity (`return0`, `return1`, ...). The slot is that activity's return reference, so a
  variable of the script is never a second slot, whatever its name: up to 1.3.0 a script or iteration
  variable whose name started with `return` was asked for too and came back as one more value of
  every task. A value is kept when it comes under a slot the answer's tasks asked for, as many times
  as they asked for it. A second value under one slot, and a value under any other name, are left out
  with a warning. A transformer that returns nothing gives its tasks no slot.
- **Refused values.** A typed collection takes a list whole or not at all: one null, or one value of
  another type than its elements, and the collection keeps what it had. So a value the return
  collection would refuse is left out as well, with a warning that says how many and why (null, or
  the type that came and the type that was expected). The first value under a slot is that task's
  answer: when it is refused the task has no answer, and a later value under the same slot does not
  stand in for it. A collection of objects (`ObjectCollection`) takes every value, null included. A
  call that is not assigned to a collection has no such rule.
- **Short results.** Leaving a value out never fails a batch and never sends a task again: the
  other values of the answer stand, and the collection comes back shorter than the list of items. The
  same holds for a node that returns fewer values than it had tasks. Results carry no item index, so a
  transformer has to return something that identifies its item, and a script that needs every item
  has to compare what came back with what it sent and run the rest again.
- **The loud failure.** After the values are stored, the collection is read back. If it does not hold
  them, the activity fails with a message that names the transformer, the collection, the number of
  values and the element type. With the rules above that should not happen; it is there so that a
  script never goes on with an empty collection, or with the values of an earlier call, when the
  collection and the check disagree. The collection is read back, and the engine's own answer is not
  relied on, because an engine before `OutWit.Engine.Data` 1.1.6 reports a refused list as taken.
- **Nothing to store.** A call that is not assigned (`Grid.ForEach(...) => Activity(...);`), a call
  assigned to a variable that is not a collection, and a transformer that returns nothing store
  nothing and fail nothing, as before.

A warning names the node by the status that came back with the answer. Without copies the server
may give a batch to another node than the one it was planned for; when the status does not say who
ran it, the warning names the planned node and says so.

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

The node is asked for one value, under the return slot Grid gave the delegated activity; a
variable of the script is never asked for, whatever its name. The result is the value that comes
back under that slot: a value under any other name, and a second value under the slot, are left
out with a warning (up to 1.3.0 the first value a node returned was the result, whatever its name).
When the return variable does not take the value (a typed variable refuses a value of another
type, and a variable of a value type refuses null), the variable keeps the value it had and a
warning says which kind of value came and which was expected. The job does not fail for it: a
script that must not go on with the old value has to check the variable itself.

### Grid.Replicas

Builds the options of a `Grid.ForEach` call whose selected items run more than once, as
optional redundancy for reliability. It runs on the host and needs nothing on the nodes.

```
ProcessingOptions:checked = Grid.Replicas(2, 0.05);   ~ 5 % of the items run twice ~
ProcessingOptions:all = Grid.Replicas(2);             ~ every item runs twice ~
results = Grid.ForEach(item in items, checked) => Transformer(item);
```

**Parameters:**
- `copies` - How many times a selected item runs, from 1 to 3
- `share` - Optional share of the items that are selected, from 0 to 1 (rounded up to a whole item); all of them when omitted

**What it guarantees:**
- The executions of one item run on **different machines** within the call, also after a node fails and its work moves. One computer registered under two accounts counts once. Different machines are not necessarily different owners.
- The platform picks the items at random and shuffles the tasks, so a node cannot tell whether the item it holds also runs elsewhere. Selected and unselected items must be in one call for that to hold: two calls reveal which is which.
- With too few machines an item gets fewer copies. A job fails only when an item that no machine has run has no machine left to run it.
- An execution whose value is left out (see *What a node's answer is held to*) is not run again. Its machine still counts as holding the item, because it has run it, so no other copy of that item is placed there. The item then comes back with fewer results than it had copies, or with none: the controller that compares the copies has to treat that as it treats a missing answer.

**What it does not do:**
- It does not compare results. `results` is a flat list with one entry per execution, in no particular order, so the transformer has to return something that identifies its item. The controller compares; the platform does not vote.
- It does not speed anything up: the copies are extra work.

Only the `Balanced` strategy supports copies; `Queued` reports an error. Copies are set in a script, because these options are not sent through SDK job parameters. A call with copies uses this cloud's own nodes only.

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

Every task is its own request, and its answer is held to the same rules as a batch's under
`Balanced` (see *What a node's answer is held to*): the value under the task's own slot, once, when
the return collection would take it. A task that fails ends the call; the values kept so far are
stored and the activity is reported as failed. Copies (`Grid.Replicas`) are not supported.

## Project Structure

```
OutWit.Controller.Grid/
  Activities/          - Grid.ForEach + Grid.Delegate DTOs
  Adapters/            - WitActivityAdapterGridForEach (fan-out) + WitActivityAdapterGridDelegate (single node)
  Builders/            - WitGridTaskBuilder — per-task variable scoping + work estimate (BuildTasks fan-out / BuildTask single)
  Interfaces/          - Internal marker interface shared with adapters
  Utils/               - GridReassignment (Balanced: allocate, run, reassign a failed node's tasks), ProcessingUtils (Queued, node requests),
                         GridAnswers (what a task asks for, what is kept of an answer), GridValueRule (what the return collection takes),
                         exception-message helpers
  Properties/          - Localized error strings (Resources.resx)
  build/               - Consumer-side MSBuild .targets shipped inside the nupkg
  WitControllerGridModule.cs - Plugin entry point (DI registrations)

OutWit.Controller.Grid.Model/
  WitGridTask.cs            - Per-task (activity + variables + estimated work)
  WitGridTaskGroup.cs       - A node's queue of tasks + rate / ETA accounting
  WitGridTaskAllocator.cs   - LPT-greedy schedule + "minimize node count" heuristic, keeps copies of an item apart
  WitGridReplicaPlanner.cs  - Which items run more than once, and the shuffled order of the executions
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
