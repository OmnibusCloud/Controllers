# OutWit.Controller.Matrices

A WitEngine controller that provides matrix and vector operations optimized for distributed computing.

## Overview

This controller adds support for dense and sparse matrix/vector types along with operations commonly used in scientific computing, machine learning, and data processing. Operations are designed to work efficiently with the WitEngine distributed computing infrastructure.

## Dependencies

- `OutWit.Controller.Variables` (version 1.0.0 or higher)

## Variable Types

### Matrix Types

| Type | Script Name | Description |
|------|-------------|-------------|
| Matrix | `Matrix` | Dense matrix of double values |
| MatrixSparse | `MatrixSparse` | Sparse matrix (CSR format) |

### Vector Types

| Type | Script Name | Description |
|------|-------------|-------------|
| Vector | `Vector` | Dense vector of double values |
| VectorSparse | `VectorSparse` | Sparse vector |

### Collection Types

| Type | Script Name | Description |
|------|-------------|-------------|
| VectorCollection | `VectorCollection` | Collection of dense vectors |
| VectorSparseCollection | `VectorSparseCollection` | Collection of sparse vectors |

## Activities

> Script names use dot notation for grouped operations — `Matrix.RowCount`, not `MatrixRowCount`. The Gustavson operation lives at the top level as `GustavsonMultiply`.

### Constructors

| Activity | Signature | Description |
|----------|-----------|-------------|
| `Matrix` | `Matrix(rows, cols, data)` or `Matrix(data)` | Create dense matrix |
| `MatrixSparse` | `MatrixSparse(rows, cols, data)` or `MatrixSparse(data)` | Create sparse matrix from tuple data (`[rowIndex, columns]` rows OR `[row, col, value]` elements) |
| `Vector` | `Vector(data)` | Create dense vector |
| `VectorSparse` | `VectorSparse(data)` | Create sparse vector |
| `VectorCollection` | `VectorCollection(vectors)` | Wrap dense vectors |
| `VectorSparseCollection` | `VectorSparseCollection(vectors)` | Wrap sparse vectors |

### Matrix Information

| Activity | Signature |
|----------|-----------|
| `Matrix.RowCount` | `Matrix.RowCount(matrix) -> Int` |
| `Matrix.ColumnCount` | `Matrix.ColumnCount(matrix) -> Int` |

### Row/Column Access

| Activity | Signature |
|----------|-----------|
| `Matrix.GetRow` | `Matrix.GetRow(matrix, index) -> Vector or VectorSparse` |
| `Matrix.GetRows` | `Matrix.GetRows(matrix, startIndex, count) -> VectorCollection / VectorSparseCollection` |
| `Matrix.GetColumn` | `Matrix.GetColumn(matrix, index) -> Vector or VectorSparse` |
| `Matrix.GetColumns` | `Matrix.GetColumns(matrix, startIndex, count) -> VectorCollection / VectorSparseCollection` |

### Sparse Operations

| Activity | Signature |
|----------|-----------|
| `GustavsonMultiply` | `GustavsonMultiply(rowIndex, rowVector, matrix) -> VectorSparse` |

Annotated `[CanRunInParallelOnClient(true)]` — safe to schedule in parallel branches of the same job.

## Usage Examples

These examples assume `matrix` and `vector` variables have already been populated upstream (e.g. by another activity that produces them, or by job parameters). Matrices ships sample `.smat` files via the consumer build's asset resolver into `<module>/Resources/`; how a consumer surfaces those into a typed `MatrixSparse` is application-specific.

### Reading dimensions and rows

```
Job:Inspect(MatrixSparse:matrix)
{
    Int:rows = Matrix.RowCount(matrix);
    Int:cols = Matrix.ColumnCount(matrix);

    VectorSparse:firstRow = Matrix.GetRow(matrix, 0);
}
```

### Iterating rows

```
Job:ProcessRows(MatrixSparse:matrix)
{
    Int:rowCount = Matrix.RowCount(matrix);
    IntCollection:indices = IntRange(0, rowCount);

    VectorSparseCollection:results;
    results = ForEach(idx in indices) => Matrix.GetRow(matrix, idx);
}
```

### Distributed sparse multiply (A × B, row-parallel)

```
Job:DistributedMultiply(MatrixSparse:A, MatrixSparse:B)
{
    Int:rows = Matrix.RowCount(A);
    IntCollection:rowIndices = IntRange(0, rows);

    ~ One Gustavson row-multiply per remote node, results collected as sparse rows ~
    VectorSparseCollection:resultRows;
    resultRows = Grid.ForEach(idx in rowIndices) =>
        GustavsonMultiply(idx, Matrix.GetRow(A, idx), B);
}
```

`resultRows` is a per-row sparse-vector collection. There's no built-in "reassemble into a single sparse matrix" activity — most consumers continue processing rows in distributed form or aggregate externally.

### Gustavson row multiply, single-machine

```
Job:GustavsonExample(MatrixSparse:matrix)
{
    VectorSparse:row = Matrix.GetRow(matrix, 0);
    VectorSparse:result = GustavsonMultiply(0, row, matrix);
}
```

## Benchmarking

The `MatrixGustavsonMultiply` activity includes built-in benchmarking support:
- Unit: `gustavson-op@v1`
- Measures operations per second
- Uses embedded test datasets for consistent benchmarking across nodes

This allows WitEngine to optimally distribute matrix operations based on node performance.

## Project Structure

```
OutWit.Controller.Matrices/
  Variables/           - Matrix and vector variable wrappers
  Collections/         - Vector collection wrappers
  Activities/          - Activity DTOs (Matrix.RowCount, Matrix.GetRow, GustavsonMultiply, ...)
  Adapters/            - Activity adapters — parse + execute + benchmark hooks
  Interfaces/          - Internal marker interface shared across adapters
  Utils/               - Exception-message helpers
  Resources/           - .smat sample data files auto-staged into <module>/Resources/
                       (Tier-2: also declared as ControllerDataAsset for GH-release fetch)
  build/               - Consumer-side MSBuild .targets shipped inside the nupkg
  WitControllerMatricesModule.cs - Plugin entry point (DI registrations)

OutWit.Controller.Matrices.Model/
  WitMatrix.cs, WitMatrixSparse.cs    - Dense + CSR sparse matrix types
  WitVector.cs, WitVectorSparse.cs    - Dense + sparse vector types
  Interfaces/                          - IWitMatrix<T>, IWitVector<T>, etc.
  Math/                                - SparseGustavson algorithm
```

The Matrices controller depends on `Variables 1.0.0+` for `Int`, `Double`, `IntCollection`, etc.

## Data Model (OutWit.Controller.Matrices.Model)

The companion model project provides:
- `IWitMatrix<T>` / `IWitVector<T>` interfaces
- `WitMatrix<T>` / `WitMatrixSparse<T>` implementations
- `WitVector<T>` / `WitVectorSparse<T>` implementations
- `SparseGustavson` algorithm implementation
- MemoryPack serialization support

## Performance Considerations

1. **Sparse vs Dense**: Use sparse types when matrix density is below 10-20%
2. **Row-wise Access**: Sparse matrices use CSR format, row access is O(1)
3. **Column Access**: Column access on sparse matrices is O(nnz), consider transposing
4. **Benchmarking**: Run benchmarks on all node types for optimal task distribution
