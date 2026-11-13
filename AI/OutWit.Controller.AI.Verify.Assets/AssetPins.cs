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

    public const string NUMPY_VERSION = "2.5.4";
    public const string NUMPY_RUNTIME_DIR = "python-3.14.6-numpy-2.5.4";
    public const string NUMPY_MODULE = "python.wasm";
    public const string NUMPY_PLAIN_MODULE = "python-plain.wasm";
    public const string NUMPY_ZIP = "numpy-2.5.4.zip";

    public const string QUICKJS_VERSION = "0.15.1";
    public const string QUICKJS_SOURCE_DIR = "quickjs-0.15.1";
    public const string QUICKJS_MODULE = "qjs.wasm";
    public const string QUICKJS_FILE = "qjs-wasi.wasm";
    public const string QUICKJS_LICENSE_FILE = "LICENSE-quickjs-ng.txt";

    /// <summary>
    /// qjs.wasm as the toolchain builds it (modules/quickjs-0.15.1: QuickJS-NG's sources, its
    /// stack below the data, the stack limit on), in the set under its old name qjs-wasi.wasm.
    /// The release's own WASI build placed its stack after the data with the limit off.
    /// </summary>
    public const string QUICKJS_MODULE_SHA256 = "3629434c55636670bad9df38c7979e24213559261497c846c0aa11e1c9d39bef";

    public const string RUBY_VERSION = "3.4.1";
    public const string RUBY_RUNTIME_DIR = "ruby-3.4.1";
    public const string RUBY_API_VERSION = "3.4.0";
    public const string RUBY_WASM_RELEASE = "2.10.1";

    /// <summary>The bundled gems a program is given without RubyGems: pure Ruby, their library folders only.</summary>
    public static readonly string[] RUBY_BUNDLED_GEMS = ["base64-0.2.0", "csv-3.3.2", "matrix-0.4.2", "prime-0.1.3"];

    public const string TYPESCRIPT_RUNTIME_DIR = "typescript-sucrase-3.35.1";
    public const string TYPESCRIPT_BUNDLE = "sucrase.js";
    public const string TYPESCRIPT_LICENSE = "LICENSE-sucrase.txt";

    /// <summary>sucrase.js as the toolchain bundles it (modules/typescript-sucrase-3.35.1), pinned like a download.</summary>
    public const string TYPESCRIPT_BUNDLE_SHA256 = "3390a6c1f791844cb001ec1686ae712e7b29e5d8283b7a18f688a1bc03e4b506";

    public const string CLANG_VERSION = "22.1.0";
    public const string CLANG_RUNTIME_DIR = "clang-22.1.0";
    public const string CLANG_PACKAGE_VERSION = "22.0.0-git20542-10";

    public const string LUA_VERSION = "5.4.8";
    public const string LUA_RUNTIME_DIR = "lua-5.4.8";
    public const string LUA_MODULE = "lua.wasm";
    public const string LUA_LICENSE = "LICENSE-lua.txt";

    /// <summary>
    /// lua.wasm as the toolchain builds it (modules/lua-5.4.8, next to this tool): not a
    /// download, but pinned the same way, so a changed module cannot slip into the set.
    /// </summary>
    public const string LUA_MODULE_SHA256 = "5b953864a7d270d34c2c7360f90bd5e5d9c2196776d6af185dfb12fd29558baf";

    #endregion

    #region Pins

    public static readonly AssetPin CPYTHON = new(
        "python-3.14.6-wasi_sdk-24.zip",
        "https://github.com/brettcannon/cpython-wasi-build/releases/download/v3.14.6/python-3.14.6-wasi_sdk-24.zip",
        "73bf2e9774c4d8820d0877ec5db0b963df3a9611fc2a63838aeaee29dfd034e6");

    /// <summary>
    /// YoWASP's clang for WASI from npm: LLVM 22.1.0 (codeberg.org/YoWASP/llvm-project 9560ae0f) as one
    /// WASI preview 1 module, and the sysroot (wasi-libc, libc++, libc++abi, compiler-rt) it expects at /usr.
    /// </summary>
    public static readonly AssetPin YOWASP_CLANG = new(
        "clang-22.0.0-git20542-10.tgz",
        "https://registry.npmjs.org/@yowasp/clang/-/clang-22.0.0-git20542-10.tgz",
        "6230ea1afa9691fa065935cf68c01642ff9b31c183fe8ac64cdfda025df06009");

    /// <summary>The licence texts of the C/C++ runtime (the package carries none), each at a pinned commit.</summary>
    public static readonly AssetPin[] CLANG_LICENCES =
    [
        new("LICENSE-llvm.txt", "https://raw.githubusercontent.com/llvm/llvm-project/llvmorg-22.1.0/LICENSE.TXT",
            "8d85c1057d742e597985c7d4e6320b015a9139385cff4cbae06ffc0ebe89afee"),
        new("LICENSE-yowasp-clang.txt", "https://raw.githubusercontent.com/YoWASP/clang/944dd7c774954180e621cc8e12984023a7f8bcbe/LICENSE.txt",
            "65c6633c2c9effa87f95bfe794d5da07bd3faab2814e4a0a15883e28ec4e543f"),
        new("LICENSE-wasi-libc.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/LICENSE",
            "2711a8b5a5cdfef0e639f96c1aca12ae23d7d64a02d0507f1bdf14d2b27bbc3a"),
        new("LICENSE-wasi-libc-MIT.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/LICENSE-MIT",
            "23f18e03dc49df91622fe2a76176497404e46ced8a715d9d2b67a7446571cca3"),
        new("LICENSE-wasi-libc-APACHE-LLVM.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/LICENSE-APACHE-LLVM",
            "268872b9816f90fd8e85db5a28d33f8150ebb8dd016653fb39ef1f94f2686bc5"),
        new("LICENSE-cloudlibc.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/libc-bottom-half/cloudlibc/LICENSE",
            "c8b789cf5a746611e6300a0cc7750dbf92b61912a709d04e639245f7290656d0"),
        new("COPYRIGHT-musl.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/libc-top-half/musl/COPYRIGHT",
            "f9bc4423732350eb0b3f7ed7e91d530298476f8fec0c6c427a1c04ade22655af"),
        new("COPYING-musl-fts.txt", "https://raw.githubusercontent.com/WebAssembly/wasi-libc/ac020b86fd44bafe60aa4fa12f407d16e3731329/fts/musl-fts/COPYING",
            "55af87e4017668f54467a3380e7ebbac5e672d8c763bfe95e6fc882a6fdc4046")
    ];

    /// <summary>
    /// The numpy runtime's folder as toolchain/python-numpy builds it (the AI Verify python-numpy
    /// workflow, two builds compared byte for byte): the start-up snapshot, the plain module, the
    /// two layers the snapshot was made with, the licences. The controller's data asset of the
    /// same name; until its release exists, --local or VERIFY_ASSETS_LOCAL names a folder that
    /// holds the workflow's copy.
    /// </summary>
    public static readonly AssetPin PYTHON_NUMPY = new(
        "verify-python-numpy.zip",
        "https://github.com/OmnibusCloud/Controllers/releases/download/verify-v0.11.0/verify-python-numpy.zip",
        "6b70674b209288f3fad36d7448e84ab9ff1903b61352c9903ea7e82504f15b0e");

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
