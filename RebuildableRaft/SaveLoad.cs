using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Netcode;
using UltimateWater;
using UnityEngine;

[HarmonyPatch(typeof(RGD_Block), "SerializeFast")]
public static class RGD_Block_SerializeFast_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RGD_Block __instance,
        FastBufferWriter writer)
    {
        SecondaryRaftSaveData data;

        bool hasData =
            MultiRaftRegistry.TryGetRgdData(
                __instance,
                out data
            );

        if (!writer.TryBeginWrite(
                hasData ? 21 : 1
            ))
        {
            throw new OverflowException(
                "Not enough space in the buffer"
            );
        }

        writer.WriteValue<bool>(
            hasData,
            default(FastBufferWriter.ForPrimitives)
        );

        if (!hasData)
        {
            return;
        }

        writer.WriteValue<int>(
            data.raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            data.x,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            data.y,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            data.z,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            data.yaw,
            default(FastBufferWriter.ForPrimitives)
        );
    }
}

[HarmonyPatch(typeof(RGD_Block), "DeserializeFast")]
public static class RGD_Block_DeserializeFast_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RGD_Block __instance,
        FastBufferReader reader)
    {
        bool hasData = false;

        reader.ReadValue<bool>(
            out hasData,
            default(FastBufferWriter.ForPrimitives)
        );

        if (!hasData)
        {
            return;
        }

        SecondaryRaftSaveData data =
            new SecondaryRaftSaveData();

        reader.ReadValue<int>(
            out data.raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out data.x,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out data.y,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out data.z,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out data.yaw,
            default(FastBufferWriter.ForPrimitives)
        );

        if (data.raftId > 0)
        {
            MultiRaftRegistry.SetRgdData(
                __instance,
                data
            );
        }
    }
}

[HarmonyPatch(
    typeof(RGD_Block),
    MethodType.Constructor,
    new Type[]
    {
        typeof(RGDType),
        typeof(Block)
    }
)]
public static class RGD_Block_Save_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RGD_Block __instance,
        Block block)
    {
        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromBlock(
                    block
                );

        if (root == null)
        {
            return;
        }

        MultiRaftRegistry.SetRgdData(
            __instance,
            MultiRaftRegistry
                .CreateSaveData(
                    root
                )
        );
    }
}

[HarmonyPatch(
    typeof(RGD_Block),
    MethodType.Constructor,
    new Type[]
    {
        typeof(SerializationInfo),
        typeof(StreamingContext)
    }
)]
public static class RGD_Block_Load_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RGD_Block __instance,
        SerializationInfo info)
    {
        if (__instance == null ||
            info == null)
        {
            return;
        }

        try
        {
            SecondaryRaftSaveData data =
                new SecondaryRaftSaveData();

            data.raftId =
                info.GetInt32(
                    MultiRaftRegistry.SaveKey +
                    ".RaftId"
                );

            data.x =
                info.GetSingle(
                    MultiRaftRegistry.SaveKey +
                    ".X"
                );

            data.y =
                info.GetSingle(
                    MultiRaftRegistry.SaveKey +
                    ".Y"
                );

            data.z =
                info.GetSingle(
                    MultiRaftRegistry.SaveKey +
                    ".Z"
                );

            data.yaw =
                info.GetSingle(
                    MultiRaftRegistry.SaveKey +
                    ".Yaw"
                );

            if (data.raftId > 0)
            {
                MultiRaftRegistry.SetRgdData(
                    __instance,
                    data
                );
            }
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(RGD_Block), "GetObjectData")]
public static class RGD_Block_GetObjectData_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        RGD_Block __instance,
        SerializationInfo info)
    {
        SecondaryRaftSaveData data;

        if (!MultiRaftRegistry
                .TryGetRgdData(
                    __instance,
                    out data
                ))
        {
            return;
        }

        try
        {
            info.AddValue(
                MultiRaftRegistry.SaveKey +
                ".RaftId",
                data.raftId
            );

            info.AddValue(
                MultiRaftRegistry.SaveKey +
                ".X",
                data.x
            );

            info.AddValue(
                MultiRaftRegistry.SaveKey +
                ".Y",
                data.y
            );

            info.AddValue(
                MultiRaftRegistry.SaveKey +
                ".Z",
                data.z
            );

            info.AddValue(
                MultiRaftRegistry.SaveKey +
                ".Yaw",
                data.yaw
            );
        }
        catch
        {
        }
    }
}

