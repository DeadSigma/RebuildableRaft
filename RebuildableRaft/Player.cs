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

[HarmonyPatch(typeof(PersonController), "SetNetworkProperties")]
public static class PersonController_SetNetworkProperties_RebuildableRaft
{
    private static readonly FieldInfo LockedPivotField =
        AccessTools.Field(
            typeof(PersonController),
            "lockedPivot"
        );

    [HarmonyPrefix]
    public static void Prefix(
        PersonController __instance,
        Message_Player_Update msg)
    {
        if (__instance == null ||
            msg == null ||
            LockedPivotField == null)
        {
            return;
        }

        if (msg.RaftAsParent)
        {
            Transform mainPivot =
                MultiRaftRegistry.MainPivot;

            if (mainPivot != null)
            {
                LockedPivotField.SetValue(
                    __instance,
                    mainPivot
                );
            }

            return;
        }

        if (msg.ControllerType !=
            ControllerType.Ground)
        {
            return;
        }

        Network_Player player =
            __instance.GetComponent
                <Network_Player>();

        if (player == null ||
            player.IsLocalPlayer)
        {
            return;
        }

        Vector3 worldPosition =
            msg.Position;

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .FindRootAtGroundPoint(
                    worldPosition +
                    Vector3.down * 0.8f
                );

        if (root == null)
        {
            return;
        }

        Transform pivot =
            root.BuildPivot;

        if (pivot == null)
        {
            return;
        }

        // Сетевой игрок привязывается к движущемуся второму плоту
        LockedPivotField.SetValue(
            __instance,
            pivot
        );

        msg.RaftAsParent =
            true;

        msg.Position =
            pivot.InverseTransformPoint(
                worldPosition
            );
    }
}


[HarmonyPatch(typeof(PersonController), "SetCorrectGroundParent")]
public static class PersonController_SetCorrectGroundParent_RebuildableRaft
{
    private static readonly FieldInfo LockedPivotField =
        AccessTools.Field(
            typeof(PersonController),
            "lockedPivot"
        );

    [HarmonyPrefix]
    public static bool Prefix(
        PersonController __instance,
        RaycastHit groundHit)
    {
        if (__instance == null ||
            LockedPivotField == null)
        {
            return true;
        }

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        SecondaryRaftRoot currentRoot =
            MultiRaftRegistry
                .GetRootFromTransform(
                    __instance.transform.parent
                );

        bool currentRootSupported =
            currentRoot != null &&
            currentRoot.GetGroundPointScore(
                __instance.transform.position +
                Vector3.down * 0.8f
            ) < float.MaxValue;

        bool inWater =
            __instance.controllerType ==
                ControllerType.Water ||
            __instance.SubmersionState !=
                SubmersionState.None;

        if (inWater &&
            currentRootSupported &&
            __instance.controllerType ==
                ControllerType.Ground)
        {
            inWater = false;
        }

        if (inWater)
        {
            if (currentRoot != null)
            {
                __instance.transform.SetParent(
                    null,
                    true
                );
            }

            if (mainPivot != null)
            {
                LockedPivotField.SetValue(
                    __instance,
                    mainPivot
                );
            }

            return false;
        }

        SecondaryRaftRoot root =
            null;

        if (groundHit.collider != null)
        {
            root =
                MultiRaftRegistry
                    .GetRootFromTransform(
                        groundHit.collider
                            .transform
                    );

            if (root == null)
            {
                Block block =
                    groundHit.collider
                        .GetComponentInParent
                            <Block>();

                root =
                    MultiRaftRegistry
                        .GetRootFromBlock(
                            block
                        );
            }
        }

        if (root == null)
        {
            root =
                MultiRaftRegistry
                    .FindRootAtGroundPoint(
                        __instance.transform.position +
                        Vector3.down * 0.8f
                    );
        }

        if (root == null &&
            currentRootSupported)
        {
            root =
                currentRoot;
        }

        if (root != null)
        {
            Transform raftPivot =
                root.BuildPivot;

            if (raftPivot == null)
            {
                return true;
            }

            LockedPivotField.SetValue(
                __instance,
                raftPivot
            );

            if (__instance.transform.parent !=
                raftPivot)
            {
                __instance.transform.SetParent(
                    raftPivot,
                    true
                );
            }

            return false;
        }

        if (currentRoot != null)
        {
            __instance.transform.SetParent(
                null,
                true
            );
        }

        if (mainPivot != null)
        {
            LockedPivotField.SetValue(
                __instance,
                mainPivot
            );
        }

        return true;
    }
}


[HarmonyPatch(typeof(PersonController), "SwitchControllerType")]
public static class PersonController_SwitchControllerType_RebuildableRaft
{
    private static readonly FieldInfo LockedPivotField =
        AccessTools.Field(
            typeof(PersonController),
            "lockedPivot"
        );

    [HarmonyPrefix]
    public static bool Prefix(
        PersonController __instance,
        ControllerType newType)
    {
        if (__instance == null ||
            newType != ControllerType.Water)
        {
            return true;
        }

        Network_Player player =
            __instance.GetComponent<Network_Player>();

        if (player == null ||
            !player.IsLocalPlayer)
        {
            return true;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    __instance.transform.parent
                );

        if (root == null)
        {
            return true;
        }

        float supportScore =
            root.GetGroundPointScore(
                player.FeetPosition
            );

        if (supportScore <
            float.MaxValue)
        {
            return false;
        }

        __instance.transform.SetParent(
            null,
            true
        );

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot != null &&
            LockedPivotField != null)
        {
            LockedPivotField.SetValue(
                __instance,
                mainPivot
            );
        }

        return true;
    }
}
