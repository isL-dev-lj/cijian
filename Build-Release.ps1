param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [Parameter(Mandatory = $true)]
    [string]$WordList
)

$ErrorActionPreference = 'Stop'
$wordListPath = (Resolve-Path -LiteralPath $WordList).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$speech = Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\System.Speech" -Filter System.Speech.dll -Recurse |
    Select-Object -First 1 -ExpandProperty FullName
$stage = New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('CijianRelease-' + [guid]::NewGuid().ToString('N')))

Push-Location $PSScriptRoot
try {
    $channel = "https://github.com/$Repository/releases/latest"
    $updateSource = Get-Content -LiteralPath CijianUpdates.cs -Raw -Encoding UTF8
    $updatedSource = [regex]::Replace($updateSource, 'private const string Channel = "[^"]+";', ('private const string Channel = "' + $channel + '";'))
    $updatePath = Join-Path $stage.FullName 'CijianUpdates.cs'
    [IO.File]::WriteAllText($updatePath, $updatedSource)
    $exe = Join-Path $stage.FullName 'Cijian.exe'
    & "$framework\csc.exe" /nologo /target:winexe /codepage:65001 "/out:$exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/reference:$speech" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\WindowsBase.dll" /resource:TypingClick.wav,TypingClick.wav /resource:Correct.wav,Correct.wav /resource:Incorrect.wav,Incorrect.wav /win32icon:Cijian.ico /resource:Cijian.ico,Cijian.ico "/resource:$wordListPath,GraduateFrequency.tsv" MoyuPractice.cs $updatePath
    if ($LASTEXITCODE -ne 0) { throw 'Cijian compilation failed.' }
    $test = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
    if ($test.ExitCode -ne 0) { throw "Cijian self-test failed with exit code $($test.ExitCode)." }
    New-Item -ItemType Directory -Path dist -Force | Out-Null
    Compress-Archive -LiteralPath $exe -DestinationPath dist\Cijian-Update.zip -Force
    Get-Item dist\Cijian-Update.zip
} finally {
    Pop-Location
}