[HarmonyPatch(typeof(SaveAndLoad), "RestoreBlock")]
public static class SaveAndLoad_RestoreBlock_RebuildableRaft
{
    public class RestoreState
    {
        public Transform previousPivot;
        public SecondaryRaftRoot root;
    }

    [HarmonyPrefix]
    public static void Prefix(
        RGD_Block rgdBlock,
        out RestoreState __state)
    {
        __state =
            null;

        SecondaryRaftSaveData data;

        if (!MultiRaftRegistry
                .TryGetRgdData(
                    rgdBlock,
                    out data
                ))
        {
            return;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry.GetRoot(
                data.raftId
            );

        if (root == null)
        {
            root =
                MultiRaftRegistry.CreateRoot(
                    data.raftId,
                    new Vector3(
                        data.x,
                        data.y,
                        data.z
                    ),
                    data.yaw
                );
        }

        if (root == null)
        {
            return;
        }

        RestoreState state =
            new RestoreState();

        state.previousPivot =
            MultiRaftRegistry
                .CurrentBuildPivot;

        state.root =
            root;

        MultiRaftRegistry.SetBuildPivot(
            root.BuildPivot
        );

        __state =
            state;
    }

    [HarmonyPostfix]
    public static void Postfix(
        Block __result,
        RestoreState __state)
    {
        if (__state == null)
        {
            return;
        }

        if (__result != null &&
            __state.root != null)
        {
            MultiRaftRegistry.TagBlock(
                __result,
                __state.root
            );
        }

        MultiRaftRegistry.SetBuildPivot(
            __state.previousPivot != null
                ? __state.previousPivot
                : MultiRaftRegistry.MainPivot
        );
    }
}


internal static class PlayerRaftPersistence
{
    private const int FileMagic = 0x52525031;
    private const int FileVersion = 1;
    private const float RestoreTimeout = 60f;

    private sealed class SavedPlayerRaft
    {
        public int raftId;
        public Vector3 localPosition;
        public float localYaw;
    }

    private static readonly FieldInfo PlayerLockedPivotField =
        AccessTools.Field(typeof(PersonController), "lockedPivot");

    private static bool worldLoaded;
    private static bool restoreRequested;
    private static bool savedStateRead;
    private static float restoreStartedAt;
    private static SavedPlayerRaft savedState;

    internal static void Reset()
    {
        worldLoaded = false;
        restoreRequested = false;
        savedStateRead = false;
        restoreStartedAt = 0f;
        savedState = null;
    }

    internal static void OnWorldLoaded()
    {
        worldLoaded = true;
    }

    internal static void OnUserRestored()
    {
        restoreRequested = true;
        savedStateRead = false;
        savedState = null;
        restoreStartedAt = Time.realtimeSinceStartup;
    }

    internal static void Update()
    {
        if (!restoreRequested || !worldLoaded || SaveAndLoad.IsGameLoading)
            return;

        Network_Player player = RAPI.GetLocalPlayer();
        if (player == null || !player.IsLocalPlayer || player.PersonController == null)
            return;

        if (!savedStateRead)
        {
            string path = GetSavePath(player);
            if (path == null)
                return;

            savedState = ReadState(path);
            savedStateRead = true;

            if (savedState == null)
            {
                restoreRequested = false;
                return;
            }
        }

        SecondaryRaftRoot root = MultiRaftRegistry.GetRoot(savedState.raftId);
        if (root != null && root.HasPlacedBlocks && root.IsNetworkPoseReady)
        {
            Transform pivot = root.BuildPivot;
            Vector3 target = pivot.TransformPoint(savedState.localPosition);
            Vector3 feet = target + Vector3.down * 0.8f;

            if (root.GetGroundPointScore(feet) == float.MaxValue &&
                !TryFindLandingPosition(root, out target))
            {
                if (Time.realtimeSinceStartup - restoreStartedAt > RestoreTimeout)
                {
                    restoreRequested = false;

                }
                return;
            }

            float yaw = root.transform.eulerAngles.y + savedState.localYaw;
            TeleportPlayer(player, target, yaw, root);
            restoreRequested = false;

            return;
        }

        if (Time.realtimeSinceStartup - restoreStartedAt > RestoreTimeout)
        {

            restoreRequested = false;
        }
    }

