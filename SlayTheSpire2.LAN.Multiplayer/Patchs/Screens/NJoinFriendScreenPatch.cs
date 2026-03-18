using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SlayTheSpire2.LAN.Multiplayer.Components;
using SlayTheSpire2.LAN.Multiplayer.Helpers;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Patchs.Screens
{
    [HarmonyPatch(typeof(NJoinFriendScreen), "_Ready")]
    internal class NJoinFriendScreenReadyPatch
    {
        private static void Postfix(NJoinFriendScreen __instance)
        {
            try
            {
                if (__instance.GetNodeOrNull<LanJoinFriendPanel>("LANPanel") != null)
                    return;

                var lanPanel = new LanJoinFriendPanel
                {
                    Name = "LANPanel",
                    JoinFriendScreen = __instance
                };

                __instance.AddChild(lanPanel);
                RuntimeTrace.Write("[LAN] Calling EnsureInitialized immediately after AddChild.");
                lanPanel.EnsureInitialized();

                lanPanel.PatchMarginTop = 12;
                lanPanel.PatchMarginBottom = 12;
                lanPanel.PatchMarginLeft = 12;
                lanPanel.PatchMarginRight = 12;
                lanPanel.ClipContents = true;

                var viewportSize = __instance.GetViewportRect().Size;
                const float panelWidth = 420f;
                lanPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                lanPanel.OffsetLeft = Mathf.Max(20f, viewportSize.X - panelWidth - 40f);
                lanPanel.OffsetTop = 110f;
                lanPanel.OffsetRight = lanPanel.OffsetLeft + panelWidth;
                lanPanel.OffsetBottom = viewportSize.Y - 90f;
                lanPanel.ZIndex = 100;

                if (__instance.GetNodeOrNull("Panel") is NinePatchRect panel)
                {
                    lanPanel.Texture = panel.Texture;
                    lanPanel.SelfModulate = panel.SelfModulate;
                }

                Log.Info("[LAN] Injected JoinFriend LAN panel");
                RuntimeTrace.Write("[LAN] Injected JoinFriend LAN panel.");
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write($"[LAN] Inject JoinFriend panel failed: {ex}");
            }
        }
    }
}
