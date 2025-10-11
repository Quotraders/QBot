#!/usr/bin/env pwsh
# Test NewsAPI connection and data retrieval

$apiKey = "b14bc928c6484a279cf8cc6374108088"
$keywords = "Federal Reserve OR FOMC OR Trump OR emergency OR 'rate cut' OR 'rate hike' OR tariff OR Powell"
$url = "https://newsapi.org/v2/everything?q=$([System.Web.HttpUtility]::UrlEncode($keywords))&language=en&sortBy=publishedAt&pageSize=5&apiKey=$apiKey"

Write-Host "🔍 Testing NewsAPI Connection..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host ""

try {
    Write-Host "📡 Sending request to NewsAPI.org..." -ForegroundColor Yellow
    Write-Host "   Keywords: Federal Reserve, FOMC, Trump, rate cuts, Powell" -ForegroundColor Gray
    Write-Host ""
    
    $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
    
    Write-Host "✅ SUCCESS! Connection established" -ForegroundColor Green
    Write-Host ""
    Write-Host "📊 API Response Summary:" -ForegroundColor Cyan
    Write-Host "   Status: $($response.status)" -ForegroundColor White
    Write-Host "   Total Results: $($response.totalResults)" -ForegroundColor White
    Write-Host "   Articles Returned: $($response.articles.Count)" -ForegroundColor White
    Write-Host ""
    
    if ($response.articles.Count -gt 0) {
        Write-Host "📰 Latest Breaking News (Top 5):" -ForegroundColor Cyan
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
        Write-Host ""
        
        $index = 1
        foreach ($article in $response.articles) {
            $publishedTime = [DateTime]::Parse($article.publishedAt)
            $minutesAgo = [Math]::Round((Get-Date).Subtract($publishedTime).TotalMinutes)
            
            Write-Host "[$index] " -NoNewline -ForegroundColor Yellow
            Write-Host "$($article.title)" -ForegroundColor White
            Write-Host "    📅 Published: $minutesAgo minutes ago" -ForegroundColor Gray
            Write-Host "    🌐 Source: $($article.source.name)" -ForegroundColor Gray
            
            # Check for breaking news keywords
            $isBreaking = $false
            $breakingKeywords = @("emergency", "breaking", "urgent", "alert", "shock", "crash")
            foreach ($keyword in $breakingKeywords) {
                if ($article.title -match $keyword -or $article.description -match $keyword) {
                    $isBreaking = $true
                    break
                }
            }
            
            if ($isBreaking) {
                Write-Host "    🔥 BREAKING NEWS DETECTED!" -ForegroundColor Red
            }
            
            if ($article.description) {
                $desc = $article.description.Substring(0, [Math]::Min(120, $article.description.Length))
                Write-Host "    📝 $desc..." -ForegroundColor DarkGray
            }
            Write-Host ""
            $index++
        }
        
        Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
        Write-Host ""
        Write-Host "✅ NewsAPI is working correctly!" -ForegroundColor Green
        Write-Host "✅ Bot will poll these headlines every 5 minutes during market hours" -ForegroundColor Green
        Write-Host "✅ Breaking news will be detected and logged with trades" -ForegroundColor Green
        
    } else {
        Write-Host "⚠️  No articles found for current keywords" -ForegroundColor Yellow
        Write-Host "   This is normal if no recent financial news" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "📈 Rate Limit Status:" -ForegroundColor Cyan
    Write-Host "   Free tier: 100 requests/day" -ForegroundColor White
    Write-Host "   Bot usage: ~60 requests/day (5-min polling during market hours)" -ForegroundColor White
    Write-Host "   Remaining: ~40 requests buffer" -ForegroundColor Green
    
} catch {
    Write-Host "❌ ERROR: Failed to connect to NewsAPI" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    
    if ($_.Exception.Message -match "401") {
        Write-Host "[!] API Key Issue - Verify your key at https://newsapi.org/account" -ForegroundColor Yellow
    } elseif ($_.Exception.Message -match "429") {
        Write-Host "[!] Rate Limit Exceeded - Wait 24 hours or upgrade plan" -ForegroundColor Yellow
    } else {
        Write-Host "[!] Network Issue - Check internet connection" -ForegroundColor Yellow
    }
    
    exit 1
}

Write-Host ""
Write-Host "🎯 Next Steps:" -ForegroundColor Cyan
Write-Host "   1. NewsAPI key is configured in .env" -ForegroundColor White
Write-Host "   2. Build the project: dotnet build -c Release" -ForegroundColor White
Write-Host "   3. Start bot: .\launch-unified-system.bat" -ForegroundColor White
Write-Host "   4. Watch logs for news context during trades" -ForegroundColor White
Write-Host ""
