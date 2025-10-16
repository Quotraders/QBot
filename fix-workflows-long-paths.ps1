# Fix all GitHub Actions workflows to support long paths
Write-Host "`n🔧 Fixing all GitHub Actions workflows for long path support..." -ForegroundColor Cyan

$workflowDir = ".github/workflows"
$workflowFiles = Get-ChildItem -Path $workflowDir -Filter "*.yml" -File

$fixApplied = 0
$alreadyFixed = 0
$skipped = 0

foreach ($file in $workflowFiles) {
    $content = Get-Content $file.FullName -Raw
    
    # Check if already has long paths fix
    if ($content -match "git config --global core\.longpaths true") {
        Write-Host "  ✅ Already fixed: $($file.Name)" -ForegroundColor Green
        $alreadyFixed++
        continue
    }
    
    # Check if has checkout step
    if ($content -match "uses: actions/checkout@") {
        # Add long paths fix before first checkout
        $newContent = $content -replace `
            '(\s+)(steps:\s*\n\s+- name:.*Checkout)', `
            "`$1steps:`n`$1  - name: `"🔧 Enable Long Paths`"`n`$1    run: git config --global core.longpaths true`n`$1    `n`$1  - name:`$2"
        
        if ($newContent -ne $content) {
            Set-Content -Path $file.FullName -Value $newContent -NoNewline
            Write-Host "  🔧 Fixed: $($file.Name)" -ForegroundColor Yellow
            $fixApplied++
        } else {
            Write-Host "  ⚠️  Pattern not matched: $($file.Name)" -ForegroundColor DarkYellow
            $skipped++
        }
    } else {
        Write-Host "  ⏭️  No checkout: $($file.Name)" -ForegroundColor DarkGray
        $skipped++
    }
}

Write-Host "`n📊 Summary:" -ForegroundColor Cyan
Write-Host "  ✅ Already fixed: $alreadyFixed" -ForegroundColor Green
Write-Host "  🔧 Newly fixed: $fixApplied" -ForegroundColor Yellow
Write-Host "  ⏭️  Skipped: $skipped" -ForegroundColor DarkGray
Write-Host "`n✅ Done! Total workflows: $($workflowFiles.Count)" -ForegroundColor Green
