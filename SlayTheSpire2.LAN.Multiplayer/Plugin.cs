using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SlayTheSpire2.LAN.Multiplayer.Helpers;

// ReSharper disable UnusedMember.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace SlayTheSpire2.LAN.Multiplayer
{
    [ModInitializer("Initialize")]
    public class Plugin
    {
        private static void Initialize()
        {
            var harmony = new Harmony("SlayTheSpire2.LAN.Multiplayer");
            RuntimeTrace.Write("Initialize started.");

            var patchedCount = 0;
            var failedCount = 0;

            foreach (var patchType in typeof(Plugin).Assembly.GetTypes())
            {
                if (patchType.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0)
                    continue;

                try
                {
                    harmony.CreateClassProcessor(patchType).Patch();
                    patchedCount++;
                    RuntimeTrace.Write($"Patched: {patchType.FullName}");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    RuntimeTrace.Write($"Patch failed: {patchType.FullName} | {ex}");
                }
            }

            RuntimeTrace.Write($"Initialize completed. Patched={patchedCount}, Failed={failedCount}");
        }
    }
}