    internal static void SaveLocalPlayer()
    {
        if (!RAPI.IsCurrentSceneGame() || SaveAndLoad.WorldGuid == Guid.Empty)
            return;

        Network_Player player = RAPI.GetLocalPlayer();
        if (player == null || !player.IsLocalPlayer || player.PersonController == null)
            return;

        string path = GetSavePath(player);
        if (path == null)
            return;

        SecondaryRaftRoot root = MultiRaftRegistry.GetRootFromTransform(player.transform);
        if (root == null)
            root = MultiRaftRegistry.FindRootAtGroundPoint(player.FeetPosition);

        if (root == null || !root.HasPlacedBlocks)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {

            }
            return;
        }

        SavedPlayerRaft state = new SavedPlayerRaft();
        state.raftId = root.RaftId;
        state.localPosition = root.BuildPivot.InverseTransformPoint(player.transform.position);
        state.localYaw = Mathf.DeltaAngle(root.transform.eulerAngles.y, player.transform.eulerAngles.y);

        if (!IsValidPosition(state.localPosition))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + ".tmp";
            using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(FileMagic);
                writer.Write(FileVersion);
                writer.Write(state.raftId);
                writer.Write(state.localPosition.x);
                writer.Write(state.localPosition.y);
                writer.Write(state.localPosition.z);
                writer.Write(state.localYaw);
            }

            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporaryPath, path);

        }
        catch (Exception)
        {

        }
    }

    private static SavedPlayerRaft ReadState(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != FileMagic || reader.ReadInt32() != FileVersion)
                    return null;

                SavedPlayerRaft state = new SavedPlayerRaft();
                state.raftId = reader.ReadInt32();
                state.localPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                state.localYaw = reader.ReadSingle();

                if (state.raftId <= 0 || !IsValidPosition(state.localPosition) ||
                    float.IsNaN(state.localYaw) || float.IsInfinity(state.localYaw))
                    return null;

                return state;
            }
        }
        catch (Exception)
        {

            return null;
        }
    }

    private static string GetSavePath(Network_Player player)
    {
        if (player == null || SaveAndLoad.WorldGuid == Guid.Empty)
            return null;

        string playerId = player.steamID.ToString();
        if (string.IsNullOrEmpty(playerId))
            return null;

        char[] safeId = playerId.ToCharArray();
        for (int i = 0; i < safeId.Length; i++)
        {
            char c = safeId[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                safeId[i] = '_';
        }

        return Path.Combine(
            Path.Combine(SaveAndLoad.GetSettingsPath(), "RebuildableRaft"),
            SaveAndLoad.WorldGuid.ToString("N") + "_" + new string(safeId) + ".dat");
    }

    private static bool IsValidPosition(Vector3 position)
    {
        return IsValidFloat(position.x) && IsValidFloat(position.y) && IsValidFloat(position.z) &&
               Mathf.Abs(position.x) < 10000f && Mathf.Abs(position.y) < 10000f &&
               Mathf.Abs(position.z) < 10000f;
    }

    private static bool IsValidFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal static string TeleportToRaft(string[] args)
    {
        Network_Player player = RAPI.GetLocalPlayer();
        if (player == null || player.IsKilled || player.PersonController == null ||
            !RAPI.IsCurrentSceneGame() || SaveAndLoad.IsGameLoading)
        {
            return "Teleportation is not available now";
        }

        int raftNumber = 1;
        if (args != null && args.Length > 0 &&
            (args.Length != 1 || !int.TryParse(args[0], out raftNumber) || raftNumber < 1))
        {
            return "Usage: rafttp [number]";
        }

        // Нумеруются только плоты, на которых остались строительные блоки
        bool hasPrimary = MultiRaftRegistry.HasPrimaryBlocks();
        List<SecondaryRaftRoot> secondaryRafts = new List<SecondaryRaftRoot>();
        foreach (SecondaryRaftRoot candidate in MultiRaftRegistry.GetRoots())
        {
            if (candidate != null && candidate.HasPlacedBlocks)
                secondaryRafts.Add(candidate);
        }
        secondaryRafts.Sort(delegate(SecondaryRaftRoot a, SecondaryRaftRoot b)
        {
            return a.RaftId.CompareTo(b.RaftId);
        });

        int raftCount = secondaryRafts.Count + (hasPrimary ? 1 : 0);
        if (raftNumber > raftCount)
        {
            return "Raft " + raftNumber + " not found (available: " + raftCount + ")";
        }

        Vector3 destination;
        if (hasPrimary && raftNumber == 1)
        {
            if (!TryFindPrimaryLandingPosition(out destination))
                return "Raft 1 has no walkable blocks";

            TeleportPlayer(player, destination, player.transform.eulerAngles.y, null);
            return "Teleported to raft 1 (main)";
        }

        int secondaryIndex = raftNumber - (hasPrimary ? 2 : 1);
        SecondaryRaftRoot root = secondaryRafts[secondaryIndex];
        if (root == null || !root.HasPlacedBlocks || !root.IsNetworkPoseReady)
        {
            return "Raft " + raftNumber + " not loaded yet";
        }

        if (!TryFindLandingPosition(root, out destination))
        {
            return "Raft " + raftNumber + " has no walkable blocks";
        }

        TeleportPlayer(player, destination, player.transform.eulerAngles.y, root);
        return "Teleported to raft " + raftNumber + " (ID " + root.RaftId + ")";
    }

    private static bool TryFindPrimaryLandingPosition(out Vector3 position)
    {
        position = Vector3.zero;
        Transform mainPivot = MultiRaftRegistry.MainPivot;
        List<Block> blocks = BlockCreator.GetPlacedBlocks();
        if (mainPivot == null || blocks == null)
            return false;

        Block choice = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block block = blocks[i];
            if (block == null || !block.hasBeenPlaced || !block.gameObject.activeInHierarchy ||
                !block.IsWalkable() || !block.transform.IsChildOf(mainPivot) ||
                MultiRaftRegistry.GetRootFromBlock(block) != null)
                continue;

            bool isFoundation = block.buildableItem != null &&
                Block.IsBlockIndexFoundation(block.buildableItem.UniqueIndex);
            Vector3 delta = block.transform.position - mainPivot.position;
            float score = (isFoundation ? 0f : 1000000f) +
                delta.x * delta.x + delta.z * delta.z;
            if (score < bestScore)
            {
                bestScore = score;
                choice = block;
            }
        }

        if (choice == null)
            return false;

        position = choice.transform.position + choice.transform.up * 1.2f;
        return true;
    }

    private static bool TryFindLandingPosition(SecondaryRaftRoot root, out Vector3 position)
    {
        position = Vector3.zero;
        if (root == null)
            return false;

        Block[] blocks = root.GetComponentsInChildren<Block>(true);
        Block choice = null;
        float bestScore = float.MaxValue;

        foreach (Block block in blocks)
        {
            if (block == null || !block.hasBeenPlaced || !block.gameObject.activeInHierarchy ||
                !block.IsWalkable())
                continue;

            bool isFoundation = block.buildableItem != null &&
                Block.IsBlockIndexFoundation(block.buildableItem.UniqueIndex);

            Vector3 delta = block.transform.position - root.transform.position;
            float score = (isFoundation ? 0f : 1000000f) +
                delta.x * delta.x + delta.z * delta.z;
            if (score < bestScore)
            {
                bestScore = score;
                choice = block;
            }
        }

        if (choice == null)
            return false;

        position = choice.transform.position + choice.transform.up * 1.2f;
        return root.GetGroundPointScore(position + Vector3.down * 0.8f) < float.MaxValue;
    }

    private static void TeleportPlayer(
        Network_Player player,
        Vector3 position,
        float yaw,
        SecondaryRaftRoot targetRoot)
    {
        PersonController person = player.PersonController;
        person.CameraSubmersionChanged(SubmersionState.None, true);
        person.SwitchControllerType(ControllerType.Ground);

        CharacterController controller = person.controller;
        bool controllerEnabled = controller != null && controller.enabled;
        if (controllerEnabled)
            controller.enabled = false;

        Transform pivot = targetRoot != null
            ? targetRoot.BuildPivot
            : MultiRaftRegistry.MainPivot;
        player.transform.SetParent(null, true);
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        if (pivot != null)
        {
            if (PlayerLockedPivotField != null)
                PlayerLockedPivotField.SetValue(person, pivot);
            player.transform.SetParent(pivot, true);
        }

        Physics.SyncTransforms();
        if (controllerEnabled)
            controller.enabled = true;
    }
}

[HarmonyPatch(typeof(SaveAndLoad), "SaveUser")]
internal static class SaveAndLoad_SaveUser_PlayerRaft
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PlayerRaftPersistence.SaveLocalPlayer();
    }
}

[HarmonyPatch(typeof(SaveAndLoad), "RestoreUser")]
internal static class SaveAndLoad_RestoreUser_PlayerRaft
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PlayerRaftPersistence.OnUserRestored();
    }
}
