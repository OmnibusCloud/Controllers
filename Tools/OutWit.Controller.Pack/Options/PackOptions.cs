namespace OutWit.Controller.Pack.Options
{
    public sealed class PackOptions
    {
        #region Properties

        public string ModuleDir { get; init; } = string.Empty;

        public string? OutputPath { get; init; }

        public bool AllowExternalUris { get; init; }

        public bool ShowHelp { get; init; }

        #endregion
    }
}
