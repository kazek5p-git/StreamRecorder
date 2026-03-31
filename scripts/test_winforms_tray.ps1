param(
    [string]$ExePath = "C:\Users\Kazek\Documents\StreamRecorder\dotnet\src\StreamRecorder.WinForms\bin\Release\net8.0-windows\StreamRecorder.exe"
)

$ErrorActionPreference = "Stop"
$script:LogPath = Join-Path $env:TEMP "streamrecorder_winforms_tray.log"
Set-Content -LiteralPath $script:LogPath -Value "" -Encoding UTF8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const uint KEYEVENTF_KEYUP = 0x0002;
}
"@

function Write-Step {
    param([string]$Message)
    Write-Host "[test] $Message"
    Add-Content -LiteralPath $script:LogPath -Value "[test] $Message" -Encoding UTF8
}

function Write-Failure {
    param([string]$Message)
    Write-Host "[fail] $Message"
    Add-Content -LiteralPath $script:LogPath -Value "[fail] $Message" -Encoding UTF8
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutMs = 10000,
        [int]$PollMs = 100
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (& $Condition) {
            return $true
        }

        Start-Sleep -Milliseconds $PollMs
    }

    return $false
}

function Press-Key {
    param([byte]$VirtualKey)

    [NativeMethods]::keybd_event($VirtualKey, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NativeMethods]::keybd_event($VirtualKey, 0, [NativeMethods]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}

function Press-Combo {
    param(
        [byte[]]$HoldKeys,
        [byte]$Key
    )

    foreach ($hold in $HoldKeys) {
        [NativeMethods]::keybd_event($hold, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
    }

    [NativeMethods]::keybd_event($Key, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NativeMethods]::keybd_event($Key, 0, [NativeMethods]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)

    for ($i = $HoldKeys.Length - 1; $i -ge 0; $i--) {
        Start-Sleep -Milliseconds 40
        [NativeMethods]::keybd_event($HoldKeys[$i], 0, [NativeMethods]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
    }

    Start-Sleep -Milliseconds 180
}

function Get-ElementLabel {
    param([System.Windows.Automation.AutomationElement]$Element)

    if ($null -eq $Element) {
        return "<null>"
    }

    $controlType = $Element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
    return "$controlType|$($Element.Current.Name)"
}

function Get-FocusedElement {
    return [System.Windows.Automation.AutomationElement]::FocusedElement
}

function Get-ProcessMainWindow {
    param([System.Diagnostics.Process]$Process)

    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $Process.Id
        )),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window
        ))
    )

    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $condition
    )
}

function Wait-ProcessWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutMs = 10000
    )

    $window = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $script:window = Get-ProcessMainWindow -Process $Process
        return $null -ne $script:window
    }

    if (-not $ok) {
        throw "Timed out waiting for the main window."
    }

    return $script:window
}

function Focus-Window {
    param([System.Windows.Automation.AutomationElement]$Window)

    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        throw "Window '$($Window.Current.Name)' does not have a native handle."
    }

    [void][NativeMethods]::ShowWindow($handle, [NativeMethods]::SW_RESTORE)
    [void][NativeMethods]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 250
}

function Minimize-Window {
    param([System.Windows.Automation.AutomationElement]$Window)

    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        throw "Window '$($Window.Current.Name)' does not have a native handle."
    }

    [void][NativeMethods]::ShowWindow($handle, [NativeMethods]::SW_MINIMIZE)
    Start-Sleep -Milliseconds 600
}

function Find-TrayElement {
    $roots = @()
    foreach ($className in @("Shell_TrayWnd", "NotifyIconOverflowWindow")) {
        $handle = [NativeMethods]::FindWindow($className, $null)
        if ($handle -ne [IntPtr]::Zero) {
            try {
                $roots += [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            }
            catch {
            }
        }
    }

    foreach ($root in $roots) {
        $buttonCondition = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                "StreamRecorder"
            )),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button
            ))
        )

        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
        if ($null -ne $found) {
            return $found
        }

        $fallbackCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            "StreamRecorder"
        )

        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fallbackCondition)
        if ($null -ne $found) {
            return $found
        }
    }

    return $null
}

