# Fix all GitHub Actions workflows - proper symlinks + long paths fix
Write-Host "`n🔧 Fixing ALL workflows for symlinks + long paths..." -ForegroundColor Cyan

$workflowDir = ".github/workflows"
$workflowFiles = Get-ChildItem -Path $workflowDir -Filter "*.yml" -File

$fixed = 0
$alreadyFixed = 0
$errors = 0

$properFix = @"
      - name: "🔧 Configure Git for Windows Compatibility"
        run: |
          git config --global core.longpaths true
          git config --global core.symlinks false
          
"@

foreach ($file in $workflowFiles) {
    try {
        $content = Get-Content $file.FullName -Raw
        
        # Check if already properly fixed
        if ($content -match "core\.symlinks false") {
            Write-Host "  ✅ Already fixed: $($file.Name)" -ForegroundColor Green
            $alreadyFixed++
            continue
        }
        
        # Remove old broken fixes
        $content = $content -replace '(?m)^\s+- name: "🔧 Enable Long Paths".*?run:.*?git config.*?core\.longpaths.*?\n\s*\n', ''
        
        # Add proper fix before checkout
        if ($content -match '(\s+)(steps:\s*\n)(\s+- name:.*?Checkout)') {
            $indent = $matches[1]
            $newContent = $content -replace `
                '(\s+)(steps:\s*\n)(\s+- name:.*?Checkout)', `
                "`$1`$2$properFix`$3"
            
            if ($newContent -ne $content) {
                Set-Content -Path $file.FullName -Value $newContent -NoNewline
                Write-Host "  🔧 Fixed: $($file.Name)" -ForegroundColor Yellow
                $fixed++
            } else {
                Write-Host "  ⏭️  No checkout or pattern mismatch: $($file.Name)" -ForegroundColor DarkGray
            }
        } else {
            Write-Host "  ⏭️  No standard checkout step: $($file.Name)" -ForegroundColor DarkGray
        }
    }
    catch {
        Write-Host "  ❌ Error: $($file.Name) - $_" -ForegroundColor Red
        $errors++
    }
}

Write-Host "`n📊 Summary:" -ForegroundColor Cyan
Write-Host "  ✅ Already fixed: $alreadyFixed" -ForegroundColor Green
Write-Host "  🔧 Newly fixed: $fixed" -ForegroundColor Yellow
Write-Host "  ❌ Errors: $errors" -ForegroundColor Red
Write-Host "`n✅ Done! Total workflows: $($workflowFiles.Count)" -ForegroundColor Green
