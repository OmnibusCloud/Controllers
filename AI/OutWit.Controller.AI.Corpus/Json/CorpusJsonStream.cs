using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.Json;

/// <summary>
/// Reads a JSON document whose root is an object from a stream without holding it: the
/// properties of the root are handed over one at a time, and the items of one named array one
/// at a time, each as an element of its own that is gone when the next is read. A manifest of a
/// million sources or a report of a million shards is read in the memory of its largest entry.
/// A single value (a property of the root that is not the array, or one item of the array) is
/// held while it is handed over and is bounded: a document with a larger one is refused.
/// </summary>
public static class CorpusJsonStream
{
    #region Constants

    /// <summary>The most bytes one value of a document may be: an item of the array, or a property of the root beside it.</summary>
    public const int MAX_VALUE_BYTES = 4 * 1024 * 1024;

    /// <summary>The message of the exception thrown for a document whose root is not an object.</summary>
    public const string NOT_AN_OBJECT = "The document must be a JSON object.";

    private const int FIRST_BUFFER_BYTES = 64 * 1024;

    private static readonly byte[] BOM = [0xEF, 0xBB, 0xBF];

    #endregion

    #region Functions

    /// <summary>Reads a document.</summary>
    /// <param name="stream">The document, UTF-8.</param>
    /// <param name="arrayName">The property of the root whose value is read item by item when it is an array.</param>
    /// <param name="onProperty">Receives every other property of the root, and the named one when its value is no array: its name and its value. The element is valid during the call only.</param>
    /// <param name="onItem">Receives every item of the named array: its index from 0 and the item. The element is valid during the call only.</param>
    /// <param name="options">How the document is read (comments, trailing commas).</param>
    /// <returns>True when the root holds the named property and its value is an array (an empty one included).</returns>
    /// <exception cref="JsonException">The stream is no JSON document, its root is no object, a property of the root appears twice, or a value is larger than <see cref="MAX_VALUE_BYTES"/>.</exception>
    public static bool Read(Stream stream, string arrayName, Action<string, JsonElement> onProperty, Action<int, JsonElement> onItem, JsonReaderOptions options = default)
    {
        var buffer = new byte[FIRST_BUFFER_BYTES];
        var filled = Fill(stream, buffer, 0, out var final);

        // A byte order mark is no part of the document.
        var start = filled >= BOM.Length && buffer.AsSpan(0, BOM.Length).SequenceEqual(BOM) ? BOM.Length : 0;
        if (start > 0)
        {
            buffer.AsSpan(start, filled - start).CopyTo(buffer);
            filled -= start;
        }

        var state = new JsonReaderState(options);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var where = Where.Start;
        var index = 0;
        var found = false;

        while (true)
        {
            var reader = new Utf8JsonReader(buffer.AsSpan(0, filled), final, state);
            var starved = false;

            while (where != Where.End && !starved)
            {
                // A unit of several tokens (a property with its value, an item) is taken whole or
                // not at all: when the buffer ends inside one, the reading goes back to its start.
                var checkpoint = reader;
                if (!reader.Read())
                {
                    starved = true;
                    break;
                }

                switch (where)
                {
                    case Where.Start:
                        if (reader.TokenType != JsonTokenType.StartObject)
                            throw new JsonException(NOT_AN_OBJECT);

                        where = Where.Root;
                        break;

                    case Where.Root when reader.TokenType == JsonTokenType.EndObject:
                        where = Where.End;
                        break;

                    case Where.Root:
                        var name = reader.GetString() ?? "";
                        if (name == arrayName)
                        {
                            // The value's first token says whether it is the array.
                            var afterName = reader;
                            if (!reader.Read())
                            {
                                reader = checkpoint;
                                starved = true;
                                break;
                            }

                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                Once(seen, name);
                                where = Where.Array;
                                found = true;
                                break;
                            }

                            reader = afterName;
                        }

                        if (!JsonDocument.TryParseValue(ref reader, out var value))
                        {
                            reader = checkpoint;
                            starved = true;
                            break;
                        }

                        using (value)
                        {
                            Once(seen, name);
                            onProperty(name, value.RootElement);
                        }

                        break;

                    case Where.Array when reader.TokenType == JsonTokenType.EndArray:
                        where = Where.Root;
                        break;

                    case Where.Array:
                        if (!JsonDocument.TryParseValue(ref reader, out var item))
                        {
                            reader = checkpoint;
                            starved = true;
                            break;
                        }

                        using (item)
                            onItem(index++, item.RootElement);

                        break;
                }
            }

            if (where == Where.End)
            {
                // Nothing but white space (and comments, when they are allowed) may follow the root.
                if (reader.Read())
                    throw new JsonException("The document holds more than one value.");

                if (final)
                    return found;
            }

            if (final && starved)
                throw new JsonException("The document ends before it is complete.");

            // Keep what was not taken, make room when one value fills the buffer, read on.
            var consumed = (int)reader.BytesConsumed;
            state = reader.CurrentState;
            buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
            filled -= consumed;

            if (filled == buffer.Length)
            {
                if (buffer.Length >= MAX_VALUE_BYTES)
                    throw new JsonException($"A value of the document is larger than {MAX_VALUE_BYTES} bytes.");

                Array.Resize(ref buffer, Math.Min(MAX_VALUE_BYTES, buffer.Length * 2));
            }

            filled = Fill(stream, buffer, filled, out final);
        }
    }

    #endregion

    #region Tools

    /// <summary>Fills the rest of a buffer from the stream; says whether the stream has ended.</summary>
    private static int Fill(Stream stream, byte[] buffer, int filled, out bool final)
    {
        final = false;
        while (filled < buffer.Length)
        {
            var read = stream.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                final = true;
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static void Once(HashSet<string> seen, string name)
    {
        if (!seen.Add(name))
            throw new JsonException($"The key '{name}' appears more than once.");
    }

    #endregion

    #region Types

    /// <summary>Where in the document the reading is.</summary>
    private enum Where
    {
        Start,
        Root,
        Array,
        End
    }

    #endregion
}
