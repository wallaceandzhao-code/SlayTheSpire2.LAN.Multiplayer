using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using SlayTheSpire2.LAN.Multiplayer.Models;
using SlayTheSpire2.LAN.Multiplayer.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Patchs
{
    [HarmonyPatch(typeof(RunSaveManager), "SaveRun")]
    internal class RunSaveManagerSaveRunPatch
    {
        private static bool Prefix(RunSaveManager __instance, AbstractRoom? preFinishedRoom, bool ____forceSynchronous,
            ISaveStore ____saveStore, Action? ___Saved, ref Task __result)
        {
            var netService = RunManager.Instance.NetService;
            if (netService == null)
            {
                return true;
            }

            // Only override save behavior for LAN host saves. Let vanilla handle every other path to avoid
            // impacting core progression, unlocks, and settlement flows.
            if (netService.Type != NetGameType.Host || netService.Platform != PlatformType.None)
            {
                return true;
            }

            __result = TaskHelper.RunSafely(SaveRun(preFinishedRoom, ____forceSynchronous, ____saveStore, ___Saved));

            return false;
        }

        private static async Task SaveRun(AbstractRoom? preFinishedRoom, bool forceSynchronous, ISaveStore saveStore,
            Action? saved)
        {
            if (!RunManager.Instance.ShouldSave)
            {
                // Mirror vanilla "no active run" behavior for LAN custom save files.
                LanRunSaveManagerService.Instance.DeleteCurrentMultiplayerRun();
                saved?.Invoke();
                return;
            }

            var value = RunManager.Instance.ToSave(preFinishedRoom);

            var savePath = LanRunSaveManagerService.Instance.CurrentMultiplayerRunSavePath;
            using var stream = new MemoryStream();
            if (!forceSynchronous)
            {
                await JsonSerializer.SerializeAsync(stream, value,
                    JsonSerializationUtility.GetTypeInfo<SerializableRun>(), CancellationToken.None);
            }
            else
            {
                await JsonSerializer.SerializeAsync(stream, value,
                    JsonSerializationUtility.GetTypeInfo<SerializableRun>());
            }

            stream.Seek(0L, SeekOrigin.Begin);
            await saveStore.WriteFileAsync(savePath, stream.ToArray());

            var lanPlayerNameService = LanPlayerNameService.Instance;

            using var playerNamesStream = new MemoryStream();
            if (!forceSynchronous)
            {
                await JsonSerializer.SerializeAsync(playerNamesStream, lanPlayerNameService.PlayerNames,
                    PlayerNamesContext.Default.PlayerNames, CancellationToken.None);
            }
            else
            {
                await JsonSerializer.SerializeAsync(playerNamesStream, lanPlayerNameService.PlayerNames,
                    PlayerNamesContext.Default.PlayerNames);
            }

            playerNamesStream.Seek(0L, SeekOrigin.Begin);
            await saveStore.WriteFileAsync(LanRunSaveManagerService.Instance.CurrentMultiplayerRunPlayerNamesPath,
                playerNamesStream.ToArray());

            saved?.Invoke();
        }
    }
}
