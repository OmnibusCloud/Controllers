using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;

namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// The structural pre-check of a lean4export NDJSON export, format 3.1.0 (spec section 5.4), one
/// line at a time. It accepts only the exporter's canonical form: every key the format names, in
/// the exporter's (sorted) order and no other; every name, level and expression index bound once,
/// densely and in order, and used only after it is bound; no entry repeated; no declaration given
/// twice; every theorem of the manifest declared as a theorem. Its memory is a hash per name, a
/// bit per name and, when duplicates are looked for, a fingerprint per entry.
/// </summary>
public sealed class ExportPrecheck
{
    #region Constants

    private const byte TAG_NAME_STR = 1;
    private const byte TAG_NAME_NUM = 2;
    private const byte TAG_LEVEL_SUCC = 3;
    private const byte TAG_LEVEL_MAX = 4;
    private const byte TAG_LEVEL_IMAX = 5;
    private const byte TAG_LEVEL_PARAM = 6;
    private const byte TAG_BVAR = 10;
    private const byte TAG_SORT = 11;
    private const byte TAG_CONST = 12;
    private const byte TAG_APP = 13;
    private const byte TAG_LAM = 14;
    private const byte TAG_FORALL = 15;
    private const byte TAG_LET = 16;
    private const byte TAG_PROJ = 17;
    private const byte TAG_NAT = 18;
    private const byte TAG_STR = 19;
    private const byte TAG_MDATA = 20;
    private const byte TAG_AXIOM = 30;
    private const byte TAG_DEF = 31;
    private const byte TAG_THM = 32;
    private const byte TAG_OPAQUE = 33;
    private const byte TAG_QUOT = 34;
    private const byte TAG_INDUCTIVE = 35;

    private const int FAMILY_NONE = 0;
    private const int FAMILY_NAME = 1;
    private const int FAMILY_LEVEL = 2;
    private const int FAMILY_EXPR = 3;

    private const string EXPORTER = "lean4export";
    private const string FORMAT_PREFIX = "3.1.";

    #endregion

    #region Fields

    private readonly PrecheckOptions m_options;

    private readonly FingerprintSet? m_fingerprints;

    private readonly ulong[] m_theoremHashes;

    private readonly bool[] m_theoremFound;

    private readonly long[] m_tagCounts = new long[64];

    private ulong[] m_nameHashes = new ulong[1 << 16];

    private ulong[] m_declared = new ulong[1 << 10];

    private ushort[] m_depths = new ushort[1 << 16];

    private int m_childDepth;

    private byte[] m_scratch = new byte[1 << 10];

    private byte[] m_text = new byte[1 << 10];

    private int m_length;

    private long m_names = 1;

    private long m_levels = 1;

    private long m_exprs;

