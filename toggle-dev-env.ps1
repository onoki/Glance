$varName = "GLANCE_USE_DEV_SERVER"
$scope = "User"

$current = [Environment]::GetEnvironmentVariable($varName, $scope)
$enabled = $current -eq "1"
$newValue = if ($enabled) { "0" } else { "1" }

[Environment]::SetEnvironmentVariable($varName, $newValue, $scope)
$env:GLANCE_USE_DEV_SERVER = $newValue

$label = if ($newValue -eq "1") { "Yes" } else { "No" }
Write-Host "$varName set to $newValue ($scope scope)."
Write-Host "C# app uses development environment: $label"
