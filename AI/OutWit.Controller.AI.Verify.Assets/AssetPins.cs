namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// Everything the runtime set is built from, pinned by SHA-256: a download that does not
/// match its pin is discarded. Bumping a runtime means changing a pin here and rebuilding.
/// </summary>
internal static class AssetPins
{
    #region Constants

    public const string PYTHON_VERSION = "3.14.6";
    public const string PYTHON_RUNTIME_DIR = "python-3.14.6";
    public const string PYTHON_LIB_DIR = "python3.14";
    public const string STDLIB_ZIP = "python314.zip";

    public const string SYMPY_VERSION = "1.14.0";
    public const string MPMATH_VERSION = "1.4.1";
    public const string SYMPY_ZIP = "sympy-1.14.0.zip";

    public const string QUICKJS_VERSION = "0.15.1";

    public const string RUBY_VERSION = "3.4.1";
    public const string RUBY_RUNTIME_DIR = "ruby-3.4.1";
    public const string RUBY_API_VERSION = "3.4.0";
    public const string RUBY_WASM_RELEASE = "2.10.1";

    /// <summary>The bundled gems a program is given without RubyGems: pure Ruby, their library folders only.</summary>
    public static readonly string[] RUBY_BUNDLED_GEMS = ["base64-0.2.0", "csv-3.3.2", "matrix-0.4.2", "prime-0.1.3"];

    #endregion

    #region Pins

    public static readonly AssetPin CPYTHON = new(
        "python-3.14.6-wasi_sdk-24.zip",
        "https://github.com/brettcannon/cpython-wasi-build/releases/download/v3.14.6/python-3.14.6-wasi_sdk-24.zip",
        "73bf2e9774c4d8820d0877ec5db0b963df3a9611fc2a63838aeaee29dfd034e6");

    public static readonly AssetPin QUICKJS = new(
        "qjs-wasi.wasm",
        "https://github.com/quickjs-ng/quickjs/releases/download/v0.15.1/qjs-wasi.wasm",
        "b4071ef2fbb2bb693c0bbcfc07cb9d28639fd9cea2fd986824a57aeac929817b");

    public static readonly AssetPin QUICKJS_LICENSE = new(
        "LICENSE-quickjs-ng.txt",
        "https://raw.githubusercontent.com/quickjs-ng/quickjs/v0.15.1/LICENSE",
        "96f73f9d2a16c21a36b418f06073be26e7d6d5e7c1bc99756b21a4f2c74ef171");

    public static readonly AssetPin SYMPY = new(
        "sympy-1.14.0-py3-none-any.whl",
        "https://files.pythonhosted.org/packages/py3/s/sympy/sympy-1.14.0-py3-none-any.whl",
        "e091cc3e99d2141a0ba2847328f5479b05d94a6635cb96148ccb3f34671bd8f5");

    public static readonly AssetPin MPMATH = new(
        "mpmath-1.4.1-py3-none-any.whl",
        "https://files.pythonhosted.org/packages/py3/m/mpmath/mpmath-1.4.1-py3-none-any.whl",
        "dc4f0ea2304480d4a9a48a94c1020571558ade522b44a6912efac63a586e140f");

    public static readonly AssetPin RUBY = new(
        "ruby-3.4-wasm32-unknown-wasip1-full.tar.gz",
        "https://github.com/ruby/ruby.wasm/releases/download/2.10.1/ruby-3.4-wasm32-unknown-wasip1-full.tar.gz",
        "440f9a48a3bae258c70de610f7a78cfc56b536bdb9b81ef750f8d3918382515e");

    public static readonly AssetPin RUBY_LICENSE = new(
        "LICENSE-ruby.txt",
        "https://raw.githubusercontent.com/ruby/ruby/v3_4_1/COPYING",
        "967586d538a28955ec2541910cf63c5ac345fcdea94bfb1f1705a1f6eb36bcbb");

    public static readonly AssetPin RUBY_BSDL = new(
        "LICENSE-ruby-BSDL.txt",
        "https://raw.githubusercontent.com/ruby/ruby/v3_4_1/BSDL",
        "36a9a6e7347214bbba599a412617204e65bff065dcbe5c46f5cb454c80de9eb0");

    public static readonly AssetPin RUBY_WASM_LICENSE = new(
        "LICENSE-ruby.wasm.txt",
        "https://raw.githubusercontent.com/ruby/ruby.wasm/2.10.1/LICENSE",
        "90357d3794c968704914d42a52354a83f2d8b10cb43df3b63ef1ca0e5bbc0bf2");

    #endregion
}

/// <summary>One pinned download: where it lives in the cache, where it comes from, what it must hash to.</summary>
internal sealed record AssetPin(string FileName, string Url, string Sha256);
