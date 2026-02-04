using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Terminal.Controls;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace JumpSelector.Plugin
{
    public class JumpSelectorPatch
    {
        private const string JsCmdKey = "JS_CMD";
        private const string JsGpsKey = "JS_GPS";
        private const string JsQueueKey = "JS_QUEUE";
        private const string JsEndKey = "JS_END";
        private const string JsStatusKey = "JS_STATUS";
        private static bool _autoConfirmNext;
        private static long _autoConfirmUserId;
        private static int _guiLogCountdown;
        private static bool _guiHooked;

        public JumpSelectorPatch()
        {
        }

        public static void JumpSelectAction()
        {
            MyTerminalAction<MyJumpDrive> myTerminalAction = new MyTerminalAction<MyJumpDrive>("JumpSelect", new StringBuilder("Jump Select"), MyTerminalActionIcons.STATION_ON);
            myTerminalAction.Action = new Action<MyJumpDrive>(ShowJumpSelector);
            myTerminalAction.Writer = delegate (MyJumpDrive block, StringBuilder builder)
            {
                builder.Append("Jump Select");
            };
            myTerminalAction.ValidForGroups = false;
            myTerminalAction.InvalidToolbarTypes = new List<MyToolbarType>
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat
            };
            MyTerminalControlFactory.AddAction<MyJumpDrive>(myTerminalAction);

            MyTerminalAction<MyJumpDrive> autoJumpAction = new MyTerminalAction<MyJumpDrive>("JumpSelectAuto", new StringBuilder("Jump Select - Auto Jump"), MyTerminalActionIcons.STATION_ON);
            autoJumpAction.Action = new Action<MyJumpDrive>(AutoJump);
            autoJumpAction.Writer = delegate (MyJumpDrive block, StringBuilder builder)
            {
                builder.Append("Auto Jump");
            };
            autoJumpAction.ValidForGroups = false;
            autoJumpAction.InvalidToolbarTypes = new List<MyToolbarType>
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat
            };
            MyTerminalControlFactory.AddAction<MyJumpDrive>(autoJumpAction);

            MyTerminalAction<MyJumpDrive> autoJumpGpsAction = new MyTerminalAction<MyJumpDrive>("JumpSelectAutoGps", new StringBuilder("Jump Select - Auto GPS"), MyTerminalActionIcons.STATION_ON);
            autoJumpGpsAction.ActionWithParameters = new Action<MyJumpDrive, ListReader<TerminalActionParameter>>(AutoJumpToGps);
            autoJumpGpsAction.Writer = delegate (MyJumpDrive block, StringBuilder builder)
            {
                builder.Append("Auto GPS");
            };
            autoJumpGpsAction.ValidForGroups = false;
            autoJumpGpsAction.InvalidToolbarTypes = new List<MyToolbarType>
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat
            };
            autoJumpGpsAction.ParameterDefinitions.Add(TerminalActionParameter.Get(string.Empty));
            MyTerminalControlFactory.AddAction<MyJumpDrive>(autoJumpGpsAction);
        }

        public static void ShowJumpSelector(MyJumpDrive block)
        {
            if (HasPermission(block))
            {
                MyGuiSandbox.AddScreen(new JumpSelectorGui(block));
            }
            else
            {
                MyGuiSandbox.Show(new StringBuilder("You do not have permission to use this block"), VRage.Utils.MyStringId.GetOrCompute("Invalid Permissions"));
                return;
            }
        }

        public static void AutoJump(MyJumpDrive block)
        {
            if (!HasPermission(block))
            {
                MyGuiSandbox.Show(new StringBuilder("You do not have permission to use this block"), VRage.Utils.MyStringId.GetOrCompute("Invalid Permissions"));
                return;
            }

            string error;
            if (!JumpSelectorGui.TryAutoJump(block, out error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MyGuiSandbox.Show(new StringBuilder(error), VRage.Utils.MyStringId.GetOrCompute("Jump Selector"));
                }
            }
        }

        public static void AutoJumpToGps(MyJumpDrive block, ListReader<TerminalActionParameter> parameters)
        {
            if (!HasPermission(block))
            {
                MyGuiSandbox.Show(new StringBuilder("You do not have permission to use this block"), VRage.Utils.MyStringId.GetOrCompute("Invalid Permissions"));
                return;
            }

            if (parameters.Count == 0)
            {
                MyGuiSandbox.Show(new StringBuilder("Missing GPS name parameter."), VRage.Utils.MyStringId.GetOrCompute("Jump Selector"));
                return;
            }

            string gpsName = parameters[0].Value as string;
            string error;
            if (!JumpSelectorGui.TryJumpToGpsByName(block, gpsName, out error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MyGuiSandbox.Show(new StringBuilder(error), VRage.Utils.MyStringId.GetOrCompute("Jump Selector"));
                }
            }
        }

        private static bool HasPermission(MyJumpDrive block)
        {
            return block != null
                && (block.IDModule.ShareMode == MyOwnershipShareModeEnum.All
                    || block.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Owner
                    || block.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.FactionShare);
        }

        public static void JumpDriveUpdatePostfix(MyJumpDrive __instance)
        {
            try
            {
                ProcessScriptCommand(__instance);
                EnsureGuiHooked();
            }
            catch
            {
                // avoid crashing game loop
            }
        }

        private static void ProcessScriptCommand(MyJumpDrive block)
        {
            if (block == null || block.MarkedForClose)
            {
                return;
            }

            string data = block.CustomData;
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            List<string> lines = data.Replace("\r\n", "\n").Split('\n').ToList();

            int cmdIndex = -1;
            int gpsIndex = -1;
            int statusIndex = -1;
            int queueStart = -1;
            int queueEnd = -1;

            string cmd = null;
            string gps = null;
            List<string> queue = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith(JsCmdKey + "=", StringComparison.OrdinalIgnoreCase))
                {
                    cmdIndex = i;
                    cmd = line.Substring(JsCmdKey.Length + 1).Trim();
                    continue;
                }
                if (line.StartsWith(JsGpsKey + "=", StringComparison.OrdinalIgnoreCase))
                {
                    gpsIndex = i;
                    gps = line.Substring(JsGpsKey.Length + 1).Trim();
                    continue;
                }
                if (line.StartsWith(JsStatusKey + "=", StringComparison.OrdinalIgnoreCase))
                {
                    statusIndex = i;
                    continue;
                }
                if (line.StartsWith(JsQueueKey + "=", StringComparison.OrdinalIgnoreCase))
                {
                    queueStart = i;
                    queue = new List<string>();
                    string rest = line.Substring(JsQueueKey.Length + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(rest))
                    {
                        queue.Add(rest);
                    }
                    int j = i + 1;
                    for (; j < lines.Count; j++)
                    {
                        string ql = lines[j].Trim();
                        if (ql.StartsWith(JsEndKey, StringComparison.OrdinalIgnoreCase)
                            || ql.StartsWith("JS_", StringComparison.OrdinalIgnoreCase)
                            || ql.StartsWith("["))
                        {
                            break;
                        }
                        if (!string.IsNullOrWhiteSpace(ql))
                        {
                            queue.Add(ql);
                        }
                    }
                    queueEnd = j;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(cmd) || !cmd.Equals("JUMP", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WriteLog($"[JumpSelector] JS_CMD=JUMP GPS='{gps ?? ""}'");

            if (string.IsNullOrWhiteSpace(gps) && queue != null && queue.Count > 0)
            {
                gps = queue[0];
            }

            if (string.IsNullOrWhiteSpace(gps))
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: GPS name is empty");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            if (!HasPermission(block))
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: No permission");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            MyGridJumpDriveSystem jumpSystem = block.CubeGrid?.GridSystems?.JumpSystem;
            if (jumpSystem == null)
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: Jump system not available");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            IMyGps gpsObj = FindGpsByName(gps);
            if (gpsObj == null)
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: GPS not found");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            long userId = MySession.Static != null ? MySession.Static.LocalPlayerId : 0L;
            double distance = Vector3D.Distance(block.CubeGrid.WorldMatrix.Translation, gpsObj.Coords);
            double minDistance = jumpSystem.GetMinJumpDistance(userId);
            double maxDistance = jumpSystem.GetMaxJumpDistance(userId);

            if (distance < minDistance)
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: Distance too short");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            if (distance > maxDistance)
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: Distance exceeds max range");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            if (!TryFindSuitableJumpLocation(jumpSystem, gpsObj.Coords).HasValue)
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: Obstacle detected");
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            if (!RequestJumpLikeGui(jumpSystem, gpsObj.Name, gpsObj.Coords, userId, out string error))
            {
                UpdateKey(lines, ref statusIndex, JsStatusKey, "ERROR: " + (error ?? "Unknown"));
                UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");
                block.CustomData = string.Join("\n", lines);
                return;
            }

            UpdateKey(lines, ref statusIndex, JsStatusKey, "OK: " + gps);
            UpdateKey(lines, ref cmdIndex, JsCmdKey, "IDLE");

            if (queue != null && queue.Count > 0)
            {
                queue.RemoveAt(0);
                UpdateQueue(lines, queueStart, queueEnd, queue);
            }

            block.CustomData = string.Join("\n", lines);
        }

        private static IMyGps FindGpsByName(string gpsName)
        {
            if (string.IsNullOrWhiteSpace(gpsName))
            {
                return null;
            }
            List<IMyGps> list = new List<IMyGps>();
            MySession.Static.Gpss.GetGpsList(MySession.Static.LocalPlayerId, list);
            foreach (IMyGps gps in list)
            {
                if (string.Equals(gps.Name, gpsName, StringComparison.OrdinalIgnoreCase))
                {
                    return gps;
                }
            }
            return null;
        }

        public static bool JumpConfirmationPrefix(ref StringBuilder __result, string name, double distance, double actualDistance, long userId, bool obstacleDetected)
        {
            return true;
        }

        public static bool RequestJumpPrefix(MyGridJumpDriveSystem __instance, Vector3D __0, long __1, float __2, long? __3)
        {
            return true;
        }

        public static bool RequestJumpPrefix(MyGridJumpDriveSystem __instance, string __0, Vector3D __1, long __2, BoundingBoxD? __3, float __4, long? __5)
        {
            return true;
        }

        private static void CallJump(MyGridJumpDriveSystem system, Vector3D destination, long userId, float distance, long? shipId)
        {
            var method = AccessTools.Method(typeof(MyGridJumpDriveSystem), "Jump", new Type[]
            {
                typeof(Vector3D),
                typeof(long),
                typeof(float),
                typeof(long?)
            });
            if (method != null)
            {
                method.Invoke(system, new object[] { destination, userId, distance, shipId });
            }
        }

        private static Vector3D? TryFindSuitableJumpLocation(MyGridJumpDriveSystem system, Vector3D destination)
        {
            var method = AccessTools.Method(typeof(MyGridJumpDriveSystem), "FindSuitableJumpLocation", new Type[]
            {
                typeof(Vector3D)
            });
            if (method != null)
            {
                return (Vector3D?)method.Invoke(system, new object[] { destination });
            }
            return destination;
        }

        private static bool RequestJumpLikeGui(MyGridJumpDriveSystem system, string name, Vector3D destination, long userId, out string error)
        {
            error = null;
            try
            {
                var method = AccessTools.Method(typeof(MyGridJumpDriveSystem), "RequestJump", new Type[]
                {
                    typeof(string),
                    typeof(Vector3D),
                    typeof(long),
                    typeof(BoundingBoxD?),
                    typeof(float),
                    typeof(long?)
                });
                if (method == null)
                {
                    error = "RequestJump method not found";
                    return false;
                }

                method.Invoke(system, new object[] { name, destination, userId, null, 0f, null });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void SetAutoConfirmForNextJump()
        {
            long userId = 0L;
            if (MySession.Static != null)
            {
                userId = MySession.Static.LocalPlayerId;
            }
            _autoConfirmUserId = userId;
            _autoConfirmNext = true;
            _guiLogCountdown = 300;
        }

        private static void EnsureGuiHooked()
        {
            if (_guiHooked)
            {
                return;
            }
            _guiHooked = true;
            MyGuiSandbox.GuiControlCreated += OnGuiControlCreated;
        }

        public static void AddScreenPostfix(MyGuiScreenBase screen)
        {
            if (screen == null)
            {
                return;
            }

            if (_guiLogCountdown > 0)
            {
                _guiLogCountdown--;
                string screenName = screen.GetFriendlyName();
                if (string.IsNullOrWhiteSpace(screenName))
                {
                    screenName = screen.GetType().FullName;
                }
                WriteLog($"[JumpSelector] GUI Screen: {screenName} ({screen.GetType().FullName})");

                foreach (var control in screen.Controls)
                {
                    if (control == null)
                    {
                        continue;
                    }

                    string ctrlName = control.Name ?? string.Empty;
                    string ctrlType = control.GetType().FullName ?? "Unknown";
                    string ctrlText = string.Empty;
                    var button = control as MyGuiControlButton;
                    if (button != null && button.Text != null)
                    {
                        ctrlText = button.Text.ToString();
                    }

                    WriteLog($"[JumpSelector]   Control: {ctrlType} Name='{ctrlName}' Text='{ctrlText}'");
                }
            }

            TryAutoConfirmMessageBox(screen);
        }

        private static void TryAutoConfirmMessageBox(MyGuiScreenBase screen)
        {
            if (!_autoConfirmNext)
            {
                return;
            }

            var messageBox = screen as MyGuiScreenMessageBox;
            if (messageBox == null)
            {
                return;
            }

            foreach (var control in screen.Controls)
            {
                var button = control as MyGuiControlButton;
                if (button == null || button.Text == null)
                {
                    continue;
                }

                string text = button.Text.ToString();
                if (string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    WriteLog("[JumpSelector] Auto-confirm: pressing YES");
                    _autoConfirmNext = false;
                    button.PressButton();
                    break;
                }
            }
        }

        private static void OnGuiControlCreated(object control)
        {
            if (_guiLogCountdown <= 0 || control == null)
            {
                return;
            }

            _guiLogCountdown--;

            var button = control as MyGuiControlButton;
            if (button == null)
            {
                return;
            }

            string text = button.Text != null ? button.Text.ToString() : string.Empty;
            string name = button.Name ?? string.Empty;
            string type = button.GetType().FullName ?? "Unknown";

            WriteLog($"[JumpSelector] GUI Button: {type} Name='{name}' Text='{text}'");
        }

        private static void WriteLog(string message)
        {
            try
            {
                if (MySandboxGame.Log != null)
                {
                    MySandboxGame.Log.WriteLine(message);
                }
            }
            catch { }

            try
            {
                if (MyLog.Default != null)
                {
                    MyLog.Default.WriteLine(message);
                }
            }
            catch { }
        }

        private static void UpdateKey(List<string> lines, ref int index, string key, string value)
        {
            string line = key + "=" + value;
            if (index >= 0 && index < lines.Count)
            {
                lines[index] = line;
            }
            else
            {
                lines.Add(line);
                index = lines.Count - 1;
            }
        }

        private static void UpdateQueue(List<string> lines, int start, int end, List<string> queue)
        {
            if (start >= 0 && end > start)
            {
                lines.RemoveRange(start, end - start);
            }
            if (queue == null || queue.Count == 0)
            {
                return;
            }
            List<string> block = new List<string>();
            block.Add(JsQueueKey + "=");
            block.AddRange(queue);
            if (start < 0 || start > lines.Count)
            {
                lines.AddRange(block);
            }
            else
            {
                lines.InsertRange(start, block);
            }
        }

        public static IEnumerable<CodeInstruction> JumpSelectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int index = instructions.Count() - 1;
            for (int i = 0; i < index; i++)
            {
                yield return instructions.ElementAt(i);
            }
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(JumpSelectorPatch), "JumpSelectAction", null, null));
            yield return new CodeInstruction(OpCodes.Ret, null);
            yield break;
        }

        public static bool JumpEffectPatch()
        {
            return false;
        }

        public static IEnumerable<CodeInstruction> PerformJumpTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            for (int i = 0; i < code.Count(); i++)
            {
                if (code[i].Calls(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:IsLocalCharacterAffectedByJump")))
                {
                    code[i - 1] = new CodeInstruction(OpCodes.Pop, null);
                    code[i] = new CodeInstruction(OpCodes.Ldc_I4_0, null);
                    break;
                }
            }

            return code;
        }
    }
}
