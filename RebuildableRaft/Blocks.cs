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

[HarmonyPatch(typeof(BlockCreator), "RemoveBlock")]
public static class BlockCreator_RemoveBlock_RebuildableRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        Block block,
        ref bool updateRaftBounds)
    {
        if (MultiRaftRegistry
                .IsSecondaryBlock(
                    block
                ))
        {
            updateRaftBounds =
                false;
        }
    }
}

[HarmonyPatch(typeof(BlockCreator), "RemoveBlockNetwork")]
public static class BlockCreator_RemoveBlockNetwork_RebuildableRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        Block block,
        ref bool updateRaftBounds)
    {
        if (MultiRaftRegistry
                .IsSecondaryBlock(
                    block
                ))
        {
            updateRaftBounds =
                false;
        }
    }
}

[HarmonyPatch(typeof(BlockCreator), "RemoveUnstableBlockNetworked")]
public static class BlockCreator_RemoveUnstableBlock_RebuildableRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        Block block,
        ref bool updateRaftBounds)
    {
        if (MultiRaftRegistry
                .IsSecondaryBlock(
                    block
                ))
        {
            updateRaftBounds =
                false;
        }
    }
}

[HarmonyPatch(typeof(BlockCollisionConsolidator), "AddBlock")]
public static class BlockCollisionConsolidator_AddBlock_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Block block)
    {
        if (block == null ||
            !MultiRaftRegistry
                .IsSecondaryBlock(
                    block
                ))
        {
            return true;
        }

        BoxCollider[] colliders =
            block.blockColliders;

        if (colliders != null)
        {
            for (int i = 0;
                 i < colliders.Length;
                 i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = true;
                }
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(BlockCollisionConsolidator), "SetBlockActive")]
public static class BlockCollisionConsolidator_SetBlockActive_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Block block)
    {
        if (block == null ||
            !MultiRaftRegistry
                .IsSecondaryBlock(
                    block
                ))
        {
            return true;
        }

        BoxCollider[] colliders =
            block.blockColliders;

        if (colliders != null)
        {
            for (int i = 0;
                 i < colliders.Length;
                 i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = true;
                }
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(RaftBounds), "AddWalkableBlock")]
public static class RaftBounds_AddWalkableBlock_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Block block)
    {
        return !MultiRaftRegistry
            .IsSecondaryBlock(
                block
            );
    }
}
