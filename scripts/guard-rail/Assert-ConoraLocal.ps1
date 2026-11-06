# Stops unless the Conora database is the local Docker container.
$ErrorActionPreference = 'Stop'

$script:ConoraGuardContainer = 'conora-postgres'

function Assert-ConoraLocal {
    $raw = docker inspect $script:ConoraGuardContainer 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $raw) {
        throw 'O Postgres local do Conora não está no Docker. Nada foi alterado.'
    }

    $info = $raw | ConvertFrom-Json
    $container = @($info)[0]
    if ($container.State.Status -ne 'running') {
        throw 'O container conora-postgres não está rodando. Nada foi alterado.'
    }

    $binds = @($container.NetworkSettings.Ports.'5432/tcp')
    if ($binds.Count -eq 0) {
        throw 'O Postgres do Conora não está publicado nesta máquina. Nada foi alterado.'
    }

    foreach ($bind in $binds) {
        if ($bind.HostIp -notin @('0.0.0.0', '127.0.0.1', '::', '')) {
            throw "Porta publicada fora do localhost ($($bind.HostIp)). Nada foi alterado."
        }
    }
}

function Invoke-ConoraGuardSql {
    param([Parameter(Mandatory = $true)][string]$ScriptName)

    Assert-ConoraLocal

    $root = Split-Path -Parent $PSCommandPath
    docker exec $script:ConoraGuardContainer rm -rf /tmp/conora-guard-rail | Out-Null
    docker cp $root/. "${script:ConoraGuardContainer}:/tmp/conora-guard-rail"
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível copiar os scripts para o Postgres local.'
    }

    docker exec $script:ConoraGuardContainer psql -U conora -d conora -v ON_ERROR_STOP=1 -f "/tmp/conora-guard-rail/sql/$ScriptName"
    $code = $LASTEXITCODE
    docker exec $script:ConoraGuardContainer rm -rf /tmp/conora-guard-rail | Out-Null
    if ($code -ne 0) {
        throw "O script $ScriptName falhou no localhost. Nenhum outro ambiente foi usado."
    }
}
