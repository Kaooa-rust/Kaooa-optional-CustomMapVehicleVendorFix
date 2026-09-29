param([switch]$Performance)

$ErrorActionPreference = 'Stop'
$pluginPath = Join-Path $PSScriptRoot '..\CustomMapVehicleVendorFix.cs'
Add-Type -Path (Join-Path $PSScriptRoot 'RustStubs.cs'), $pluginPath, (Join-Path $PSScriptRoot 'VendorFixTests.cs')
if ($Performance) {
    [VendorFixTests]::RunPerformance()
} else {
    [VendorFixTests]::RunAll()
}
