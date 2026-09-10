$ErrorActionPreference="Stop"
$patterns=@("123456","password=","ApiKey=", "apikey:")
$files=Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|publish|.git|PlcRecipe.Tests|PlcRecipe.StressTest)\\' }
foreach($f in $files){ $text=Get-Content $f.FullName -Raw; foreach($p in $patterns){ if($text -match [regex]::Escape($p)){ throw "Potential secret/default credential in $($f.FullName): $p" } } }
Write-Host "Release secret scan passed."
