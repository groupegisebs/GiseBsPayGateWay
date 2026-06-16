#Requires -Version 5.1
<#
.SYNOPSIS
    Déploiement en une commande depuis Windows vers Ubuntu (systemd).

.DESCRIPTION
    Lit deploy/project.config.json (projet) et deploy/deploy-all.config.json (serveur).
    Publie, transfère via SCP, configure systemd et redémarre le service.

.EXAMPLE
    .\deploy\deploy-all.ps1 -ServerHost "192.168.1.10"

.EXAMPLE
    .\deploy\deploy-all.ps1
    # Utilise deploy/deploy-all.config.json si présent
#>
[CmdletBinding()]
param(
    [string]$ServerHost,
    [string]$SshUser = 'ubuntu',
    [int]$SshPort = 22,
    [string]$SshIdentityFile,
    [string]$ConnectionString,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$PublishDir = Join-Path $RepoRoot 'publish'

function Read-JsonConfig {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Label
    )

    if (-not (Test-Path $Path)) {
        throw "Fichier $Label introuvable : $Path"
    }

    return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Get-ProjectConfig {
    $path = Join-Path $ScriptDir 'project.config.json'
    if (-not (Test-Path $path)) {
        throw @"
Fichier deploy/project.config.json introuvable.
Copiez deploy/project.config.example.json vers deploy/project.config.json
et adaptez appName, serviceName, appRoot, dllName et projectPath à votre projet.
"@
    }

    $cfg = Read-JsonConfig -Path $path -Label 'project.config.json'
    foreach ($key in @('appName', 'serviceName', 'appRoot', 'dllName', 'projectPath', 'healthCheckUrl', 'listenPort')) {
        if (-not $cfg.$key) {
            throw "Clé '$key' manquante dans deploy/project.config.json"
        }
    }

    return [PSCustomObject]@{
        AppName         = [string]$cfg.appName
        ServiceName     = [string]$cfg.serviceName
        AppRoot         = [string]$cfg.appRoot
        AppPath         = "$($cfg.appRoot)/app"
        DllName         = [string]$cfg.dllName
        ProjectPath     = Join-Path $RepoRoot ([string]$cfg.projectPath)
        HealthCheckUrl  = [string]$cfg.healthCheckUrl
        ListenPort      = [int]$cfg.listenPort
        SshIdentityKey  = if ($cfg.sshIdentityKey) { [string]$cfg.sshIdentityKey } else { 'ComptaDoc_deploy' }
    }
}

$Project = Get-ProjectConfig

$localOverride = Join-Path $ScriptDir 'deploy.local.ps1'
if (Test-Path $localOverride) {
    . $localOverride
}

$configPath = Join-Path $ScriptDir 'deploy-all.config.json'
if (Test-Path $configPath) {
    $fileConfig = Read-JsonConfig -Path $configPath -Label 'deploy-all.config.json'
    if ([string]::IsNullOrWhiteSpace($ServerHost) -and $fileConfig.ServerHost) {
        $ServerHost = [string]$fileConfig.ServerHost
    }
    if ($fileConfig.SshUser -and $SshUser -eq 'ubuntu') {
        $SshUser = [string]$fileConfig.SshUser
    }
    if ($fileConfig.SshPort -and $SshPort -eq 22) {
        $SshPort = [int]$fileConfig.SshPort
    }
}

if ([string]::IsNullOrWhiteSpace($ServerHost) -and $env:DEPLOY_SSH_HOST) {
    $ServerHost = $env:DEPLOY_SSH_HOST
}

if ([string]::IsNullOrWhiteSpace($ServerHost) -or $ServerHost -eq 'YOUR_SERVER_HOST_OR_IP') {
    throw @"
ServerHost requis. Indiquez -ServerHost, DEPLOY_SSH_HOST,
ou copiez deploy/deploy-all.config.example.json vers deploy/deploy-all.config.json.
"@
}

$defaultKey = Join-Path $env:USERPROFILE ".ssh/$($Project.SshIdentityKey)"
if ([string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    foreach ($keyName in @($Project.SshIdentityKey, 'cognidoc_deploy', 'id_ed25519', 'id_rsa')) {
        if ([string]::IsNullOrWhiteSpace($keyName)) { continue }
        $candidate = Join-Path $env:USERPROFILE ".ssh/$keyName"
        if (Test-Path $candidate) {
            $SshIdentityFile = $candidate
            break
        }
    }
}
if ([string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    throw @"
Clé SSH introuvable. Indiquez -SshIdentityFile ou placez une clé dans :
  $defaultKey
  $(Join-Path $env:USERPROFILE '.ssh/cognidoc_deploy')
Voir deploy/README.md pour la configuration SSH.
"@
}

$SshCommonArgs = @('-p', $SshPort, '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new')
$ScpCommonArgs = @('-P', $SshPort, '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=accept-new')
if (-not [string]::IsNullOrWhiteSpace($SshIdentityFile)) {
    if (-not (Test-Path $SshIdentityFile)) {
        throw "Clé SSH privée introuvable : $SshIdentityFile"
    }
    $resolvedKey = (Resolve-Path $SshIdentityFile).Path
    $SshCommonArgs += @('-i', $resolvedKey)
    $ScpCommonArgs += @('-i', $resolvedKey)
}

$BackupDir = "$($Project.AppRoot)/backups"
$SshTarget = "${SshUser}@${ServerHost}"
$StagingRemote = "/tmp/$($Project.ServiceName)-deploy-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

function Write-Step {
    param([int]$Number, [int]$Total, [string]$Message)
    Write-Host ""
    Write-Host "Étape $Number/$Total : $Message" -ForegroundColor Cyan
}

function Test-CommandAvailable {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Prérequis manquant : '$Name' introuvable dans le PATH."
    }
}

function Invoke-Ssh {
    param([string]$RemoteCommand)
    & ssh @SshCommonArgs $SshTarget $RemoteCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Commande SSH échouée (code $LASTEXITCODE). Voir deploy/README.md pour la clé SSH."
    }
}

function Invoke-RemoteBash {
    param([string]$Script)
    $lfOnly = ($Script -replace "`r", '').Trim() + "`n"
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($lfOnly)
    $b64 = [Convert]::ToBase64String($bytes)
    $remote = "echo '$b64' | base64 -d | bash -s"
    & ssh @SshCommonArgs $SshTarget $remote
    if ($LASTEXITCODE -ne 0) {
        throw "Script bash distant échoué (code $LASTEXITCODE)."
    }
}

function Get-AppSettingsFromProject {
    $projectDir = Split-Path $Project.ProjectPath -Parent
    $appSettingsPath = Join-Path $projectDir 'appsettings.json'

    if (-not (Test-Path $appSettingsPath)) {
        throw "Fichier appsettings.json introuvable : $appSettingsPath"
    }

    $json = Read-JsonConfig -Path $appSettingsPath -Label 'appsettings.json'

    $conn = [string]$json.ConnectionStrings.DefaultConnection
    if ([string]::IsNullOrWhiteSpace($conn)) {
        throw "ConnectionStrings:DefaultConnection manquant dans appsettings.json"
    }

    return [PSCustomObject]@{
        ConnectionString = $conn
    }
}

function Get-DatabaseNameFromConnectionString {
    param([string]$ConnectionString)

    if ($ConnectionString -match '(?i)(?:^|;)Database\s*=\s*([^;]+)') {
        return $Matches[1].Trim()
    }

    return $null
}

function Merge-ConnectionStringDatabase {
    param(
        [string]$ConnectionString,
        [string]$DatabaseName
    )

    if ([string]::IsNullOrWhiteSpace($DatabaseName)) {
        return $ConnectionString
    }

    $current = Get-DatabaseNameFromConnectionString -ConnectionString $ConnectionString
    if ($current -ceq $DatabaseName) {
        return $ConnectionString
    }

    if ($ConnectionString -match '(?i)Database\s*=') {
        return [regex]::Replace($ConnectionString, '(?i)(^|;)Database\s*=[^;]*', "`${1}Database=$DatabaseName")
    }

    return "$ConnectionString;Database=$DatabaseName"
}

function Get-ConnectionStringFromUserSecrets {
    if (-not (Test-Path $Project.ProjectPath)) {
        throw "Projet introuvable : $($Project.ProjectPath)"
    }

    $output = & dotnet user-secrets list --project $Project.ProjectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    foreach ($line in @($output)) {
        $text = [string]$line
        if ($text -match '^ConnectionStrings:DefaultConnection\s*=\s*(.+)$') {
            return $Matches[1].Trim()
        }
    }

    return $null
}

function New-SystemdServiceContent {
    param(
        [string]$ConnString
    )

    $connEscaped = $ConnString -replace '\\', '\\' -replace '"', '\"'
    $appRootEscaped = $Project.AppRoot -replace '\\', '\\' -replace '"', '\"'
    $serviceEscaped = $Project.ServiceName -replace '\\', '\\' -replace '"', '\"'
    $appNameEscaped = $Project.AppName -replace '\\', '\\' -replace '"', '\"'

    @"
[Unit]
Description=$($Project.AppName)
After=network.target

[Service]
Type=simple
User=$SshUser
Group=$SshUser
WorkingDirectory=$($Project.AppPath)
ExecStart=/usr/bin/dotnet $($Project.AppPath)/$($Project.DllName)
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment="UBUNTU1_CONNECTION_STRING=$connEscaped"
Environment="UBUNTU1_APP_ROOT=$appRootEscaped"
Environment="UBUNTU1_SERVICE_NAME=$serviceEscaped"
Environment="UBUNTU1_LISTEN_PORT=$($Project.ListenPort)"
Environment="UBUNTU1_APP_NAME=$appNameEscaped"
Restart=always
RestartSec=5
TimeoutStartSec=120
TimeoutStopSec=30
KillSignal=SIGINT
SyslogIdentifier=$($Project.ServiceName)
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
"@
}

Write-Host "Projet : $($Project.AppName) → service $($Project.ServiceName)" -ForegroundColor DarkGray
Write-Host "Vérification des prérequis (dotnet, ssh, scp)..." -ForegroundColor DarkGray
Test-CommandAvailable -Name 'dotnet'
Test-CommandAvailable -Name 'ssh'
Test-CommandAvailable -Name 'scp'
Write-Host "Prérequis OK." -ForegroundColor Green

$totalSteps = 7

Write-Step -Number 1 -Total $totalSteps -Message 'Publication Release vers ./publish'
if ($SkipPublish) {
    Write-Host "  -SkipPublish : réutilisation du dossier publish existant."
} else {
    if (-not (Test-Path $Project.ProjectPath)) {
        throw "Projet introuvable : $($Project.ProjectPath)"
    }
    & dotnet publish $Project.ProjectPath -c Release -o $PublishDir --no-self-contained
    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la publication (code $LASTEXITCODE)."
    }
}

