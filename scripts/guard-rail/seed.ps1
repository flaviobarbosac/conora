# Fills flavio.barbosa.vix@gmail.com on the local Conora database only.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Assert-ConoraLocal.ps1"
Invoke-ConoraGuardSql -ScriptName 'seed.sql'
