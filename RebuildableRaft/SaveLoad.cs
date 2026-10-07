using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
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