$dllPath = Join-Path $PublishDir $Project.DllName
if (-not (Test-Path $dllPath)) {
    throw "Assembly introuvable : $dllPath"
}
Write-Host "Publication prête : $PublishDir" -ForegroundColor Green

Write-Host "Lecture ConnectionStrings et Database depuis appsettings.json..." -ForegroundColor DarkGray
$AppSettings = Get-AppSettingsFromProject
$authoritativeDatabase = Get-DatabaseNameFromConnectionString -ConnectionString $AppSettings.ConnectionString
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $fromSecrets = Get-ConnectionStringFromUserSecrets
    if (-not [string]::IsNullOrWhiteSpace($fromSecrets)) {
        $ConnectionString = $fromSecrets
        Write-Host "Chaîne de connexion chargée depuis user-secrets (mot de passe non affiché)." -ForegroundColor Green
    } else {
        $ConnectionString = $AppSettings.ConnectionString
        Write-Host "Chaîne de connexion chargée depuis appsettings.json." -ForegroundColor Yellow
    }
} else {
    Write-Host "Chaîne de connexion surchargée via -ConnectionString." -ForegroundColor Green
}

if (-not [string]::IsNullOrWhiteSpace($authoritativeDatabase)) {
    $aligned = Merge-ConnectionStringDatabase -ConnectionString $ConnectionString -DatabaseName $authoritativeDatabase
    if ($aligned -ne $ConnectionString) {
        Write-Host "Nom de base aligné sur appsettings.json : $authoritativeDatabase" -ForegroundColor Yellow
        $ConnectionString = $aligned
    }
}
    Write-Host "  Database=$authoritativeDatabase (depuis appsettings.json)" -ForegroundColor DarkGray

