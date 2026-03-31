param(
    [string]$ExePath = "C:\Users\Kazek\Documents\StreamRecorder\dotnet\src\StreamRecorder.WinForms\bin\Release\net8.0-windows\StreamRecorder.exe"
)

$ErrorActionPreference = "Stop"
$script:LogPath = Join-Path $env:TEMP "streamrecorder_winforms_accessibility.log"
Set-Content -LiteralPath $script:LogPath -Value "" -Encoding UTF8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

function Write-Step {
    param([string]$Message)
    Write-Host "[test] $Message"
    Add-Content -LiteralPath $script:LogPath -Value "[test] $Message" -Encoding UTF8
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

function Get-WindowByName {
    param(
        [string]$Name,
        [int]$TimeoutMs = 5000
    )

    $window = $null
    $ok = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $script:window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window
                )),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty,
                    $Name
                ))
            ))
        )

        return $null -ne $script:window
    }

    if (-not $ok) {
        throw "Timed out waiting for window '$Name'."
    }

    return $script:window
}

function Find-Descendant {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType
    )

    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name
        )),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType
        ))
    )

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-FirstByControlType {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [System.Windows.Automation.ControlType]$ControlType
    )

    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType
        ))
    )
}

function Focus-Window {
    param([System.Windows.Automation.AutomationElement]$Window)

    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        throw "Window '$($Window.Current.Name)' does not have a native handle."
    }

    [void][NativeMethods]::ShowWindow($handle, 5)
    [void][NativeMethods]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 250
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        throw "Element '$($Element.Current.Name)' does not support InvokePattern."
    }

    $pattern.Invoke()
    Start-Sleep -Milliseconds 250
}

function Expand-Element {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
        throw "Element '$($Element.Current.Name)' does not support ExpandCollapsePattern."
    }

    $pattern.Expand()
    Start-Sleep -Milliseconds 250
}

function Get-FocusedElement {
    return [System.Windows.Automation.AutomationElement]::FocusedElement
}

function Get-ElementLabel {
    param([System.Windows.Automation.AutomationElement]$Element)

    if ($null -eq $Element) {
        return "<null>"
    }

    $controlType = $Element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
    return "$controlType|$($Element.Current.Name)"
}

function Dump-Descendants {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [int]$Limit = 80
    )

    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $queue = New-Object System.Collections.Generic.Queue[System.Windows.Automation.AutomationElement]
    $queue.Enqueue($Root)
    $count = 0

    while ($queue.Count -gt 0 -and $count -lt $Limit) {
        $current = $queue.Dequeue()
        Write-Host ("[uia] " + (Get-ElementLabel $current))
        Add-Content -LiteralPath $script:LogPath -Value ("[uia] " + (Get-ElementLabel $current)) -Encoding UTF8
        $count++

        $child = $walker.GetFirstChild($current)
        while ($null -ne $child) {
            $queue.Enqueue($child)
            $child = $walker.GetNextSibling($child)
        }
    }
}

function Assert-FocusedName {
    param(
        [string]$ExpectedName,
        [string]$Message
    )

    $actual = Get-FocusedElement
    if ($actual.Current.Name -ne $ExpectedName) {
        throw "$Message. Expected focus '$ExpectedName', got '$(Get-ElementLabel $actual)'."
    }
}

function Send-Keys {
    param([string]$Keys)

    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 250
}

function Get-Value {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        throw "Element '$($Element.Current.Name)' does not support ValuePattern."
    }

    return $pattern.Current.Value
}

function Set-Value {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Value
    )

    $pattern = $null
    if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        throw "Element '$($Element.Current.Name)' does not support ValuePattern."
    }

    $pattern.SetValue($Value)
    Start-Sleep -Milliseconds 150
}

$failures = New-Object System.Collections.Generic.List[string]
$process = $null

function Run-Check {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Step $Name
    try {
        & $Action
    }
    catch {
        $script:failures.Add("${Name}: $($_.Exception.Message)")
    }
}

