$client = New-Object System.Net.Sockets.TcpClient
try {
    Write-Host "Connecting to localhost:4201..."
    $client.Connect("localhost", 4201)
    Write-Host "Connected! Socket created."
    
    $stream = $client.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    
    # Wait for initial message
    Write-Host "`nWaiting for welcome message..."
    Start-Sleep -Milliseconds 1000
    
    # Read any available data
    $buffer = New-Object byte[] 4096
    if ($stream.DataAvailable) {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
        Write-Host "Received ($bytesRead bytes): $([System.Text.RegularExpressions.Regex]::Escape($response))"
    } else {
        Write-Host "No data received yet"
    }
    
    # Try sending a command
    Write-Host "`nSending: connect #1"
    $writer.WriteLine("connect #1")
    $writer.Flush()
    
    # Wait longer for response
    Write-Host "Waiting for response..."
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Milliseconds 500
        
        if ($stream.DataAvailable) {
            Write-Host "Data available at iteration $i"
            $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
            $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
            Write-Host "Received ($bytesRead bytes): $response"
            break
        } else {
            Write-Host "." -NoNewline
        }
    }
    
    if (-not $stream.DataAvailable) {
        Write-Host "`nNo response after 5 seconds"
    }
    
    Write-Host "`nConnection still alive: $($client.Connected)"
    
} catch {
    Write-Host "Error: $_"
    Write-Host $_.Exception.StackTrace
} finally {
    if ($client) {
        $client.Close()
    }
    Write-Host "`nConnection closed"
}