Write-Step -Number 2 -Total $totalSteps -Message 'Préparation des répertoires sur le serveur'
$prepareDirs = "sudo mkdir -p '$($Project.AppPath)' '$BackupDir' && sudo chown -R ${SshUser}:${SshUser} '$($Project.AppRoot)'"
Invoke-Ssh -RemoteCommand $prepareDirs

Write-Step -Number 3 -Total $totalSteps -Message "Sauvegarde de l'application existante (si présente)"
$backupScript = @"
set -eu
TIMESTAMP=`$(date +%Y%m%d-%H%M%S)
if [ -d '$($Project.AppPath)' ] && [ "`$(ls -A '$($Project.AppPath)' 2>/dev/null || true)" ]; then
  BACKUP_PATH='$BackupDir/'`$TIMESTAMP
  sudo cp -a '$($Project.AppPath)' "`$BACKUP_PATH"
  echo "Sauvegarde creee : `$BACKUP_PATH"
else
  echo "Aucune version precedente a sauvegarder."
fi
"@
Invoke-RemoteBash -Script $backupScript

Write-Step -Number 4 -Total $totalSteps -Message 'Transfert des fichiers publiés via SCP'
Invoke-Ssh -RemoteCommand "mkdir -p '$StagingRemote'"
& scp @ScpCommonArgs -r "${PublishDir}/." "${SshTarget}:${StagingRemote}/"
if ($LASTEXITCODE -ne 0) {
    throw "Échec du transfert SCP (code $LASTEXITCODE)."
}

