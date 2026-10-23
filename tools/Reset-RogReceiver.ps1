param(
    [ValidateRange(0, 60)]
    [int]$OffSeconds = 3,
    [ValidateRange(0, 120)]
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Stop'

$receiver = Get-PnpDevice -PresentOnly -Class USB -ErrorAction Stop |
    Where-Object InstanceId -Like 'USB\VID_0B05&PID_1AD0\*' |
    Select-Object -First 1
if ($null -eq $receiver) {
    throw 'ROG receiver USB\VID_0B05&PID_1AD0 was not found.'
}

Write-Output "Receiver instance: $($receiver.InstanceId)"

if ($OffSeconds -gt 0) {
    Disable-PnpDevice -InstanceId $receiver.InstanceId -Confirm:$false -ErrorAction Stop
    Write-Output "Disabled receiver for $OffSeconds seconds."
    Start-Sleep -Seconds $OffSeconds
}

Enable-PnpDevice -InstanceId $receiver.InstanceId -Confirm:$false -ErrorAction Stop
Write-Output 'Enabled receiver.'

$deadline = (Get-Date).AddSeconds($WaitSeconds)
do {
    Start-Sleep -Milliseconds 500
    $state = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object InstanceId -Like 'USB\VID_0B05&PID_1AD0\*' |
        Select-Object -First 1
} until (($null -ne $state -and $state.Status -eq 'OK') -or (Get-Date) -ge $deadline)

if ($null -eq $state) {
    throw 'ROG receiver did not return after re-enabling.'
}

$state | Select-Object Status, Class, FriendlyName, InstanceId | Format-Table -AutoSize
if ($state.Status -ne 'OK') {
    throw "ROG receiver returned with status $($state.Status)."
}

Write-Output 'ROG receiver recovered successfully.'
