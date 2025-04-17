$files = Get-ChildItem -Path . -Recurse -Include "*.cs"

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw
    
    if ($content -match "using System\.Data\.SqlClient;") {
        Write-Host "Updating $($file.FullName)"
        $content = $content -replace "using System\.Data\.SqlClient;", "using Microsoft.Data.SqlClient;"
        Set-Content -Path $file.FullName -Value $content
    }
}

Write-Host "All files updated successfully!" 