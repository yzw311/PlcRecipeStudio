$ErrorActionPreference="Stop"
$patterns=@("123456","password=","ApiKey=", "apikey:")
# scripts 目录含本脚本的匹配模式字面量，扫描必然自命中，须排除
$files=Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|publish|.git|scripts|PlcRecipe.Tests|PlcRecipe.StressTest)\\' }
foreach($f in $files){ $text=Get-Content $f.FullName -Raw; foreach($p in $patterns){ if($text -match [regex]::Escape($p)){ throw "Potential secret/default credential in $($f.FullName): $p" } } }
Write-Host "Release secret scan passed."
