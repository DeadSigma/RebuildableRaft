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

[HarmonyPatch(typeof(RaftVelocityManager), "AddMotor")]
public static class RaftVelocityManager_AddMotor_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        MotorWheel motor)
    {
        return motor == null ||
            MultiRaftRegistry
                .GetRootFromTransform(
                    motor.transform
                ) == null;
    }
}

[HarmonyPatch(typeof(RaftVelocityManager), "AddSteeringWheel")]
public static class RaftVelocityManager_AddSteeringWheel_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        SteeringWheel steeringWheel)
    {
        return steeringWheel == null ||
            MultiRaftRegistry
                .GetRootFromTransform(
                    steeringWheel.transform
                ) == null;
    }
}

[HarmonyPatch(typeof(Sail), "OnBlockPlaced")]
public static class Sail_OnBlockPlaced_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        Sail __instance)
    {
        if (__instance == null ||
            MultiRaftRegistry
                .GetRootFromTransform(
                    __instance.transform
                ) == null)
        {
            return;
        }

        if (Sail.AllSails.Contains(
                __instance
            ))
        {
            Sail.AllSails.Remove(
                __instance
            );
        }
    }
}
