# Find placeholder violations
$pattern = '//\s*(PLACEHOLDER|MOCK|STUB|FAKE|DUMMY|TEMP)[\s:]'
$excluded = '(Safety\\Analyzers|IntelligenceStack|Tests|Backtest|docs\\|tools\\)'

Get-ChildItem -Path "src" -Filter "*.cs" -Recurse -File | ForEach-Object {
    $file = $_.FullName
    if ($file -notmatch $excluded) {
        Select-String -Path $file -Pattern $pattern -CaseSensitive | ForEach-Object {
            Write-Host "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"
        }
    }
}