function Dump-TrayTree {
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker

    foreach ($className in @("Shell_TrayWnd", "NotifyIconOverflowWindow")) {
        $handle = [NativeMethods]::FindWindow($className, $null)
        if ($handle -eq [IntPtr]::Zero) {
            continue
        }

        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            Add-Content -LiteralPath $script:LogPath -Value "[tree] root $className => $(Get-ElementLabel $root)" -Encoding UTF8

            $queue = New-Object System.Collections.Generic.Queue[System.Windows.Automation.AutomationElement]
            $queue.Enqueue($root)
            $count = 0

            while ($queue.Count -gt 0 -and $count -lt 120) {
                $current = $queue.Dequeue()
                Add-Content -LiteralPath $script:LogPath -Value "[tree] $(Get-ElementLabel $current)" -Encoding UTF8
                $count++

                $child = $walker.GetFirstChild($current)
                while ($null -ne $child) {
                    $queue.Enqueue($child)
                    $child = $walker.GetNextSibling($child)
                }
            }
        }
        catch {
        }
    }
}

function Wait-TrayElement {
    param([int]$TimeoutMs = 8000)

    $element = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $script:element = Find-TrayElement
        return $null -ne $script:element
    }

    if (-not $ok) {
        throw "Timed out waiting for the tray icon accessibility element."
    }

    return $script:element
}

function Focus-TrayIcon {
    param([int]$MaxSteps = 30)

    Press-Combo -HoldKeys @(0x5B) -Key 0x42
    Start-Sleep -Milliseconds 400

    for ($step = 0; $step -lt $MaxSteps; $step++) {
        $focused = Get-FocusedElement
        $label = Get-ElementLabel $focused
        Add-Content -LiteralPath $script:LogPath -Value "[focus] $label" -Encoding UTF8

        if ($null -ne $focused -and $focused.Current.Name -eq "StreamRecorder") {
            return $focused
        }

        if ($null -ne $focused -and $focused.Current.Name -in @("Show hidden icons", "Pokaż ukryte ikony")) {
            Press-Key -VirtualKey 0x0D
            Start-Sleep -Milliseconds 500
            $overflowElement = Find-TrayElement
            if ($null -ne $overflowElement) {
                try {
                    $overflowElement.SetFocus()
                    Start-Sleep -Milliseconds 250
                }
                catch {
                }

                return $overflowElement
            }
        }

        Press-Key -VirtualKey 0x27
    }

    return $null
}

function Wait-MenuItem {
    param(
        [string]$ExpectedName,
        [int]$TimeoutMs = 4000
    )

    $item = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $script:item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::MenuItem
                )),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty,
                    $ExpectedName
                ))
            ))
        )

        $focused = Get-FocusedElement
        return ($null -ne $script:item) -or (
            $null -ne $focused `
            -and $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem `
            -and $focused.Current.Name -eq $ExpectedName)
    }

    if (-not $ok) {
        $focused = Get-FocusedElement
        throw "Expected focused menu item '$ExpectedName', got '$(Get-ElementLabel $focused)'."
    }

    return $script:item
}

$process = $null
$failures = New-Object System.Collections.Generic.List[string]

try {
    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "Executable not found: $ExePath"
    }

    Write-Step "Launching WinForms rewrite"
    $process = Start-Process -FilePath $ExePath -PassThru
    $mainWindow = Wait-ProcessWindow -Process $process
    Focus-Window -Window $mainWindow

    Write-Step "Minimizing main window to tray"
    Minimize-Window -Window $mainWindow
    Dump-TrayTree
    $trayElement = Focus-TrayIcon
    if ($null -eq $trayElement) {
        $trayElement = Wait-TrayElement
    }
    Write-Step ("Tray element found: " + (Get-ElementLabel $trayElement))

    Write-Step "Focusing tray icon and opening context menu"
    $trayElement.SetFocus()
    Start-Sleep -Milliseconds 300
    Press-Combo -HoldKeys @(0x10) -Key 0x79
    [void](Wait-MenuItem -ExpectedName "Show")
    Press-Key -VirtualKey 0x1B

    Write-Step "Testing Enter on the tray icon"
    $trayElement = Focus-TrayIcon
    if ($null -eq $trayElement) {
        $trayElement = Wait-TrayElement
        try {
            $trayElement.SetFocus()
            Start-Sleep -Milliseconds 250
        }
        catch {
        }
    }

    Press-Key -VirtualKey 0x0D

    $restored = Wait-Until -TimeoutMs 5000 -Condition {
        $window = Get-ProcessMainWindow -Process $process
        if ($null -eq $window) {
            return $false
        }

        $focused = Get-FocusedElement
        return $focused.Current.Name -in @("Stations", "Add station")
    }

    if (-not $restored) {
        $focused = Get-FocusedElement
        throw "Enter on the tray icon did not restore focus to the application. Current focus: $(Get-ElementLabel $focused)."
    }

    Write-Step "Tray smoke test completed successfully"
}
catch {
    $failures.Add($_.Exception.Message)
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        try {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(3000)) {
                $process.Kill()
            }
        }
        catch {
        }
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Failure $failure
    }

    exit 1
}

exit 0
