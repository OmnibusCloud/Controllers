namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Language family of a runtime module — drives the sandbox invocation shape
/// (argv layout, stdlib preopen, environment).
/// </summary>
public enum VerifyRuntimeLanguage
{
    Python = 0,
    JavaScript = 1
}
