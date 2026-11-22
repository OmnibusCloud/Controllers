namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>What a head of an image counts (<see cref="CorpusTestImageHead"/>).</summary>
internal enum CorpusTestImageHeadKind
{
    /// <summary>One byte: the members of an object, or 255 for a null object.</summary>
    Object,

    /// <summary>Four bytes: the items of a list, or -1 for a null list.</summary>
    List,

    /// <summary>Four bytes: the head of a string (the complement of its UTF-8 byte count, its UTF-16 character count, 0 for an empty string, -1 for a null one).</summary>
    Text,

    /// <summary>Four bytes: the character count that follows the byte count of a UTF-8 string.</summary>
    Chars
}