    private long m_line;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a pre-check.
    /// </summary>
    /// <param name="options">Its limits and expectations.</param>
    public ExportPrecheck(PrecheckOptions options)
    {
        m_options = options;
        m_fingerprints = options.CheckDuplicates ? new FingerprintSet() : null;
        m_theoremHashes = options.Theorems.Select(HashOfFullName).ToArray();
        m_theoremFound = new bool[m_theoremHashes.Length];
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks one line (without its newline).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <exception cref="PrecheckRefusalException">The line breaks a rule.</exception>
    public void CheckLine(ReadOnlySpan<byte> line)
    {
        m_line++;
        if (m_line == 1)
        {
            CheckMeta(line);
            return;
        }
        var reader = new Utf8JsonReader(line, new JsonReaderOptions { MaxDepth = m_options.MaxJsonDepth });
        try
        {
            CheckEntry(ref reader, line);
        }
        catch (PrecheckRefusalException error)
        {
            throw new PrecheckRefusalException(error.Code, $"line {m_line}: {error.Detail}");
        }
        catch (JsonException error)
        {
            throw new PrecheckRefusalException("json_invalid", $"line {m_line}: {error.Message}");
        }
        catch (InvalidOperationException error)
        {
            throw new PrecheckRefusalException("json_invalid", $"line {m_line}: {error.Message}");
        }
    }

    /// <summary>
    /// The checks that need the whole export: something was declared, and every theorem of the
    /// manifest was declared as a theorem.
    /// </summary>
    /// <exception cref="PrecheckRefusalException">A rule is broken.</exception>
    public void Finish()
    {
        if (m_line == 0)
            throw new PrecheckRefusalException("meta_invalid", "the export is empty");
        for (var i = 0; i < m_theoremHashes.Length; i++)
        {
            if (!m_theoremFound[i])
                throw new PrecheckRefusalException("theorem_missing", $"'{m_options.Theorems[i]}' is not declared as a theorem in the export");
        }
    }

    private void CheckMeta(ReadOnlySpan<byte> line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line.ToArray());
        }
        catch (JsonException error)
        {
            throw Refuse("meta_invalid", "line 1: the first line is not JSON: " + error.Message);
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("meta", out var meta) || root.EnumerateObject().Count() != 1)
                throw Refuse("meta_invalid", "the first line is not the meta object");
            MetaExporter = Text(meta, "exporter", "name");
            MetaExporterVersion = Text(meta, "exporter", "version");
            MetaFormat = Text(meta, "format", "version");
            MetaLeanVersion = Text(meta, "lean", "version");
            MetaLeanGithash = Text(meta, "lean", "githash");
        }
        if (MetaExporter != EXPORTER)
            throw Refuse("meta_invalid", $"exporter '{MetaExporter}', not {EXPORTER}");
        if (MetaFormat is null || !MetaFormat.StartsWith(FORMAT_PREFIX, StringComparison.Ordinal))
            throw Refuse("meta_invalid", $"format '{MetaFormat}', not 3.1.x");
        if (m_options.LeanVersion is not null && MetaLeanVersion != m_options.LeanVersion)
            throw Refuse("meta_invalid", $"Lean '{MetaLeanVersion}', the manifest says {m_options.LeanVersion}");
        if (m_options.LeanGithash is not null && MetaLeanGithash != m_options.LeanGithash)
            throw Refuse("meta_invalid", $"Lean git hash '{MetaLeanGithash}', the manifest says {m_options.LeanGithash}");
    }

    private static string? Text(JsonElement meta, string outer, string inner)
    {
        if (meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty(outer, out var element)
            || element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(inner, out var value)
            || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    #endregion

    #region Entries

    private void CheckEntry(ref Utf8JsonReader r, scoped ReadOnlySpan<byte> line)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartObject)
            throw Refuse("json_invalid", "a line that is not one JSON object");
        m_length = 0;
        m_childDepth = 0;
        var family = FAMILY_NONE;
        long index = -1;
        byte tag = 0;
        scoped ReadOnlySpan<byte> previous = default;
        while (true)
        {
            Next(ref r);
            if (r.TokenType == JsonTokenType.EndObject)
                break;
            var key = PropertyName(ref r, previous);
            previous = key;
            if (key.SequenceEqual("ie"u8) || key.SequenceEqual("il"u8) || key.SequenceEqual("in"u8))
            {
                if (family != FAMILY_NONE)
                    throw Refuse("json_invalid", "two index keys in one line");
                family = key[1] == (byte)'e' ? FAMILY_EXPR : key[1] == (byte)'l' ? FAMILY_LEVEL : FAMILY_NAME;
                index = (long)UInt(ref r);
                continue;
            }
            if (tag != 0)
                throw Refuse("json_invalid", $"the key '{Encoding.UTF8.GetString(key)}' after the line's kind (two kinds, or a key the format does not name)");
            tag = Kind(ref r, key, line);
        }
        if (r.Read())
            throw Refuse("json_invalid", "content after the object");
        if (tag == 0)
            throw Refuse("json_invalid", "a line of no known kind");
        m_tagCounts[tag]++;
        var expected = tag < TAG_LEVEL_SUCC ? FAMILY_NAME : tag < TAG_BVAR ? FAMILY_LEVEL : tag < TAG_AXIOM ? FAMILY_EXPR : FAMILY_NONE;
        if (family != expected)
            throw Refuse("json_invalid", "an index key that does not fit the line's kind");
        switch (family)
        {
            case FAMILY_NAME:
                Bind(index, ref m_names, "name");
                BindName(index);
                break;
            case FAMILY_LEVEL:
                Bind(index, ref m_levels, "level");
                break;
            case FAMILY_EXPR:
                Bind(index, ref m_exprs, "expression");
                BindDepth(index);
                break;
        }
        if (family != FAMILY_NONE && m_fingerprints is not null && !m_fingerprints.Add(XxHash3.HashToUInt64(m_scratch.AsSpan(0, m_length))))
        {
            DuplicateEntries++;
            if (m_options.StopAtFirst)
                throw Refuse("entry_duplicate", $"the {FamilyName(family)} bound as {index} repeats an earlier entry");
        }
    }

    private byte Kind(ref Utf8JsonReader r, scoped ReadOnlySpan<byte> key, scoped ReadOnlySpan<byte> line)
    {
        switch (key.Length > 0 ? key[0] : (byte)0)
        {
            case (byte)'a':
                if (key.SequenceEqual("app"u8))
                {
                    Begin(ref r, TAG_APP);
                    Key(ref r, "arg"u8); ExprRef(ref r);
                    Key(ref r, "fn"u8); ExprRef(ref r);
                    End(ref r);
                    return TAG_APP;
                }
                if (key.SequenceEqual("axiom"u8))
                {
                    Begin(ref r, TAG_AXIOM);
                    Key(ref r, "isUnsafe"u8); Bool(ref r);
                    Key(ref r, "levelParams"u8); Names(ref r);
                    Key(ref r, "name"u8); Declare(NameRef(ref r));
                    Key(ref r, "type"u8); ExprRef(ref r);
                    End(ref r);
                    Axioms++;
                    Constants++;
                    return TAG_AXIOM;
                }
                break;
            case (byte)'b':
                if (key.SequenceEqual("bvar"u8))
                {
                    Append(TAG_BVAR);
                    Append(UInt(ref r));
                    return TAG_BVAR;
                }
                break;
            case (byte)'c':
                if (key.SequenceEqual("const"u8))
                {
                    Begin(ref r, TAG_CONST);
                    Key(ref r, "name"u8); NameRef(ref r);
                    Key(ref r, "us"u8); Levels(ref r);
                    End(ref r);
                    return TAG_CONST;
                }
                break;
            case (byte)'d':
                if (key.SequenceEqual("def"u8))
                {
                    Begin(ref r, TAG_DEF);
                    Key(ref r, "all"u8); Names(ref r);
                    Key(ref r, "hints"u8); Hints(ref r);
                    Key(ref r, "levelParams"u8); Names(ref r);
                    Key(ref r, "name"u8); Declare(NameRef(ref r));
                    Key(ref r, "safety"u8); Enum(ref r, "partial"u8, "safe"u8, "unsafe"u8, default);
                    Key(ref r, "type"u8); ExprRef(ref r);
                    Key(ref r, "value"u8); ExprRef(ref r);
                    End(ref r);
                    Definitions++;
                    Constants++;
                    return TAG_DEF;
                }
                break;
            case (byte)'f':
                if (key.SequenceEqual("forallE"u8))
                {
                    Binder(ref r, TAG_FORALL);
                    return TAG_FORALL;
                }
                break;
            case (byte)'i':
                if (key.SequenceEqual("imax"u8))
                {
                    Append(TAG_LEVEL_IMAX);
                    LevelPair(ref r);
                    return TAG_LEVEL_IMAX;
                }
                if (key.SequenceEqual("inductive"u8))
                {
                    Inductive(ref r);
                    return TAG_INDUCTIVE;
                }
                break;
            case (byte)'l':
                if (key.SequenceEqual("lam"u8))
                {
                    Binder(ref r, TAG_LAM);
                    return TAG_LAM;
                }
                if (key.SequenceEqual("letE"u8))
                {
                    Begin(ref r, TAG_LET);
                    Key(ref r, "body"u8); ExprRef(ref r);
                    Key(ref r, "name"u8); NameRef(ref r);
                    Key(ref r, "nondep"u8); Bool(ref r);
                    Key(ref r, "type"u8); ExprRef(ref r);
                    Key(ref r, "value"u8); ExprRef(ref r);
                    End(ref r);
                    return TAG_LET;
                }
                break;
            case (byte)'m':
                if (key.SequenceEqual("max"u8))
                {
                    Append(TAG_LEVEL_MAX);
                    LevelPair(ref r);
                    return TAG_LEVEL_MAX;
                }
                if (key.SequenceEqual("mdata"u8))
                {
                    Begin(ref r, TAG_MDATA);
                    Key(ref r, "data"u8);
                    Next(ref r);
                    if (r.TokenType != JsonTokenType.StartObject)
                        throw Refuse("json_invalid", "mdata's data is not an object");
                    var start = (int)r.TokenStartIndex;
                    r.Skip();
                    Append(line.Slice(start, (int)r.BytesConsumed - start));
                    Key(ref r, "expr"u8); ExprRef(ref r);
                    End(ref r);
                    return TAG_MDATA;
                }
                break;
            case (byte)'n':
                if (key.SequenceEqual("natVal"u8))
                {
                    Append(TAG_NAT);
                    Digits(ref r);
                    return TAG_NAT;
                }
                if (key.SequenceEqual("num"u8))
                {
                    Begin(ref r, TAG_NAME_NUM);
                    Key(ref r, "i"u8); Append(UInt(ref r));
                    Key(ref r, "pre"u8); NameRef(ref r);
                    End(ref r);
                    return TAG_NAME_NUM;
                }
                break;
            case (byte)'o':
                if (key.SequenceEqual("opaque"u8))
                {
                    Begin(ref r, TAG_OPAQUE);
                    Key(ref r, "all"u8); Names(ref r);
                    Key(ref r, "isUnsafe"u8); Bool(ref r);
                    Key(ref r, "levelParams"u8); Names(ref r);
                    Key(ref r, "name"u8); Declare(NameRef(ref r));
                    Key(ref r, "type"u8); ExprRef(ref r);
                    Key(ref r, "value"u8); ExprRef(ref r);
                    End(ref r);
                    Opaques++;
                    Constants++;
                    return TAG_OPAQUE;
                }
                break;
            case (byte)'p':
                if (key.SequenceEqual("param"u8))
                {
                    Append(TAG_LEVEL_PARAM);
                    NameRef(ref r);
                    return TAG_LEVEL_PARAM;
                }
                if (key.SequenceEqual("proj"u8))
                {
                    Begin(ref r, TAG_PROJ);
                    Key(ref r, "idx"u8); Append(UInt(ref r));
                    Key(ref r, "struct"u8); ExprRef(ref r);
                    Key(ref r, "typeName"u8); NameRef(ref r);
                    End(ref r);
                    return TAG_PROJ;
                }
                break;
            case (byte)'q':
                if (key.SequenceEqual("quot"u8))
                {
                    Begin(ref r, TAG_QUOT);
                    Key(ref r, "kind"u8); Enum(ref r, "ctor"u8, "ind"u8, "lift"u8, "type"u8);
                    Key(ref r, "levelParams"u8); Names(ref r);
                    Key(ref r, "name"u8); Declare(NameRef(ref r));
                    Key(ref r, "type"u8); ExprRef(ref r);
                    End(ref r);
                    Quotients++;
                    Constants++;
                    return TAG_QUOT;
                }
                break;
            case (byte)'s':
                if (key.SequenceEqual("sort"u8))
                {
                    Append(TAG_SORT);
                    LevelRef(ref r);
                    return TAG_SORT;
                }
                if (key.SequenceEqual("str"u8))
                {
                    Begin(ref r, TAG_NAME_STR);
                    Key(ref r, "pre"u8); NameRef(ref r);
                    Key(ref r, "str"u8); String(ref r, false);
                    End(ref r);
                    return TAG_NAME_STR;
                }
                if (key.SequenceEqual("strVal"u8))
                {
                    Append(TAG_STR);
                    String(ref r, true);
                    return TAG_STR;
                }
                if (key.SequenceEqual("succ"u8))
                {
                    Append(TAG_LEVEL_SUCC);
                    LevelRef(ref r);
                    return TAG_LEVEL_SUCC;
                }
                break;
            case (byte)'t':
                if (key.SequenceEqual("thm"u8))
                {
                    Begin(ref r, TAG_THM);
                    Key(ref r, "all"u8); Names(ref r);
                    Key(ref r, "levelParams"u8); Names(ref r);
                    Key(ref r, "name"u8);
                    var name = NameRef(ref r);
                    Declare(name);
                    Key(ref r, "type"u8); ExprRef(ref r);
                    Key(ref r, "value"u8); ExprRef(ref r);
                    End(ref r);
                    Theorems++;
                    Constants++;
                    MarkTheorem(name);
                    return TAG_THM;
                }
                break;
        }
        throw Refuse("json_invalid", $"an unknown key '{Encoding.UTF8.GetString(key)}'");
    }

    private void Binder(ref Utf8JsonReader r, byte tag)
    {
        Begin(ref r, tag);
        Key(ref r, "binderInfo"u8); Enum(ref r, "default"u8, "implicit"u8, "instImplicit"u8, "strictImplicit"u8);
        Key(ref r, "body"u8); ExprRef(ref r);
        Key(ref r, "name"u8); NameRef(ref r);
        Key(ref r, "type"u8); ExprRef(ref r);
        End(ref r);
    }

    private void Inductive(ref Utf8JsonReader r)
    {
        Begin(ref r, TAG_INDUCTIVE);
        Key(ref r, "ctors"u8);
        Constructors += Array(ref r, Constructor);
        Key(ref r, "recs"u8);
        Recursors += Array(ref r, Recursor);
        Key(ref r, "types"u8);
        var types = Array(ref r, InductiveType);
        if (types == 0)
            throw Refuse("json_invalid", "an inductive block without types");
        InductiveTypes += types;
        End(ref r);
        InductiveBlocks++;
    }

    private void Constructor(ref Utf8JsonReader r)
    {
        BeginObject(ref r);
        Key(ref r, "cidx"u8); UInt(ref r);
        Key(ref r, "induct"u8); NameRef(ref r);
        Key(ref r, "isUnsafe"u8); Bool(ref r);
        Key(ref r, "levelParams"u8); Names(ref r);
        Key(ref r, "name"u8); Declare(NameRef(ref r));
        Key(ref r, "numFields"u8); UInt(ref r);
        Key(ref r, "numParams"u8); UInt(ref r);
        Key(ref r, "type"u8); ExprRef(ref r);
        End(ref r);
        Constants++;
    }

    private void Recursor(ref Utf8JsonReader r)
    {
        BeginObject(ref r);
        Key(ref r, "all"u8); Names(ref r);
        Key(ref r, "isUnsafe"u8); Bool(ref r);
        Key(ref r, "k"u8); Bool(ref r);
        Key(ref r, "levelParams"u8); Names(ref r);
        Key(ref r, "name"u8); Declare(NameRef(ref r));
        Key(ref r, "numIndices"u8); UInt(ref r);
        Key(ref r, "numMinors"u8); UInt(ref r);
        Key(ref r, "numMotives"u8); UInt(ref r);
        Key(ref r, "numParams"u8); UInt(ref r);
        Key(ref r, "rules"u8); Array(ref r, Rule);
        Key(ref r, "type"u8); ExprRef(ref r);
        End(ref r);
        Constants++;
    }

    private void Rule(ref Utf8JsonReader r)
    {
        BeginObject(ref r);
        Key(ref r, "ctor"u8); NameRef(ref r);
        Key(ref r, "nfields"u8); UInt(ref r);
        Key(ref r, "rhs"u8); ExprRef(ref r);
        End(ref r);
    }

    private void InductiveType(ref Utf8JsonReader r)
    {
        BeginObject(ref r);
        Key(ref r, "all"u8); Names(ref r);
        Key(ref r, "ctors"u8); Names(ref r);
        Key(ref r, "isRec"u8); Bool(ref r);
        Key(ref r, "isReflexive"u8); Bool(ref r);
        Key(ref r, "isUnsafe"u8); Bool(ref r);
        Key(ref r, "levelParams"u8); Names(ref r);
        Key(ref r, "name"u8); Declare(NameRef(ref r));
        Key(ref r, "numIndices"u8); UInt(ref r);
        Key(ref r, "numNested"u8); UInt(ref r);
        Key(ref r, "numParams"u8); UInt(ref r);
        Key(ref r, "type"u8); ExprRef(ref r);
        End(ref r);
        Constants++;
    }

    private void Hints(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType == JsonTokenType.String)
        {
            if (r.ValueIsEscaped)
                throw Refuse("json_invalid", "an escaped hint");
            if (r.ValueSpan.SequenceEqual("opaque"u8))
                Append((byte)1);
            else if (r.ValueSpan.SequenceEqual("abbrev"u8))
                Append((byte)2);
            else
                throw Refuse("json_invalid", "an unknown reducibility hint");
            return;
        }
        if (r.TokenType != JsonTokenType.StartObject)
            throw Refuse("json_invalid", "a hint that is neither a word nor an object");
        Append((byte)3);
        Key(ref r, "regular"u8); Append(UInt(ref r));
        End(ref r);
    }

    #endregion

    #region Values

    private delegate void ElementReader(ref Utf8JsonReader r);

    private int Array(ref Utf8JsonReader r, ElementReader element)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartArray)
            throw Refuse("json_invalid", "an array was expected");
        var count = 0;
        while (true)
        {
            var copy = r;
            Next(ref copy);
            if (copy.TokenType == JsonTokenType.EndArray)
            {
                r = copy;
                return count;
            }
            element(ref r);
            count++;
        }
    }

    private void Names(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartArray)
            throw Refuse("json_invalid", "an array of names was expected");
        var count = 0;
        while (true)
        {
            Next(ref r);
            if (r.TokenType == JsonTokenType.EndArray)
                break;
            Append(CheckRef(Number(ref r), m_names, "name"));
            count++;
        }
        Append((ulong)count);
    }

    private void Levels(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartArray)
            throw Refuse("json_invalid", "an array of levels was expected");
        var count = 0;
        while (true)
        {
            Next(ref r);
            if (r.TokenType == JsonTokenType.EndArray)
                break;
            Append(CheckRef(Number(ref r), m_levels, "level"));
            count++;
        }
        Append((ulong)count);
    }

    private void LevelPair(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartArray)
            throw Refuse("json_invalid", "a pair of levels was expected");
        LevelRef(ref r);
        LevelRef(ref r);
        Next(ref r);
        if (r.TokenType != JsonTokenType.EndArray)
            throw Refuse("json_invalid", "a pair of levels was expected");
    }

    private ulong NameRef(ref Utf8JsonReader r)
    {
        var value = CheckRef(UInt(ref r), m_names, "name");
        Append(value);
        return value;
    }

    private void LevelRef(ref Utf8JsonReader r)
    {
        Append(CheckRef(UInt(ref r), m_levels, "level"));
    }

    private void ExprRef(ref Utf8JsonReader r)
    {
        var value = CheckRef(UInt(ref r), m_exprs, "expression");
        Append(value);
        m_childDepth = Math.Max(m_childDepth, m_depths[(long)value]);
    }

    private ulong CheckRef(ulong value, long bound, string what)
    {
        if (value >= (ulong)bound)
            throw Refuse("index_invalid", $"{what} {value} is used before it is bound (next: {bound})");
        return value;
    }

    private static ulong UInt(ref Utf8JsonReader r)
    {
        Next(ref r);
        return Number(ref r);
    }

    private static ulong Number(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.Number || !r.TryGetUInt64(out var value))
            throw Refuse("json_invalid", "a number out of range, or not a natural number");
        return value;
    }

    private void Bool(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType == JsonTokenType.True)
            Append((byte)1);
        else if (r.TokenType == JsonTokenType.False)
            Append((byte)0);
        else
            throw Refuse("json_invalid", "a boolean was expected");
    }

    private void Enum(ref Utf8JsonReader r, ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c, ReadOnlySpan<byte> d)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.String || r.ValueIsEscaped)
            throw Refuse("json_invalid", "a word was expected");
        var value = r.ValueSpan;
        byte code = value.SequenceEqual(a) ? (byte)1 : value.SequenceEqual(b) ? (byte)2
            : value.SequenceEqual(c) ? (byte)3 : (!d.IsEmpty && value.SequenceEqual(d)) ? (byte)4 : (byte)0;
        if (code == 0)
            throw Refuse("json_invalid", $"an unknown word '{Encoding.UTF8.GetString(value)}'");
        Append(code);
    }

    private void String(ref Utf8JsonReader r, bool literal)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.String)
            throw Refuse("json_invalid", "a string was expected");
        ReadOnlySpan<byte> text;
        if (r.ValueIsEscaped)
        {
            if (m_text.Length < r.ValueSpan.Length)
                m_text = new byte[Math.Max(r.ValueSpan.Length, m_text.Length * 2)];
            var length = r.CopyString(m_text);
            text = m_text.AsSpan(0, length);
        }
        else
        {
            text = r.ValueSpan;
        }
        if (literal)
            LongestStringLiteral = Math.Max(LongestStringLiteral, text.Length);
        Append((ulong)text.Length);
        Append(text);
    }

    private void Digits(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.String || r.ValueIsEscaped)
            throw Refuse("json_invalid", "a number literal must be a string of digits");
        var digits = r.ValueSpan;
        if (digits.Length > m_options.MaxLiteralDigits)
            throw Refuse("literal_too_long", $"a number literal of {digits.Length} digits (limit {m_options.MaxLiteralDigits})");
        if (digits.Length == 0 || digits.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0)
            throw Refuse("json_invalid", "a number literal must be a string of digits");
        if (digits.Length > 1 && digits[0] == (byte)'0')
            throw Refuse("json_invalid", "a number literal with a leading zero");
        LargestNumberLiteral = Math.Max(LargestNumberLiteral, digits.Length);
        Append((ulong)digits.Length);
        Append(digits);
    }

    #endregion

    #region Tokens

    private static void Next(ref Utf8JsonReader r)
    {
        if (!r.Read())
            throw Refuse("json_invalid", "the line ends inside its object");
    }

    private static ReadOnlySpan<byte> PropertyName(ref Utf8JsonReader r, scoped ReadOnlySpan<byte> previous)
    {
        if (r.TokenType != JsonTokenType.PropertyName)
            throw Refuse("json_invalid", "a key was expected");
        if (r.ValueIsEscaped)
            throw Refuse("json_invalid", "an escaped key");
        var key = r.ValueSpan;
        if (!previous.IsEmpty && key.SequenceCompareTo(previous) <= 0)
            throw Refuse("json_invalid", "keys repeated or not in the exporter's order");
        return key;
    }

    private void Begin(ref Utf8JsonReader r, byte tag)
    {
        Append(tag);
        BeginObject(ref r);
    }

    private static void BeginObject(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.StartObject)
            throw Refuse("json_invalid", "an object was expected");
    }

    private static void Key(ref Utf8JsonReader r, ReadOnlySpan<byte> key)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.PropertyName || r.ValueIsEscaped || !r.ValueSpan.SequenceEqual(key))
            throw Refuse("json_invalid", $"the key '{Encoding.UTF8.GetString(key)}' was expected here");
    }

    private static void End(ref Utf8JsonReader r)
    {
        Next(ref r);
        if (r.TokenType != JsonTokenType.EndObject)
            throw Refuse("json_invalid", "a key the format does not name");
    }

    #endregion

    #region Bookkeeping

    private void Bind(long index, ref long next, string what)
    {
        if (index != next)
            throw Refuse("index_invalid", $"{what} index {index} where {next} is next (indices are bound once, densely, in order)");
        next++;
    }

    private void BindName(long index)
    {
        if (index >= m_nameHashes.Length)
            System.Array.Resize(ref m_nameHashes, m_nameHashes.Length * 2);
        // The scratch holds: tag, the prefix's index, then the component.
        var tag = m_scratch[0];
        var prefix = BinaryPrimitives.ReadUInt64LittleEndian(m_scratch.AsSpan(1, 8));
        var component = tag == TAG_NAME_NUM
            ? m_scratch.AsSpan(1, 8)
            : m_scratch.AsSpan(1 + 8 + 8, m_length - 17);
        if (tag == TAG_NAME_NUM)
            prefix = BinaryPrimitives.ReadUInt64LittleEndian(m_scratch.AsSpan(9, 8));
        m_nameHashes[index] = HashOfComponent(m_nameHashes[(long)prefix], tag, component);
    }

    private void BindDepth(long index)
    {
        if (index >= m_depths.Length)
            System.Array.Resize(ref m_depths, m_depths.Length * 2);
        var depth = Math.Min(m_childDepth + 1, ushort.MaxValue);
        m_depths[index] = (ushort)depth;
        if (depth > MaxTermDepth)
        {
            MaxTermDepth = depth;
            if (m_options.MaxTermDepth > 0 && depth > m_options.MaxTermDepth)
                throw Refuse("term_too_deep", $"expression {index} is {depth} deep (limit {m_options.MaxTermDepth})");
        }
    }

    private void Declare(ulong name)
    {
        var word = (long)(name >> 6);
        if (word >= m_declared.Length)
            System.Array.Resize(ref m_declared, (int)Math.Max(word + 1, m_declared.Length * 2L));
        var bit = 1UL << (int)(name & 63);
        if ((m_declared[word] & bit) != 0)
            throw Refuse("entry_duplicate", $"the constant of name {name} is declared twice");
        m_declared[word] |= bit;
    }

    private void MarkTheorem(ulong name)
    {
        var hash = m_nameHashes[(long)name];
        for (var i = 0; i < m_theoremHashes.Length; i++)
        {
            if (m_theoremHashes[i] == hash)
                m_theoremFound[i] = true;
        }
    }

    private static ulong HashOfFullName(string name)
    {
        ulong hash = 0;
        foreach (var part in name.Split('.'))
            hash = HashOfComponent(hash, TAG_NAME_STR, Encoding.UTF8.GetBytes(part));
        return hash;
    }

    private static ulong HashOfComponent(ulong prefix, byte tag, ReadOnlySpan<byte> component)
    {
        Span<byte> head = stackalloc byte[9];
        BinaryPrimitives.WriteUInt64LittleEndian(head, prefix);
        head[8] = tag;
        var hasher = new XxHash3();
        hasher.Append(head);
        hasher.Append(component);
        return hasher.GetCurrentHashAsUInt64();
    }

    private void Append(byte value)
    {
        Ensure(1);
        m_scratch[m_length++] = value;
    }

    private void Append(ulong value)
    {
        Ensure(8);
        BinaryPrimitives.WriteUInt64LittleEndian(m_scratch.AsSpan(m_length, 8), value);
        m_length += 8;
    }

    private void Append(ReadOnlySpan<byte> value)
    {
        Ensure(value.Length);
        value.CopyTo(m_scratch.AsSpan(m_length));
        m_length += value.Length;
    }

    private void Ensure(int more)
    {
        if (m_length + more > m_scratch.Length)
            System.Array.Resize(ref m_scratch, Math.Max(m_scratch.Length * 2, m_length + more));
    }

    private static string FamilyName(int family)
    {
        return family == FAMILY_NAME ? "name" : family == FAMILY_LEVEL ? "level" : "expression";
    }

    private static PrecheckRefusalException Refuse(string code, string detail)
    {
        return new PrecheckRefusalException(code, detail);
    }

    #endregion

    #region Properties

    /// <summary>Lines checked so far.</summary>
    public long Lines => m_line;

    /// <summary>Names bound (the anonymous name included).</summary>
    public long NameCount => m_names;

    /// <summary>Levels bound (zero included).</summary>
    public long LevelCount => m_levels;

    /// <summary>Expressions bound.</summary>
    public long ExpressionCount => m_exprs;

    /// <summary>Constants declared (an inductive block counts its types, constructors and recursors).</summary>
    public long Constants { get; private set; }

    /// <summary>Theorems declared.</summary>
    public long Theorems { get; private set; }

    /// <summary>Definitions declared.</summary>
    public long Definitions { get; private set; }

    /// <summary>Axioms declared.</summary>
    public long Axioms { get; private set; }

    /// <summary>Opaque constants declared.</summary>
    public long Opaques { get; private set; }

    /// <summary>Quotient constants declared.</summary>
    public long Quotients { get; private set; }

    /// <summary>Inductive blocks.</summary>
    public long InductiveBlocks { get; private set; }

    /// <summary>Inductive types.</summary>
    public long InductiveTypes { get; private set; }

    /// <summary>Constructors.</summary>
    public long Constructors { get; private set; }

    /// <summary>Recursors.</summary>
    public long Recursors { get; private set; }

    /// <summary>The digits of the largest number literal.</summary>
    public int LargestNumberLiteral { get; private set; }

    /// <summary>The bytes of the longest string literal.</summary>
    public int LongestStringLiteral { get; private set; }

    /// <summary>The longest chain of expressions (a term's depth as a graph, saturating at 65,535).</summary>
    public int MaxTermDepth { get; private set; }

    /// <summary>The bytes of the per-expression depths.</summary>
    public long DepthTableBytes => m_depths.LongLength * sizeof(ushort);

    /// <summary>Entries that repeat an earlier one (counted when the run does not stop at the first).</summary>
    public long DuplicateEntries { get; private set; }

    /// <summary>Fingerprints held (0 when duplicates are not looked for).</summary>
    public long FingerprintCount => m_fingerprints?.Count ?? 0;

    /// <summary>The bytes the fingerprints take.</summary>
    public long FingerprintBytes => m_fingerprints?.Bytes ?? 0;

    /// <summary>The bytes of the per-name tables.</summary>
    public long NameTableBytes => m_nameHashes.LongLength * sizeof(ulong) + m_declared.LongLength * sizeof(ulong);

    /// <summary>Lines of each kind, by the line's tag.</summary>
    public IReadOnlyList<long> TagCounts => m_tagCounts;

    /// <summary>The meta line's exporter name.</summary>
    public string? MetaExporter { get; private set; }

    /// <summary>The meta line's exporter version.</summary>
    public string? MetaExporterVersion { get; private set; }

    /// <summary>The meta line's format version.</summary>
    public string? MetaFormat { get; private set; }

    /// <summary>The meta line's Lean version.</summary>
    public string? MetaLeanVersion { get; private set; }

    /// <summary>The meta line's Lean git hash.</summary>
    public string? MetaLeanGithash { get; private set; }

    #endregion
}
