$client = New-Object System.Net.Sockets.TcpClient
try {
    Write-Host "Connecting to localhost:4201..."
    $client.Connect("localhost", 4201)
    Write-Host "Connected!`n"
    
    $stream = $client.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    
    function Read-Response {
        Start-Sleep -Milliseconds 500
        $output = ""
        while ($stream.DataAvailable) {
            $output += $reader.ReadLine() + "`n"
        }
        return $output
    }
    
    function Send-Command($cmd) {
        Write-Host "> $cmd" -ForegroundColor Cyan
        $writer.WriteLine($cmd)
        Start-Sleep -Milliseconds 1000
        $response = Read-Response
        Write-Host $response
    }
    
    # Wait for welcome
    Start-Sleep -Milliseconds 1000
    $welcome = Read-Response
    Write-Host $welcome -ForegroundColor Green
    
    # Run your test commands
    Send-Command "connect #1"
    Send-Command "look"
    Send-Command "+where"
    Send-Command "l God"
    Send-Command "+events"
    Send-Command "WHO"
    Send-Command "asdasd"
    
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
} finally {
    if ($client) {
        $client.Close()
    }
    Write-Host "`nDisconnected" -ForegroundColor Yellow
}
