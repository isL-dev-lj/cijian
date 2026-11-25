param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [Parameter(Mandatory = $true)]
    [string]$WordList,
    [string]$PublisherKeyFile
)

$ErrorActionPreference = 'Stop'
$wordListPath = (Resolve-Path -LiteralPath $WordList).Path
$keyPath = if ($PublisherKeyFile) { (Resolve-Path -LiteralPath $PublisherKeyFile).Path }
$words = Get-Content -LiteralPath $wordListPath -Encoding UTF8 | ForEach-Object {
    $columns = $_ -split "`t", 2
    if ($columns.Count -ne 2 -or !$columns[0].Trim() -or !$columns[1].Trim()) {
        throw 'Invalid word list row. Expected a word, a tab and a definition.'
    }
    $columns[0].Trim()
}
if (@($words).Count -lt 51 -or @($words | Sort-Object -Unique).Count -ne @($words).Count) {
    throw 'The word list must contain at least 51 distinct entries.'
}
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
    if ($keyPath) {
        Add-Type -AssemblyName System.Security
        $rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
        $rsa.PersistKeyInCsp = $false
        try {
            $key = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath), $null, 'CurrentUser')
            $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($key))
            if (!$updatedSource.Contains($rsa.ToXmlString($false))) { throw 'Publisher key mismatch.' }
            $signature = $rsa.SignData([IO.File]::ReadAllBytes($exe), 'SHA256')
            $zip = (Resolve-Path dist\Cijian-Update.zip).Path
            $stream = [IO.File]::Open($zip, 'Open', 'ReadWrite')
            try {
                $stream.Position = $stream.Length - 22
                $endRecord = [byte[]]::new(22)
                [void]$stream.Read($endRecord, 0, 22)
                if ([BitConverter]::ToUInt32($endRecord, 0) -ne 0x06054b50 -or
                    [BitConverter]::ToUInt16($endRecord, 20) -ne 0) { throw 'Unexpected ZIP footer.' }
                $stream.Position = $stream.Length - 2
                $length = [BitConverter]::GetBytes([uint16]$signature.Length)
                $stream.Write($length, 0, $length.Length)
                $stream.Write($signature, 0, $signature.Length)
            } finally { $stream.Dispose() }
        } finally { $rsa.Dispose() }
    }
    Get-Item dist\Cijian-Update.zip
} finally {
    Pop-Location
}
