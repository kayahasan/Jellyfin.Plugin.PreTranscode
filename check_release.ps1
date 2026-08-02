for ($i = 0; $i -lt 60; $i++) {
    $r = Invoke-RestMethod -Uri 'https://api.github.com/repos/kayahasan/Jellyfin.Plugin.PreTranscode/releases/tags/v1.0.0.16'
    if ($r.assets.Count -gt 0) {
        $r.body
        exit
    }
    Start-Sleep -Seconds 3
}
Write-Host "TIMEOUT"
