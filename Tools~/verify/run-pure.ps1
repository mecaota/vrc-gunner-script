# 純計算のテストを1ファイル動かす(build.sh から、ファイルごとに別の pwsh で呼ぶ)。
# テストのファイルは、引数なしの public static string Run() か、パッケージの根を受け取る Run(string) を持つ static クラス。
# 結果の最後の行が「全部 OK」なら合格。ほかのファイルが要るテストは、先頭の注釈に書く:
#   // verify-uses: <このパッケージの根からのパス>
#   // verify-uses-gunner: <-Gunner で渡したパッケージの根からのパス>
#   // verify-refs: <足す参照の名前,…>
# 使い方: pwsh -NoProfile -File run-pure.ps1 -File <テスト.cs> -Root <パッケージの根> [-Gunner <別のパッケージの根>]
param(
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$Root,
    [string]$Gunner = ''
)
$ErrorActionPreference = 'Stop'
$paths = @($File)
$refs = @('System.Collections', 'System.Runtime', 'System.Numerics.Vectors', 'System.Text.RegularExpressions',
    'System.Text.Encoding.Extensions', 'System.Linq', 'System.Console', 'System.Runtime.Extensions')
foreach ($line in Get-Content -LiteralPath $File -Encoding UTF8) {
    if ($line -match '^//\s*verify-uses:\s*(.+)$') { $paths += (Join-Path $Root $Matches[1].Trim()) }
    elseif ($line -match '^//\s*verify-uses-gunner:\s*(.+)$') {
        if ($Gunner -eq '') { Write-Output "SKIP $(Split-Path -Leaf $File): needs -Gunner"; exit 0 }
        $paths += (Join-Path $Gunner $Matches[1].Trim())
    }
    elseif ($line -match '^//\s*verify-refs:\s*(.+)$') { $refs += ($Matches[1] -split ',' | ForEach-Object { $_.Trim() }) }
}
foreach ($p in $paths) { if (-not (Test-Path -LiteralPath $p)) { Write-Output "NG  missing file: $p"; exit 1 } }
$types = Add-Type -Path $paths -ReferencedAssemblies $refs -PassThru
$ran = 0
$failed = 0
foreach ($t in $types) {
    if (-not ($t.IsAbstract -and $t.IsSealed)) { continue }
    $m = $t.GetMethod('Run', [System.Reflection.BindingFlags]'Public, Static')
    if ($null -eq $m -or $m.ReturnType -ne [string]) { continue }
    $ps = $m.GetParameters()
    if ($ps.Count -eq 0) { $out = $m.Invoke($null, $null) }
    elseif ($ps.Count -eq 1 -and $ps[0].ParameterType -eq [string]) { $out = $m.Invoke($null, @($Root)) }
    else { continue }
    $ran++
    $last = (($out -split "`n") | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 1).Trim()
    if ($last -eq '全部 OK') { Write-Output "OK  $($t.FullName)" }
    else {
        $failed++
        Write-Output "NG  $($t.FullName)"
        Write-Output $out
    }
}
if ($ran -eq 0) { Write-Output "NG  $(Split-Path -Leaf $File): no static string Run()"; exit 1 }
if ($failed -gt 0) { exit 1 }
exit 0
