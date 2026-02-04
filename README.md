JumpSelector v2
================

Overview
--------
JumpSelector v2 adds a Jump Select GUI to Jump Drives and enables automated
GPS jumps driven by scripts through Jump Drive Custom Data.

Key Features
------------
- GUI Jump Select: pick GPS from list or Blind Jump.
- Actions in G-menu: Jump Select, Auto Jump, Auto GPS.
- Script automation via Jump Drive Custom Data:
  - JS_CMD=JUMP triggers a jump to JS_GPS.
  - JS_STATUS reports OK or ERROR reasons.

Quick Start (GUI)
-----------------
1. Open a Jump Drive.
2. Click "Jump Select".
3. Choose a GPS entry.
4. Click "Confirm".

Quick Start (Automation)
------------------------
1. Open Jump Drive Custom Data.
2. Set:
   JS_CMD=JUMP
   JS_GPS=* Base Novus 004
3. The plugin executes the jump and sets:
   JS_CMD=IDLE
   JS_STATUS=OK: * Base Novus 004

Automation API (Custom Data)
----------------------------
Required keys:
- JS_CMD=JUMP | IDLE
- JS_GPS=<GPS name only>

Status keys:
- JS_STATUS=OK: <GPS>
- JS_STATUS=ERROR: <reason>

Possible errors:
- ERROR: GPS not found
- ERROR: Jump system not available
- ERROR: Distance too short
- ERROR: Distance exceeds max range
- ERROR: Obstacle detected
- ERROR: No permission

Script Example: GPS List From PB Custom Data
--------------------------------------------
This version reads GPS names from the Programmable Block Custom Data.
Put one GPS name per line.

PB Custom Data example:
* Base Novus 004
* Jump Luna
* Jump Desolo

Programmable Block script (no Program class):

```csharp
const string jumpDriveNameContains = "Jump Drive Type II";

bool running = false;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    LoadState();
}

public void Main(string argument, UpdateType updateSource)
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    if (!string.IsNullOrWhiteSpace(argument))
    {
        var cmd = argument.Trim().ToLowerInvariant();
        if (cmd == "start") { running = true; SaveState(); Echo("START"); return; }
        if (cmd == "stop") { running = false; SaveState(); Echo("STOP"); return; }
    }

    Echo("Running: " + running);
    if (!running) return;

    var gpsQueue = ReadGpsFromCustomData();
    if (gpsQueue.Count == 0)
    {
        Echo("Kolejka pusta.");
        return;
    }

    var drive = FindDrive();
    if (drive == null)
    {
        Echo("Brak JumpDrive.");
        return;
    }

    string status = ReadStatus(drive.CustomData);
    Echo("Status: " + (status ?? "<none>"));

    if (status != null && status.StartsWith("OK:"))
    {
        gpsQueue.RemoveAt(0);
        WriteGpsToCustomData(gpsQueue);
        ClearStatus(drive);
        Echo("Skok OK, pozostalo: " + gpsQueue.Count);
        return;
    }

    string gpsName = gpsQueue[0];
    SendJump(drive, gpsName);
    Echo("Wyslano: " + gpsName);
}

Sandbox.ModAPI.Ingame.IMyTerminalBlock FindDrive()
{
    var drives = new System.Collections.Generic.List<Sandbox.ModAPI.Ingame.IMyJumpDrive>();
    GridTerminalSystem.GetBlocksOfType(drives, d => d.CustomName.Contains(jumpDriveNameContains));
    return drives.Count > 0 ? drives[0] as Sandbox.ModAPI.Ingame.IMyTerminalBlock : null;
}

System.Collections.Generic.List<string> ReadGpsFromCustomData()
{
    var list = new System.Collections.Generic.List<string>();
    var lines = Me.CustomData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var line in lines)
    {
        var l = line.Trim();
        if (string.IsNullOrWhiteSpace(l)) continue;
        if (l.StartsWith("GPS:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = l.Split(':');
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                list.Add(parts[1].Trim());
        }
        else
        {
            list.Add(l);
        }
    }
    return list;
}

void WriteGpsToCustomData(System.Collections.Generic.List<string> list)
{
    Me.CustomData = string.Join("\n", list);
}

void SendJump(Sandbox.ModAPI.Ingame.IMyTerminalBlock drive, string gpsName)
{
    string data = "JS_CMD=JUMP\nJS_GPS=" + gpsName + "\n";
    drive.CustomData = data;
}

string ReadStatus(string data)
{
    if (string.IsNullOrWhiteSpace(data)) return null;
    var lines = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var l in lines)
    {
        if (l.StartsWith("JS_STATUS=", StringComparison.OrdinalIgnoreCase))
            return l.Substring("JS_STATUS=".Length).Trim();
    }
    return null;
}

void ClearStatus(Sandbox.ModAPI.Ingame.IMyTerminalBlock drive)
{
    string data = drive.CustomData ?? "";
    var lines = new System.Collections.Generic.List<string>();
    var parts = data.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var l in parts)
    {
        if (!l.StartsWith("JS_STATUS=", StringComparison.OrdinalIgnoreCase))
            lines.Add(l);
    }
    drive.CustomData = string.Join("\n", lines);
}

void SaveState()
{
    Storage = running ? "RUN" : "STOP";
}

void LoadState()
{
    running = (Storage ?? "").Trim().Equals("RUN", StringComparison.OrdinalIgnoreCase);
}
```

Quick Start for Scripts
-----------------------
1. Add the script to a Programmable Block.
2. Put GPS names into the PB Custom Data (one per line).
3. Run the PB with argument `start`.
4. Stop automation with argument `stop`.

Notes
-----
- JS_GPS must be a GPS name only (no "GPS:" prefix or coordinates).
- The plugin updates JS_STATUS so your script can react to success or errors.
