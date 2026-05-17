# OutWit.Controller.Variables

A core WitEngine controller that provides basic variable types and operations for the distributed computing system.

## Overview

This controller is a fundamental building block for WitEngine. It provides all primitive variable types, collections, and basic operations needed to work with data in WitEngine jobs.

## Dependencies

None. This is a base controller that other controllers depend on.

## Variable Types

The controller provides the following variable types:

### Primitive Types

| Type | Script Name | Description |
|------|-------------|-------------|
| Boolean | `Boolean` | True/false values |
| Byte | `Byte` | 8-bit unsigned integer (0-255) |
| SByte | `SByte` | 8-bit signed integer (-128 to 127) |
| Short | `Short` | 16-bit signed integer |
| UShort | `UShort` | 16-bit unsigned integer |
| Integer | `Int` | 32-bit signed integer |
| UInteger | `UInt` | 32-bit unsigned integer |
| Long | `Long` | 64-bit signed integer |
| ULong | `ULong` | 64-bit unsigned integer |
| Float | `Float` | Single-precision floating point |
| Double | `Double` | Double-precision floating point |
| Decimal | `Decimal` | High-precision decimal |
| String | `String` | Text string |

### Complex Types

| Type | Script Name | Description |
|------|-------------|-------------|
| DateTime | `DateTime` | Date and time value |
| DateTimeOffset | `DateTimeOffset` | Date/time with timezone |
| TimeSpan | `TimeSpan` | Duration/interval |
| Guid | `Guid` | Globally unique identifier |
| Color | `Color` | RGBA color value |
| Object | `Object` | Generic object container |
| Tuple | `Tuple` | Key-value pair |
| Array | `Array` | Generic array |
| ProcessingOptions | `ProcessingOptions` | Distributed processing configuration |

### Collections

Each primitive type has a corresponding collection type (e.g., `IntCollection`, `StringCollection`).

## Activities

### Value Assignment

Each variable type has an assignment activity with the same name:

```
Int:count = 42;
String:name = "Hello";
Boolean:flag = true;
Double:value = 3.14;
```

### Range Generation

Generate sequences of numbers:

```
IntCollection:numbers = IntRange(0, 100);
DoubleCollection:values = DoubleRange(0.0, 1.0, 0.1);
```

### Utility Activities

| Activity | Description | Example |
|----------|-------------|---------|
| `DateTimeNow()` | Current date/time | `DateTime:now = DateTimeNow();` |
| `DateTimeOffsetNow()` | Current date/time with timezone | `DateTimeOffset:now = DateTimeOffsetNow();` |
| `NewGuid()` | Generate new GUID | `Guid:id = NewGuid();` |

## Usage Examples

### Basic Variable Declaration

```
Job:Example()
{
    Int:counter = 0;
    String:message = "Processing started";
    Double:threshold = 0.95;
    Boolean:isComplete = false;
}
```

### Working with Collections

```
Job:CollectionExample()
{
    IntCollection:numbers = IntRange(1, 10);
    StringCollection:names = StringCollection(["Alice", "Bob", "Charlie"]);
}
```

### Processing Options for Distributed Execution

```
Job:DistributedExample()
{
    ProcessingOptions:opts = ProcessingOptions();
    
    ~ Use opts with Grid.ForEach for distributed processing ~
}
```

## Project Structure

```
OutWit.Controller.Variables/
  Variables/           - Variable type definitions
  Collections/         - Collection type definitions  
  Activities/          - Activity definitions
  Adapters/            - Activity adapters (parsing and processing)
  Properties/          - Localized resources
  WitControllerVariablesModule.cs - Plugin entry point
```

## Creating Custom Variable Types

To create a custom variable type:

1. Create a class inheriting from `WitVariable<T>`
2. Add the `[Variable("TypeName")]` attribute
3. Implement serialization with `[MemoryPackable]`
4. Register in the module with `services.AddVariable<YourVariable>()`

See existing variable implementations for reference.
