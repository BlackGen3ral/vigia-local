param([string]$Version = '1.0.0', [string]$InnoCompiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versão inválida.' }
    dotnet publish .\desktop\Vigia.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o dist\app
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o Vigia Local.' }
    if (!$InnoCompiler) {
        $InnoCompiler = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (!$InnoCompiler) { throw 'Compilador Inno Setup não encontrado.' }
    & $InnoCompiler '/Q' "/DAppVersion=$Version" .\scripts\installer.iss
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar o instalador.' }
    $sum = (Get-FileHash -LiteralPath 'dist\VigiaLocal-Setup.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sum  VigiaLocal-Setup.exe" | Set-Content -LiteralPath 'dist\SHA256SUMS.txt' -Encoding ascii
} finally { Pop-Location }
