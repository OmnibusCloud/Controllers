using System.Collections;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Interfaces;

/// <summary>
/// One solver family as the sweep host sees it: how its block of the options
/// is validated, how a chunk of variants becomes the tasks its node activity
/// takes, how the results the nodes return become manifest rows, and which
/// artifacts a row offers a document client. Everything else - the plan, the
/// chunking, the state, the manifest, the index - is the same for every
/// family and never asks which one it is.
/// </summary>
internal interface ISweepFamily
{
    #region Functions

    /// <summary>
    /// Validates the family's block of a study, and the study's parameters
    /// against it, before any node sees a task.
    /// </summary>
    /// <param name="options">The study; its family block is this family's.</param>
    /// <param name="blobService">Reads the blobs the check needs (a base deck, a case's templated files).</param>
    /// <returns>Findings, one sentence each; empty when the study may run.</returns>
    Task<IReadOnlyList<string>> ValidateAsync(SweepOptionsData options, IWitBlobService blobService);

    /// <summary>
    /// The tasks of one chunk, of the type the family's node activity takes.
    /// </summary>
    /// <param name="options">The validated study.</param>
    /// <param name="variants">The chunk's variants, in table order.</param>
    /// <param name="blobService">Reads and writes the blobs the tasks reference.</param>
    /// <returns>One task per variant, each of <see cref="TaskType"/>.</returns>
    Task<IReadOnlyList<object>> MakeTasksAsync(SweepOptionsData options, IReadOnlyList<SweepVariantData> variants, IWitBlobService blobService);

    /// <summary>
    /// The manifest rows of a harvested wave: the sweep's verdict and the
    /// node's result, verbatim. Entries of another type are left out - the
    /// harvest's count check turns that into a loud failure.
    /// </summary>
    /// <param name="wave">The results as the grid returned them (completion order).</param>
    /// <returns>One row per result of this family.</returns>
    IReadOnlyList<SweepManifestRowData> ToRows(IEnumerable wave);

    /// <summary>
    /// The downloadable artifacts of a row, for the result index.
    /// </summary>
    /// <param name="row">A row this family made.</param>
    /// <returns>The artifacts by kind; empty when the run produced none.</returns>
    IReadOnlyList<SweepArtifactData> ArtifactsOf(SweepManifestRowData row);

    #endregion

    #region Properties

    /// <summary>The family.</summary>
    SweepFamily Family { get; }

    /// <summary>The task type the family's node activity takes; the script's task collection must hold it.</summary>
    Type TaskType { get; }

    /// <summary>The bundled script that runs a study of this family.</summary>
    string ScriptName { get; }

    #endregion
}
