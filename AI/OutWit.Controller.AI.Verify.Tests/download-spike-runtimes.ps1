# Fetches the pinned WASM language runtimes for the sandbox spike tests
# into @Data/runtimes/ (gitignored). The tests Assert.Ignore when these are
# absent, so running this script is what "opts in" a machine.
#
# Works in Windows PowerShell 5.1 and pwsh (Linux/macOS).

$ErrorActionPreference = 'Stop'

$runtimes = Join-Path $PSScriptRoot '@Data/runtimes'
New-Item -ItemType Directory -Force $runtimes | Out-Null

$pins = @(
    @{
        Name   = 'qjs-wasi.wasm'
        Url    = 'https://github.com/quickjs-ng/quickjs/releases/download/v0.15.1/qjs-wasi.wasm'
        Sha256 = 'B4071EF2FBB2BB693C0BBCFC07CB9D28639FD9CEA2FD986824A57AEAC929817B'
    },
    @{
        Name   = 'python-3.14.6-wasi_sdk-24.zip'
        Url    = 'https://github.com/brettcannon/cpython-wasi-build/releases/download/v3.14.6/python-3.14.6-wasi_sdk-24.zip'
        Sha256 = '73BF2E9774C4D8820D0877EC5DB0B963DF3A9611FC2A63838AEAEE29DFD034E6'
    }
)

foreach ($pin in $pins) {
    $path = Join-Path $runtimes $pin.Name
    if (-not (Test-Path $path)) {
        Write-Host "downloading $($pin.Name)..."
        Invoke-WebRequest -Uri $pin.Url -OutFile $path
    }
    $actual = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($actual -ne $pin.Sha256) {
        Remove-Item $path
        throw "$($pin.Name): SHA256 mismatch (got $actual, pinned $($pin.Sha256)) - file removed, re-run to retry"
    }
    Write-Host "$($pin.Name): ok ($actual)"
}

$pyDir = Join-Path $runtimes 'python-3.14.6'
if (-not (Test-Path (Join-Path $pyDir 'python.wasm'))) {
    Write-Host 'extracting python-3.14.6...'
    Expand-Archive -Path (Join-Path $runtimes 'python-3.14.6-wasi_sdk-24.zip') -DestinationPath $pyDir -Force
}

Write-Host 'runtimes ready.'
