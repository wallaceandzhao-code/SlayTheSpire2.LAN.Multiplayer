using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SlayTheSpire2.LAN.Multiplayer.Components;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Patchs.Screens
{
    [HarmonyPatch(typeof(NJoinFriendScreen), "_Ready")]
    internal class NJoinFriendScreenReadyPatch
    {
        private static void Prefix(NJoinFriendScreen __instance)
        {
            var lanPanel = new LanJoinFriendPanel
            {
                Name = "LANPanel",
                JoinFriendScreen = __instance
            };

            __instance.AddChild(lanPanel);

            lanPanel.PatchMarginTop = 12;
            lanPanel.PatchMarginBottom = 12;
            lanPanel.PatchMarginLeft = 12;
            lanPanel.PatchMarginRight = 12;

            lanPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
            lanPanel.OffsetLeft = 450;
            lanPanel.OffsetTop = -338;
            lanPanel.OffsetRight = 790;
            lanPanel.OffsetBottom = 338;

            if (__instance.GetNode("Panel") is NinePatchRect panel)
            {
                lanPanel.Texture = panel.Texture;
                lanPanel.SelfModulate = panel.SelfModulate;
            }
        }
    }
}
