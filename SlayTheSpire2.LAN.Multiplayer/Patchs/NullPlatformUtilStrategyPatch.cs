using HarmonyLib;
using MegaCrit.Sts2.Core.Platform.Null;
using SlayTheSpire2.LAN.Multiplayer.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Patchs
{
    [HarmonyPatch(typeof(NullPlatformUtilStrategy), "GetPlayerName")]
    internal class NullPlatformUtilStrategyGetPlayerNamePatch
    {
        private static bool Prefix(ulong playerId, List<NullMultiplayerName>? ____mpNames, ref string __result)
        {
            if (____mpNames != null)
            {
                foreach (var mpName in ____mpNames)
                {
                    if (mpName.netId == playerId)
                    {
                        __result = mpName.name;
                        return false;
                    }
                }
            }

            if (LanPlayerNameService.Instance.PlayerNames.TryGetValue(playerId, out var playerName))
            {
                __result = playerName;
                return false;
            }

            __result = playerId switch
            {
                1uL => "测试主机",
                1000uL => "测试客户端 1",
                2000uL => "测试客户端 2",
                3000uL => "测试客户端 3",
                _ => playerId.ToString(),
            };

            return false;
        }
    }
}