Write-Step -Number 5 -Total $totalSteps -Message 'Déploiement et préservation de appsettings.Production.json'
$deployScript = @"
set -eu
PROD_SETTINGS='$($Project.AppPath)/appsettings.Production.json'
SAVED_SETTINGS=''

if [ -f "`$PROD_SETTINGS" ]; then
  SAVED_SETTINGS=`$(mktemp)
  cp "`$PROD_SETTINGS" "`$SAVED_SETTINGS"
fi

sudo rsync -a --delete --exclude 'appsettings.Production.json' '$StagingRemote/' '$($Project.AppPath)/'

if [ -n "`$SAVED_SETTINGS" ] && [ -f "`$SAVED_SETTINGS" ]; then
  sudo cp "`$SAVED_SETTINGS" "`$PROD_SETTINGS"
  rm -f "`$SAVED_SETTINGS"
fi

sudo chown -R ${SshUser}:${SshUser} '$($Project.AppPath)'
rm -rf '$StagingRemote'
"@
Invoke-RemoteBash -Script $deployScript

Write-Step -Number 6 -Total $totalSteps -Message "Configuration du service systemd $($Project.ServiceName)"
$serviceContent = New-SystemdServiceContent -ConnString $ConnectionString
$localServiceFile = Join-Path ([System.IO.Path]::GetTempPath()) "$($Project.ServiceName).service"
$remoteServiceFile = "/tmp/$($Project.ServiceName).service"

try {
    [System.IO.File]::WriteAllText($localServiceFile, $serviceContent, [System.Text.UTF8Encoding]::new($false))
    & scp @ScpCommonArgs $localServiceFile "${SshTarget}:${remoteServiceFile}"
    if ($LASTEXITCODE -ne 0) {
        throw "Échec de la copie du fichier systemd (code $LASTEXITCODE)."
    }
    Invoke-Ssh -RemoteCommand "sudo cp '$remoteServiceFile' '/etc/systemd/system/$($Project.ServiceName).service' && rm -f '$remoteServiceFile'"
} finally {
    if (Test-Path $localServiceFile) {
        Remove-Item -Path $localServiceFile -Force
    }
}

Write-Step -Number 7 -Total $totalSteps -Message 'Redémarrage du service et vérification HTTP'
Invoke-Ssh -RemoteCommand "sudo systemctl daemon-reload && sudo systemctl enable $($Project.ServiceName) && sudo systemctl restart $($Project.ServiceName)"
Start-Sleep -Seconds 3

Write-Host ""
Write-Host "Statut du service $($Project.ServiceName) :" -ForegroundColor Yellow
& ssh @SshCommonArgs $SshTarget "sudo systemctl status $($Project.ServiceName) --no-pager || true" 2>&1 | ForEach-Object { Write-Host $_ }

$curlScript = @"
set -eu
if curl -fsS -o /dev/null -w 'HTTP %{http_code}\n' '$($Project.HealthCheckUrl)'; then
  echo 'Healthcheck reussi.'
else
  exit 1
fi
"@
try {
    Invoke-RemoteBash -Script $curlScript
    Write-Host ""
    Write-Host "Déploiement terminé avec succès sur ${ServerHost} ($($Project.AppName))." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "Healthcheck échoué. Logs : ssh $SshTarget 'sudo journalctl -u $($Project.ServiceName) -e --no-pager'" -ForegroundColor Red
    throw
}
