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

### Matrix Information

| Activity | Description | Example |
|----------|-------------|---------|
| `MatrixRowCount(matrix)` | Get number of rows | `Int:rows = MatrixRowCount(m);` |
| `MatrixColumnCount(matrix)` | Get number of columns | `Int:cols = MatrixColumnCount(m);` |

### Row/Column Access

| Activity | Description |
|----------|-------------|
| `MatrixGetRow(matrix, index)` | Get single row as vector |
| `MatrixGetRows(matrix, startIndex, count)` | Get multiple rows |
| `MatrixGetColumn(matrix, index)` | Get single column as vector |
| `MatrixGetColumns(matrix, startIndex, count)` | Get multiple columns |

### Matrix Operations

| Activity | Description |
|----------|-------------|
| `MatrixGustavsonMultiply(rowIndex, rowVector, matrix)` | Sparse row-by-matrix multiplication using Gustavson algorithm |

### Constructors

| Activity | Description |
|----------|-------------|
| `Matrix(data)` | Create dense matrix |
| `MatrixSparse(data)` | Create sparse matrix |
| `Vector(data)` | Create dense vector |
| `VectorSparse(data)` | Create sparse vector |
| `VectorCollection(vectors)` | Create vector collection |
| `VectorSparseCollection(vectors)` | Create sparse vector collection |

## Usage Examples

### Basic Matrix Operations

```
Job:MatrixExample()
{
    MatrixSparse:matrix = LoadMatrix("input.smat");
    
    Int:rows = MatrixRowCount(matrix);
    Int:cols = MatrixColumnCount(matrix);
    
    Trace("Matrix size: " + rows + "x" + cols, false);
    
    VectorSparse:firstRow = MatrixGetRow(matrix, 0);
}
```

### Processing Matrix Rows

```
Job:ProcessRows()
{
    MatrixSparse:matrix = LoadMatrix("data.smat");
    Int:rowCount = MatrixRowCount(matrix);
    IntCollection:indices = IntRange(0, rowCount);
    
    VectorSparseCollection:results;
    
    results = ForEach(idx in indices) => MatrixGetRow(matrix, idx);
}
```

### Distributed Matrix Multiplication

```
Job:DistributedMultiply()
{
    MatrixSparse:A = LoadMatrix("A.smat");
    MatrixSparse:B = LoadMatrix("B.smat");
    
    Int:rows = MatrixRowCount(A);
    IntCollection:rowIndices = IntRange(0, rows);
    
    ~ Distribute row-by-matrix multiplication across nodes ~
    VectorSparseCollection:resultRows;
    
    resultRows = Grid.ForEach(idx in rowIndices) => 
        MatrixGustavsonMultiply(idx, MatrixGetRow(A, idx), B);
}
```

### Gustavson Sparse Matrix Multiplication

The `MatrixGustavsonMultiply` activity implements the Gustavson algorithm for sparse matrix multiplication. It is optimized for:
- Sparse matrices with low fill-in
- Row-wise parallel processing
- Memory-efficient computation

```
Job:GustavsonExample()
{
    MatrixSparse:matrix = LoadMatrix("sparse.smat");
    VectorSparse:row = MatrixGetRow(matrix, 0);
    
    VectorSparse:result;
    result = MatrixGustavsonMultiply(0, row, matrix);
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
  Variables/           - Matrix and vector variable types
  Collections/         - Vector collection types
  Activities/          - Matrix operation activities
  Adapters/            - Activity adapters with processing and benchmarking
  Properties/          - Localized resources
  WitControllerMatricesModule.cs - Plugin entry point
```

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
