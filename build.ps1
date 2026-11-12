param(
    [switch]$RunTests,
    [switch]$SmokeTest,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x C# compiler not found.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$targetExe = Join-Path $OutputDirectory 'yyc-golf.exe'
$sourceFiles = @('Engine.cs', 'DesktopMiniGolf.cs', 'Tests.cs', 'AssemblyInfo.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$iconPath = Join-Path $PSScriptRoot 'app.ico'
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/win32icon:$iconPath" "/win32manifest:$manifestPath" "/out:$targetExe" $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if ($RunTests) {
    $reportPath = Join-Path $OutputDirectory 'self-test.txt'
    $testProcess = Start-Process -FilePath $targetExe -ArgumentList @('--self-test', ('"' + $reportPath + '"')) -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath $reportPath -Encoding UTF8
    if ($testProcess.ExitCode -ne 0) { throw 'Physics tests failed' }
}
if ($SmokeTest) {
    $smokeReport = Join-Path $OutputDirectory 'smoke-test.txt'
    $smokeProcess = Start-Process -FilePath $targetExe -ArgumentList @('--smoke-test', ('"' + $smokeReport + '"')) -WindowStyle Hidden -PassThru
    if (-not $smokeProcess.WaitForExit(30000)) { $smokeProcess.Kill(); throw 'GUI smoke test timed out' }
    if ($smokeProcess.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $smokeReport) { Get-Content -LiteralPath $smokeReport }
        throw 'GUI smoke test failed'
    }
    Write-Output 'PASS Windows native window initialization, offscreen rendering and message loop'
}
Get-Item -LiteralPath $targetExe | Select-Object FullName, Length
