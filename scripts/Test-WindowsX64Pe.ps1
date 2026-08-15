param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$file = Get-Item -LiteralPath $resolvedPath
if ($file.Length -lt 256) {
    throw "Executable is too small to be a valid PE image: $resolvedPath ($($file.Length) bytes)"
}

$bytes = [System.IO.File]::ReadAllBytes($resolvedPath)
if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    throw "Executable does not begin with DOS MZ signature: $resolvedPath"
}

$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ($peOffset -lt 0x40 -or ($peOffset + 26) -ge $bytes.Length) {
    throw "Executable contains an invalid PE header offset: $resolvedPath"
}

if ($bytes[$peOffset] -ne 0x50 -or
    $bytes[$peOffset + 1] -ne 0x45 -or
    $bytes[$peOffset + 2] -ne 0x00 -or
    $bytes[$peOffset + 3] -ne 0x00) {
    throw "Executable does not contain a PE signature: $resolvedPath"
}

$machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x8664) {
    throw "Executable machine is not AMD64/x64: $resolvedPath"
}

$optionalHeaderMagic = [BitConverter]::ToUInt16($bytes, $peOffset + 24)
if ($optionalHeaderMagic -ne 0x020B) {
    throw "Executable is not PE32+ / 64-bit: $resolvedPath"
}

Write-Host "Verified PE32+ AMD64 executable: $resolvedPath ($($file.Length) bytes)"
