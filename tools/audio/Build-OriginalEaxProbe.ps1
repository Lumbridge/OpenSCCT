#Requires -Version 7
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../local/reverb-parity/vendor-probe'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'An installed Visual C++ x86 toolchain is required.' }
$toolset = Get-ChildItem (Join-Path $vs 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk = Get-ChildItem (Join-Path $sdkRoot 'Include') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'um/dsound.h') } | Sort-Object Name -Descending | Select-Object -First 1
$compiler = Join-Path $toolset.FullName 'bin/Hostx64/x86/cl.exe'
$includes = @((Join-Path $toolset.FullName 'include'),(Join-Path $sdk.FullName 'um'),(Join-Path $sdk.FullName 'shared'),(Join-Path $sdk.FullName 'ucrt'))
$libs = @((Join-Path $toolset.FullName 'lib/x86'),(Join-Path $sdkRoot "Lib/$($sdk.Name)/um/x86"),(Join-Path $sdkRoot "Lib/$($sdk.Name)/ucrt/x86"))
$arguments = @('/nologo','/std:c++17','/EHsc','/W4','/DUNICODE','/D_UNICODE','/D_WIN32_WINNT=0x0A00','/DNTDDI_VERSION=0x0A00000A','/MT')
$arguments += $includes | ForEach-Object { '/I' + $_ }
$arguments += @((Join-Path $PSScriptRoot 'Probe-OriginalEax.cpp'),('/Fo' + (Join-Path $OutputDirectory 'Probe-OriginalEax.obj')),('/Fe' + (Join-Path $OutputDirectory 'Probe-OriginalEax.exe')),'/link')
$arguments += $libs | ForEach-Object { '/LIBPATH:' + $_ }
$arguments += @('user32.lib','ole32.lib','dxguid.lib','uuid.lib','mmdevapi.lib')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Original EAX probe compilation failed.' }
Write-Output (Join-Path $OutputDirectory 'Probe-OriginalEax.exe')
