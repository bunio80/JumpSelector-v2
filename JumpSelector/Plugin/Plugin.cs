using HarmonyLib;
using System;
using Sandbox;
using VRage.Plugins;

namespace JumpSelector.Plugin
{
	public class Plugin : IPlugin, IDisposable
	{
		public void Dispose()
		{
		}

		public void Init(object gameInstance)
        {
			Harmony harmony = new Harmony("JumpSelector");
			harmony.Patch(AccessTools.Method("Sandbox.Game.Entities.MyJumpDrive:CreateTerminalControls", null, null), null, null, new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:JumpSelectTranspiler", null, null)), null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:UpdateJumpEffect", null, null), new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:JumpEffectPatch", null, null)), null, null, null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:PerformJump", null, null), null, null, new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:PerformJumpTranspiler", null, null)), null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:CleanupAfterJump", null, null), null, null, new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:PerformJumpTranspiler", null, null)), null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:GetConfirmationText", null, null), new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:JumpConfirmationPrefix", null, null)), null, null, null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:RequestJump", new Type[] { typeof(VRageMath.Vector3D), typeof(long), typeof(float), typeof(long?) }, null), new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:RequestJumpPrefix", new Type[] { typeof(Sandbox.Game.GameSystems.MyGridJumpDriveSystem), typeof(VRageMath.Vector3D), typeof(long), typeof(float), typeof(long?) }, null)), null, null, null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.GameSystems.MyGridJumpDriveSystem:RequestJump", new Type[] { typeof(string), typeof(VRageMath.Vector3D), typeof(long), typeof(VRageMath.BoundingBoxD?), typeof(float), typeof(long?) }, null), new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:RequestJumpPrefix", new Type[] { typeof(Sandbox.Game.GameSystems.MyGridJumpDriveSystem), typeof(string), typeof(VRageMath.Vector3D), typeof(long), typeof(VRageMath.BoundingBoxD?), typeof(float), typeof(long?) }, null)), null, null, null);
			harmony.Patch(AccessTools.Method("Sandbox.Graphics.GUI.MyGuiSandbox:AddScreen", new Type[] { typeof(Sandbox.Graphics.GUI.MyGuiScreenBase) }, null), null, new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:AddScreenPostfix", new Type[] { typeof(Sandbox.Graphics.GUI.MyGuiScreenBase) }, null)), null, null);
			harmony.Patch(AccessTools.Method("Sandbox.Game.Entities.MyJumpDrive:UpdateAfterSimulation100", null, null), null, new HarmonyMethod(AccessTools.Method("JumpSelector.Plugin.JumpSelectorPatch:JumpDriveUpdatePostfix", null, null)), null, null);
			MySandboxGame.Log.WriteLine("Jump Selector Plugin Loaded.");
		}

		public void Update()
		{
		}
	}
}
