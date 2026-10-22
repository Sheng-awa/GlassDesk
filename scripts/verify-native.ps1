param([Parameter(Mandatory=$true)][string]$NativeDirectory)
$ErrorActionPreference='Stop'
$taskNative=[IO.Path]::GetFullPath($NativeDirectory)
foreach($taskName in @('GlassDesk.Native.dll','GlassDesk.CaptureWorker.exe')) {
    if(!(Test-Path -LiteralPath (Join-Path $taskNative $taskName) -PathType Leaf)) {
        throw "Missing native input: $taskName. Run scripts/build-native.ps1 for this native output directory."
    }
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class GlassDeskBuildExportCheck {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
 [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
 public static extern IntPtr GetProcAddress(IntPtr module, string name);
 [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
}
'@
$taskModule=[GlassDeskBuildExportCheck]::LoadLibraryExW((Join-Path $taskNative 'GlassDesk.Native.dll'),[IntPtr]::Zero,1)
if($taskModule -eq [IntPtr]::Zero){throw 'Cannot inspect GlassDesk.Native.dll.'}
try {
    foreach($taskSymbol in @('GDGetNativeAbiInfo','GDConfigureTypographyV2','GDMeasureTextV2','GDRenderSurfaceV2')) {
        if([GlassDeskBuildExportCheck]::GetProcAddress($taskModule,$taskSymbol) -eq [IntPtr]::Zero) {
            throw "Native input is incompatible ($taskSymbol missing). Rebuild with scripts/build-native.ps1; do not reuse the old native DLL."
        }
    }
} finally {[void][GlassDeskBuildExportCheck]::FreeLibrary($taskModule)}
