using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

public static class RebuildableRaftDevAutoLoad
{
    private const string AutoLoadArgument = "-rebuildableraft-autoload";
    private const string LegacyDevArgument = "-rebuildableraft-dev";
    private const float ComponentsTimeout = 30f;
    private const float MainMenuDelay = 3f;

    private static bool started;
    private static bool runnerCreated;

    public static bool IsEnabled()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], AutoLoadArgument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], LegacyDevArgument, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void CreateRunner()
    {
        if (runnerCreated || !IsEnabled())
        {
            return;
        }

        runnerCreated = true;

        GameObject gameObject =
            new GameObject("RebuildableRaft_AutoLoad");

        gameObject.hideFlags = HideFlags.HideAndDontSave;

        UnityEngine.Object.DontDestroyOnLoad(
            gameObject
        );

        gameObject.AddComponent<RebuildableRaftAutoLoadRunner>();

        Debug.Log(
            "[RebuildableRaft] Автозагрузка последнего мира включена"
        );
    }

    internal static IEnumerator LoadLatestWorld()
    {
        if (started)
        {
            yield break;
        }

        started = true;

        float timeoutAt =
            Time.realtimeSinceStartup + ComponentsTimeout;

        while (ComponentManager<SaveAndLoad>.Value == null ||
               ComponentManager<Raft_Network>.Value == null)
        {
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                Debug.LogError(
                    "[RebuildableRaft] Компоненты загрузки мира не появились за 30 секунд"
                );

                yield break;
            }

            yield return null;
        }

        if (LoadSceneManager.IsGameSceneLoaded)
        {
            yield break;
        }

        yield return new WaitForSecondsRealtime(
            MainMenuDelay
        );

        if (LoadSceneManager.IsGameSceneLoaded)
        {
            yield break;
        }

        SaveAndLoad saveAndLoad =
            ComponentManager<SaveAndLoad>.Value;

        Raft_Network network =
            ComponentManager<Raft_Network>.Value;

        if (saveAndLoad == null ||
            network == null)
        {
            Debug.LogError(
                "[RebuildableRaft] Компоненты загрузки мира исчезли до запуска"
            );

            yield break;
        }

        Debug.Log(
            "[RebuildableRaft] Компоненты загрузки мира найдены"
        );

        saveAndLoad.CreateNecessaryLoadDirectories();
        saveAndLoad.ConvertAllOldSavesToNewFormat();

        DirectoryInfo worldRoot =
            new DirectoryInfo(
                SaveAndLoad.WorldPath
            );

        if (!worldRoot.Exists)
        {
            Debug.LogError(
                "[RebuildableRaft] Папка сохранений не найдена"
            );

            yield break;
        }

        List<GameToFolderConnection> worlds =
            new List<GameToFolderConnection>();

        foreach (DirectoryInfo directory
                 in worldRoot.GetDirectories())
        {
            if (directory.Name ==
                SaveAndLoad.BackupFolderName)
            {
                continue;
            }

            DirectoryInfo gameDirectoryInfo = null;

            RGD_Game game =
                SaveAndLoad.GetLatestRGDGameFromBackupFolders(
                    directory,
                    out gameDirectoryInfo
                );

            if (game == null)
            {
                continue;
            }

            worlds.Add(
                new GameToFolderConnection
                {
                    rgdGame = game,
                    directoryInfo = directory,
                    gameDirectoryInfo =
                        gameDirectoryInfo
                }
            );
        }

        GameToFolderConnection latest =
            worlds
                .OrderByDescending(
                    GetLastSaveTimeUtc
                )
                .FirstOrDefault();

        if (latest == null ||
            latest.rgdGame == null)
        {
            Debug.LogError(
                "[RebuildableRaft] Не найдено сохранений для автозагрузки"
            );

            yield break;
        }

        SaveAndLoad.WorldToLoad =
            latest.rgdGame;

        GameModeValueManager.SelectCurrentGameMode(
            SaveAndLoad.WorldToLoad.mode
        );

        GameManager.IsInNewGame = false;
        GameManager.FriendlyFire = false;

        Debug.Log(
            "[RebuildableRaft] Запускается последнее изменённое сохранение"
        );

        network.HostGame(
            RequestJoinAuthSetting.ALLOW_NONE,
            string.Empty
        );
    }

    private static DateTime GetLastSaveTimeUtc(
        GameToFolderConnection world)
    {
        if (world == null)
        {
            return DateTime.MinValue;
        }

        if (world.gameDirectoryInfo != null)
        {
            return
                world.gameDirectoryInfo.LastWriteTimeUtc;
        }

        if (world.directoryInfo != null)
        {
            return
                world.directoryInfo.LastWriteTimeUtc;
        }

        return DateTime.MinValue;
    }
}

internal sealed class RebuildableRaftAutoLoadRunner
    : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return
            RebuildableRaftDevAutoLoad.LoadLatestWorld();
    }
}

[HarmonyPatch(typeof(BlockCreator), "Update")]
internal static class RebuildableRaftAutoLoadBootstrap
{
    [HarmonyPrepare]
    private static bool Prepare()
    {
        RebuildableRaftDevAutoLoad.CreateRunner();

        return false;
    }
}
