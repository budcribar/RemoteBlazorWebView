function Set-RailwayAgentKeys {
    <#
    .SYNOPSIS
        Sets AGENT_ENROLLMENT_KEYS (and optionally AGENT_ADMIN_KEY / PUBLIC_BASE_URL) on the Railway relay service.

    .DESCRIPTION
        Outside the Development environment the relay refuses every agent while AGENT_ENROLLMENT_KEYS is unset
        (doc/Agents.md). Run this before deploying that relay build, so the agents that are already installed
        keep connecting.

        Each agent sends the "key" field of %ProgramData%\RemotePCDiagnostics\agent.json on its PC. List every key
        that should stay enrolled with -Key, add this PC's key with -IncludeLocalAgentKey, and keep the keys that
        are already set on Railway with -Append.

        Dot-source it (as a PowerShell profile Functions folder does) to define the Set-RailwayAgentKeys function,
        or run the file directly with the same parameters.

        Uses the Railway CLI (https://docs.railway.com/guides/cli). Sign in first with "railway login". The script
        links its own folder (%LOCALAPPDATA%\Set-RailwayAgentKeys) to -Project / -Service / -Environment, so it works
        from any folder and leaves your other "railway link" folders alone.
        Setting a variable makes Railway redeploy the service, unless -SkipDeploy is given.

    .EXAMPLE
        Set-RailwayAgentKeys -Key test-shop -IncludeLocalAgentKey -WhatIf
        Shows what would be set, without changing Railway.

    .EXAMPLE
        Set-RailwayAgentKeys -Key rpd_live_abc123,test-shop -Append
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        # Enrollment keys to accept. Comma-separated values are split.
        [string[]] $Key = @(),

        # Also accept the key this PC's agent was installed with (needs read access to agent.json, usually an elevated prompt).
        [switch] $IncludeLocalAgentKey,

        # Keep the keys already set on Railway and add these to them. Without it, the list is replaced.
        [switch] $Append,

        # Railway project name or id of the relay.
        [string] $Project = 'Remote Blazor WebView',

        # Railway service name or id of the relay.
        [string] $Service = 'remote-webview-server',

        # Railway environment name or id.
        [string] $Environment = 'production',

        # Optional: also set AGENT_ADMIN_KEY (needed for unattended starts).
        [string] $AdminKey,

        # Optional: also set PUBLIC_BASE_URL, e.g. https://server.remoteblazorwebview.com:34544
        [string] $PublicBaseUrl,

        # Set the variables without redeploying now.
        [switch] $SkipDeploy
    )

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    $KeysVariable = 'AGENT_ENROLLMENT_KEYS'
    $PublicKeys = @('test-shop')   # Values that appear in public repositories: they only keep out people who haven't read them.

    function Get-TargetArgs {
        $targetArgs = @()
        if ($Service) { $targetArgs += @('--service', $Service) }
        if ($Environment) { $targetArgs += @('--environment', $Environment) }
        return $targetArgs
    }

    function Invoke-Railway([string[]] $Arguments) {
        # Only standard output is captured (so JSON stays parseable); the CLI's own messages still reach the console.
        $output = & railway @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "railway $($Arguments[0]) failed (exit $LASTEXITCODE). See the Railway CLI's message above."
        }
        return ($output -join [Environment]::NewLine)
    }

    # Keys are compared exactly by the relay. A comma would split one key into two, and spaces are trimmed off.
    function Test-KeyText([string] $Value) {
        if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
        foreach ($ch in $Value.ToCharArray()) {
            if ($ch -eq ',' -or [char]::IsWhiteSpace($ch) -or [char]::IsControl($ch)) { return $false }
        }
        return $true
    }

    function Split-KeyList([string[]] $Values) {
        $result = @()
        foreach ($value in $Values) {
            if ($null -eq $value) { continue }
            foreach ($part in $value.Split(',')) {
                $trimmed = $part.Trim()
                if ($trimmed.Length -gt 0) { $result += $trimmed }
            }
        }
        return $result
    }

    function Format-Masked([string] $Value) {
        if ($PublicKeys -contains $Value) { return $Value }
        if ($Value.Length -le 8) { return ('*' * $Value.Length) }
        return $Value.Substring(0, 8) + ('*' * ($Value.Length - 8))
    }

    # ---------------------------------------------------------------- Railway CLI and sign-in

    if (-not (Get-Command railway -ErrorAction SilentlyContinue)) {
        throw "The Railway CLI was not found. Install it (for example 'npm i -g @railway/cli' or 'scoop install railway'), then run 'railway login'."
    }
    $who = Invoke-Railway @('whoami')
    Write-Host "Railway: $who"

    $target = Get-TargetArgs
    # Railway commands act on the project linked to the current folder, so link a folder of our own.
    $linkDir = Join-Path $env:LOCALAPPDATA 'Set-RailwayAgentKeys'
    New-Item -ItemType Directory -Force -Path $linkDir | Out-Null
    Push-Location -LiteralPath $linkDir
    try {
        Invoke-Railway (@('link', '--project', $Project) + $target) | Out-Null
        Write-Host 'Linked project:'
        Write-Host (Invoke-Railway @('status'))

        # ---------------------------------------------------------------- collect the keys

        $keys = [System.Collections.Generic.List[string]]::new()
        function Add-Key([string] $Value, [string] $Source) {
            if (-not (Test-KeyText $Value)) {
                throw "The key from $Source is empty or has a comma, a space or a control character."
            }
            if (-not $keys.Contains($Value)) {
                $keys.Add($Value)
                Write-Host "  + $(Format-Masked $Value)  ($Source)"
            }
        }

        Write-Host 'Keys to accept:'

        if ($Append) {
            $current = Invoke-Railway (@('variables', '--json') + $target) | ConvertFrom-Json
            $existing = $current.PSObject.Properties[$KeysVariable]
            if ($existing) {
                foreach ($k in (Split-KeyList @([string]$existing.Value))) { Add-Key $k 'already on Railway' }
            }
            else {
                Write-Host "  ($KeysVariable is not set on Railway yet)"
            }
        }

        foreach ($k in (Split-KeyList $Key)) { Add-Key $k '-Key' }

        if ($IncludeLocalAgentKey) {
            $configPath = Join-Path $env:ProgramData 'RemotePCDiagnostics\agent.json'
            if (-not (Test-Path -LiteralPath $configPath)) {
                throw "No agent is installed on this PC ($configPath not found)."
            }
            try {
                $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
            }
            catch {
                throw "Can't read $configPath ($($_.Exception.Message)). Run PowerShell as Administrator."
            }
            $localKey = [string]$config.key
            if ([string]::IsNullOrWhiteSpace($localKey)) {
                Write-Warning "This PC's agent has no enrollment key in $configPath. Reinstall it with --key=<key> or it will be refused."
            }
            else {
                Add-Key $localKey.Trim() "this PC ($env:COMPUTERNAME)"
                if ($config.PSObject.Properties['server']) { Write-Host "    this PC's agent connects to $($config.server)" }
            }
        }

        if ($keys.Count -eq 0) {
            throw "No keys given. Pass -Key, -IncludeLocalAgentKey or -Append. With $KeysVariable unset the relay refuses every agent."
        }

        foreach ($k in $keys) {
            if ($PublicKeys -contains $k) {
                Write-Warning "'$k' is published in a public repository, so anyone who reads it can enroll a computer. Plan to move those agents to a private key."
            }
        }

        # ---------------------------------------------------------------- set the variables

        $settings = [ordered]@{ $KeysVariable = ($keys -join ',') }
        if ($PSBoundParameters.ContainsKey('AdminKey')) {
            if (-not (Test-KeyText $AdminKey)) { throw 'AdminKey is empty or has a comma, a space or a control character.' }
            $settings['AGENT_ADMIN_KEY'] = $AdminKey
        }
        if ($PSBoundParameters.ContainsKey('PublicBaseUrl')) {
            $uri = $null
            if (-not [Uri]::TryCreate($PublicBaseUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') {
                throw "PublicBaseUrl must be an absolute https address, got '$PublicBaseUrl'."
            }
            $settings['PUBLIC_BASE_URL'] = $PublicBaseUrl.TrimEnd('/')
        }

        $setArgs = @('variables')
        foreach ($name in $settings.Keys) { $setArgs += @('--set', "$name=$($settings[$name])") }
        $setArgs += $target
        if ($SkipDeploy) { $setArgs += '--skip-deploys' }

        $where = "service '$Service' in project '$Project' ($Environment)"
        $what = "Set $($settings.Keys -join ', ') ($($keys.Count) enrollment key(s))" + $(if ($SkipDeploy) { ' without redeploying' } else { ' and redeploy' })
        if ($PSCmdlet.ShouldProcess($where, $what)) {
            Invoke-Railway $setArgs | Out-Null
            Write-Host "Done: $what on $where."
            if ($SkipDeploy) { Write-Host 'The new values apply at the next deploy.' }
            Write-Host 'An agent whose key is not listed is refused and keeps retrying. Run again with -Append -Key <key> to let it in.'
        }
    }
    finally {
        Pop-Location
    }
}

# Run directly (not dot-sourced): call the function with the script's arguments.
if ($MyInvocation.InvocationName -ne '.') {
    Set-RailwayAgentKeys @args
}
