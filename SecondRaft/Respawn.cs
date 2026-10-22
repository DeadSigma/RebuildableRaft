using HarmonyLib;
using HMLLibrary;
using System;
using UnityEngine;

internal static class PlayerRaftRespawn
{
    private const int NoRaft = int.MinValue;
    private const float TrackingInterval = 0.15f;
    private const float MissingRaftGracePeriod = 5f;
    private const float RespawnTimeout = 15f;

    private static int lastVisitedRaftId = NoRaft;
    private static float nextTrackingTime;
    private static bool waitingForRaft;
    private static float respawnCompletedAt;
    private static float trackingSuppressedUntil;

    private static Player blockedPlayer;
    private static PersonController blockedPerson;
    private static bool previousPlayerEnabled;
    private static bool previousPersonEnabled;
    private static bool inputBlocked;

    internal static void Reset()
    {
        RestoreInput();
        lastVisitedRaftId = NoRaft;
        nextTrackingTime = 0f;
        waitingForRaft = false;
        respawnCompletedAt = 0f;
        trackingSuppressedUntil = 0f;
    }

    internal static void Update()
    {
        if (SaveAndLoad.IsGameLoading)
            return;

        Network_Player player = RAPI.GetLocalPlayer();
        if (player == null || !player.IsLocalPlayer || player.PersonController == null ||
            player.PlayerScript == null)
            return;

        if (waitingForRaft)
        {
            float elapsed = Time.realtimeSinceStartup - respawnCompletedAt;

            if (!player.IsKilled && !player.PlayerScript.IsDead &&
                PlayerRaftPersistence.TryRespawnOnRaft(
                    lastVisitedRaftId, elapsed >= MissingRaftGracePeriod))
            {
                CompleteRespawn();
                return;
            }

            if (elapsed >= RespawnTimeout)
                CompleteRespawn();

            return;
        }

        if (player.IsKilled || player.PlayerScript.IsDead ||
            Time.realtimeSinceStartup < nextTrackingTime ||
            Time.realtimeSinceStartup < trackingSuppressedUntil)
            return;

        nextTrackingTime = Time.realtimeSinceStartup + TrackingInterval;
        CaptureRaft(player);
    }

    internal static void CaptureBeforeDeath(Player playerScript)
    {
        Network_Player player = playerScript != null
            ? playerScript.GetComponent<Network_Player>() : null;
        if (player == null || !player.IsLocalPlayer || waitingForRaft)
            return;

        CaptureRaft(player);
        trackingSuppressedUntil = Time.realtimeSinceStartup + 1f;
    }

    internal static void OnRespawnComplete(Player playerScript)
    {
        Network_Player player = playerScript != null
            ? playerScript.GetComponent<Network_Player>() : null;
        if (player == null || !player.IsLocalPlayer)
            return;

        if (waitingForRaft)
        {
            waitingForRaft = false;
            RestoreInput();
        }

        if (lastVisitedRaftId == NoRaft || SaveAndLoad.IsGameLoading)
            return;

        // Позиция восстанавливается до следующего кадра с управлением игроком
        if (PlayerRaftPersistence.TryRespawnOnRaft(lastVisitedRaftId, false, true))
        {
            CompleteRespawn();
            return;
        }

        waitingForRaft = true;
        respawnCompletedAt = Time.realtimeSinceStartup;
        BlockInput(player);
    }

    internal static void NotifyTeleport(int raftId)
    {
        lastVisitedRaftId = raftId;
    }

    private static void CompleteRespawn()
    {
        waitingForRaft = false;
        RestoreInput();
        nextTrackingTime = Time.realtimeSinceStartup + TrackingInterval;
        trackingSuppressedUntil = Time.realtimeSinceStartup + 0.5f;
    }

    private static void BlockInput(Network_Player player)
    {
        if (inputBlocked || player == null || player.PlayerScript == null ||
            player.PersonController == null)
            return;

        blockedPlayer = player.PlayerScript;
        blockedPerson = player.PersonController;
        previousPlayerEnabled = blockedPlayer.enabled;
        previousPersonEnabled = blockedPerson.enabled;
        inputBlocked = true;
        blockedPlayer.enabled = false;
        blockedPerson.enabled = false;
    }

    private static void RestoreInput()
    {
        if (!inputBlocked)
            return;

        if (blockedPerson != null)
            blockedPerson.enabled = previousPersonEnabled;
        if (blockedPlayer != null)
            blockedPlayer.enabled = previousPlayerEnabled;

        blockedPlayer = null;
        blockedPerson = null;
        inputBlocked = false;
    }

    private static void CaptureRaft(Network_Player player)
    {
        PersonController person = player.PersonController;
        if (person == null || person.controllerType != ControllerType.Ground ||
            !person.IsGrounded)
            return;

        SecondaryRaftRoot root = MultiRaftRegistry.FindRootAtGroundPoint(player.FeetPosition);
        if (root != null && root.HasPlacedBlocks)
        {
            lastVisitedRaftId = root.RaftId;
            return;
        }

        Transform mainPivot = MultiRaftRegistry.MainPivot;
        Transform parent = player.transform.parent;
        if (mainPivot != null && parent != null &&
            (parent == mainPivot || parent.IsChildOf(mainPivot)) &&
            MultiRaftRegistry.HasPrimaryBlocks())
        {
            lastVisitedRaftId = 0;
        }
    }
}

[HarmonyPatch(typeof(Player), "Kill", new Type[] { typeof(bool) })]
internal static class Player_Kill_RebuildableRaftRespawn
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance)
    {
        PlayerRaftRespawn.CaptureBeforeDeath(__instance);
    }
}

[HarmonyPatch(typeof(Player), "OnRespawnComplete")]
internal static class Player_OnRespawnComplete_RebuildableRaft
{
    [HarmonyPostfix]
    private static void Postfix(Player __instance)
    {
        PlayerRaftRespawn.OnRespawnComplete(__instance);
    }
}

[HarmonyPatch(typeof(PersonController), "OnPlayerDeath")]
internal static class PersonController_OnPlayerDeath_RebuildableRaft
{
    [HarmonyPrefix]
    private static void Prefix(PersonController __instance)
    {
        if (__instance != null)
            PlayerRaftRespawn.CaptureBeforeDeath(__instance.GetComponent<Player>());
    }
}