try {
    if (-not (Test-Path -LiteralPath $ExePath)) {
        throw "Executable not found: $ExePath"
    }

    Write-Step "Launching WinForms rewrite"
    $process = Start-Process -FilePath $ExePath -PassThru
    $mainWindow = Wait-ProcessWindow -Process $process
    Focus-Window -Window $mainWindow

    $menuBar = $null
    $addButton = $null
    $showLogButton = $null
    $stationList = $null

    Run-Check "Checking main window controls" {
        $script:menuBar = Find-FirstByControlType -Root $mainWindow -ControlType ([System.Windows.Automation.ControlType]::MenuBar)
        $script:addButton = Find-Descendant -Root $mainWindow -Name "Add station" -ControlType ([System.Windows.Automation.ControlType]::Button)
        $script:showLogButton = Find-Descendant -Root $mainWindow -Name "Show log" -ControlType ([System.Windows.Automation.ControlType]::Button)
        $script:stationList = Find-Descendant -Root $mainWindow -Name "Stations" -ControlType ([System.Windows.Automation.ControlType]::List)
        if ($null -eq $script:menuBar -or $null -eq $script:addButton -or $null -eq $script:showLogButton -or $null -eq $script:stationList) {
            Dump-Descendants -Root $mainWindow
            throw "Main window is missing one or more expected accessible controls."
        }
    }

    Run-Check "Checking native menu focus" {
        Focus-Window -Window $mainWindow
        Send-Keys "%"
        Assert-FocusedName -ExpectedName "File" -Message "Alt should focus the File menu"
        Send-Keys "{RIGHT}"
        Assert-FocusedName -ExpectedName "Help" -Message "Right arrow should move to Help menu"
        Send-Keys "{ESC}"
    }

    Run-Check "Checking main tab order" {
        $addButton.SetFocus()
        Start-Sleep -Milliseconds 150
        Assert-FocusedName -ExpectedName "Add station" -Message "Add station button should take focus"
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Show log" -Message "Tab from Add station should move to Show log"
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Stations" -Message "Tab from Show log should move to the station list"
    }

    Run-Check "Checking log window focus and Escape" {
        Invoke-Element -Element $showLogButton
        $logWindow = Get-WindowByName -Name "Log"
        Focus-Window -Window $logWindow
        Assert-FocusedName -ExpectedName "Log entries" -Message "Log window should focus the log list after opening"
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Close" -Message "Tab in Log should move to Close"
        Send-Keys "{ESC}"
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $focused = Get-FocusedElement
            return $focused.Current.Name -eq "Show log"
        })) {
            throw "After closing Log, focus should return to Show log."
        }
    }

    Run-Check "Checking station dialog keyboard input and Escape" {
        Invoke-Element -Element $addButton
        $stationDialog = Get-WindowByName -Name "Add station"
        Focus-Window -Window $stationDialog
        Assert-FocusedName -ExpectedName "Station name" -Message "Add station should focus the name field"
        Send-Keys "Test Station"
        $nameField = Find-Descendant -Root $stationDialog -Name "Station name" -ControlType ([System.Windows.Automation.ControlType]::Edit)
        if ((Get-Value -Element $nameField) -ne "Test Station") {
            throw "Typing into the station name field failed."
        }
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Stream URL" -Message "Tab from name should move to URL"
        Send-Keys "http://example.com/stream"
        $urlField = Find-Descendant -Root $stationDialog -Name "Stream URL" -ControlType ([System.Windows.Automation.ControlType]::Edit)
        if ((Get-Value -Element $urlField) -ne "http://example.com/stream") {
            throw "Typing into the station URL field failed."
        }
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Username" -Message "Tab from URL should move to Username"
        Send-Keys "user"
        Send-Keys "{TAB}"
        Assert-FocusedName -ExpectedName "Password" -Message "Tab from Username should move to Password"
        Send-Keys "secret"
        Send-Keys "+{TAB}"
        Assert-FocusedName -ExpectedName "Username" -Message "Shift+Tab from Password should move back to Username"
        Send-Keys "{ESC}"
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Window
                    )),
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty,
                        "Add station"
                    ))
                ))
            )
            return $null -eq $window
        })) {
            throw "Escape should close the Add station dialog."
        }
    }

    Run-Check "Adding a station for context-menu and schedules testing" {
        Invoke-Element -Element $addButton
        $stationDialog = Get-WindowByName -Name "Add station"
        Focus-Window -Window $stationDialog
        $nameField = Find-Descendant -Root $stationDialog -Name "Station name" -ControlType ([System.Windows.Automation.ControlType]::Edit)
        $urlField = Find-Descendant -Root $stationDialog -Name "Stream URL" -ControlType ([System.Windows.Automation.ControlType]::Edit)
        Set-Value -Element $nameField -Value "Radio Test"
        Set-Value -Element $urlField -Value "http://example.com/live"
        $urlField.SetFocus()
        Start-Sleep -Milliseconds 150
        Send-Keys "{ENTER}"
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $list = Find-Descendant -Root $mainWindow -Name "Stations" -ControlType ([System.Windows.Automation.ControlType]::List)
            return $null -ne $list
        })) {
            throw "The main station list did not recover after adding a station."
        }
        Focus-Window -Window $mainWindow
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $focused = Get-FocusedElement
            return $focused.Current.Name -eq "Stations"
        })) {
            throw "After adding a station, focus should return to the station list."
        }
    }

    Run-Check "Checking station context menu from the keyboard" {
        Send-Keys "+{F10}"
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $focused = Get-FocusedElement
            return $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem
        })) {
            throw "Shift+F10 should open the station context menu."
        }
        $firstMenu = Get-FocusedElement
        if ($firstMenu.Current.Name -ne "Add station") {
            throw "Station context menu should open on 'Add station', got '$(Get-ElementLabel $firstMenu)'."
        }
        Send-Keys "{DOWN}"
        Assert-FocusedName -ExpectedName "Start recording" -Message "Context menu should contain Start recording"
        Send-Keys "{DOWN}"
        Assert-FocusedName -ExpectedName "Stop recording" -Message "Context menu should contain Stop recording"
        Send-Keys "{DOWN}"
        Assert-FocusedName -ExpectedName "Edit station" -Message "Context menu should contain Edit station"
        Send-Keys "{DOWN}"
        Assert-FocusedName -ExpectedName "Schedules..." -Message "Context menu should contain Schedules"
        Send-Keys "{ESC}"
    }

    Run-Check "Checking settings dialog Escape" {
        $fileMenu = Find-Descendant -Root $mainWindow -Name "File" -ControlType ([System.Windows.Automation.ControlType]::MenuItem)
        if ($null -eq $fileMenu) {
            throw "The File menu was not found."
        }
        Expand-Element -Element $fileMenu
        $settingsItem = Find-Descendant -Root [System.Windows.Automation.AutomationElement]::RootElement -Name "Settings" -ControlType ([System.Windows.Automation.ControlType]::MenuItem)
        if ($null -eq $settingsItem) {
            throw "The Settings menu item was not found after expanding File."
        }
        Invoke-Element -Element $settingsItem
        $settingsWindow = Get-WindowByName -Name "Settings"
        Focus-Window -Window $settingsWindow
        Assert-FocusedName -ExpectedName "Launch application at Windows startup" -Message "Settings should focus the first checkbox"
        Send-Keys "{ESC}"
        if (-not (Wait-Until -TimeoutMs 4000 -Condition {
            $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Window
                    )),
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty,
                        "Settings"
                    ))
                ))
            )
            return $null -eq $window
        })) {
            throw "Escape should close the Settings dialog."
        }
    }

    Write-Step "Accessibility smoke test completed"
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
        Write-Host "[fail] $failure"
        Add-Content -LiteralPath $script:LogPath -Value "[fail] $failure" -Encoding UTF8
    }

    exit 1
}

exit 0
