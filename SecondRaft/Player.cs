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

[HarmonyPatch(typeof(PersonController), "Update")]
public static class PersonController_Update_RebuildableRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        PersonController __instance)
    {
        if (__instance == null)
        {
            return;
        }

        Network_Player player =
            __instance.GetComponent<Network_Player>();

        if (player == null ||
            !player.IsLocalPlayer)
        {
            return;
        }

        Physics.SyncTransforms();
    }
}


[HarmonyPatch(typeof(PersonController), "SetNetworkProperties")]
public static class PersonController_SetNetworkProperties_RebuildableRaft
{
    private static readonly FieldInfo LockedPivotField =
        AccessTools.Field(typeof(PersonController), "lockedPivot");

    private static readonly FieldInfo NetworkLocalPivotFlagField =
        AccessTools.Field(typeof(PersonController),
            "networkPositionIsLocalLockedPivot");

    private const float VanillaFeetOffset = 0.8f;
    private const float RemoteFeetVisualLift = 0.11f;

    private sealed class RemoteDeckState
    {
        public int raftId = -1;
        public bool hasLocalAnchor;
        public Vector2 localAnchor;
    }

    private static readonly ConditionalWeakTable<PersonController, RemoteDeckState>
        RemoteDeckStates =
            new ConditionalWeakTable<PersonController, RemoteDeckState>();

    [HarmonyPostfix]
    public static void Postfix(
        PersonController __instance,
        Message_Player_Update msg)
    {
        if (__instance == null || msg == null ||
            LockedPivotField == null ||
            NetworkLocalPivotFlagField == null)
            return;

        Network_Player player =
            __instance.GetComponent<Network_Player>();

        if (player == null || player.IsLocalPlayer)
            return;

        RemoteDeckState state =
            RemoteDeckStates.GetValue(
                __instance, ignored => new RemoteDeckState());

        Transform mainPivot = MultiRaftRegistry.MainPivot;
        Vector3 worldPosition = msg.RaftAsParent && mainPivot != null
            ? mainPivot.TransformPoint(msg.Position)
            : msg.Position;

        SecondaryRaftRoot previousRoot =
            MultiRaftRegistry.GetRootFromTransform(
                __instance.transform.parent);

        SecondaryRaftRoot selectedRoot = null;
        Vector3 selectedDeckPoint = Vector3.zero;
        float bestScore = float.MaxValue;

        if (msg.ControllerType == ControllerType.Ground &&
            (msg.ControllerIsGrounded || previousRoot != null))
        {
            foreach (SecondaryRaftRoot root in
                MultiRaftRegistry.GetRoots())
            {
                if (root == null || !root.IsNetworkPoseReady)
                    continue;

                Vector3 deckPoint;
                float score;
                float expectedFeetY = root == previousRoot
                    ? __instance.transform.position.y - 0.8f
                    : worldPosition.y - 0.8f;

                if (!TryFindDeckAtXZ(root, worldPosition,
                        expectedFeetY, out deckPoint, out score))
                    continue;

                if (root == previousRoot)
                    score -= 0.4f;

                if (score < bestScore)
                {
                    bestScore = score;
                    selectedRoot = root;
                    selectedDeckPoint = deckPoint;
                }
            }
        }

        if (selectedRoot == null)
        {
            state.raftId = -1;
            state.hasLocalAnchor = false;
            if (mainPivot != null)
                LockedPivotField.SetValue(__instance, mainPivot);
            return;
        }

        Transform secondaryPivot = selectedRoot.BuildPivot;
        if (secondaryPivot == null)
            return;

        if (state.raftId != selectedRoot.RaftId)
        {
            state.raftId = selectedRoot.RaftId;
            state.hasLocalAnchor = false;
        }

        Vector3 localPosition =
            secondaryPivot.InverseTransformPoint(worldPosition);

        if (msg.ControllerIsGrounded)
        {
            // Ноги выравниваются в системе координат палубы
            Vector3 localDeck =
                secondaryPivot.InverseTransformPoint(selectedDeckPoint);
            localPosition.y = localDeck.y +
                VanillaFeetOffset + RemoteFeetVisualLift;

            Vector2 receivedXZ =
                new Vector2(localPosition.x, localPosition.z);
            Vector4 animationVelocity = msg.AnimationVelocity;
            bool walking = animationVelocity.x * animationVelocity.x +
                animationVelocity.z * animationVelocity.z > 0.0004f;

            // На месте исключаются мелкие сетевые колебания относительно плота
            if (!state.hasLocalAnchor || walking ||
                (receivedXZ - state.localAnchor).sqrMagnitude > 4f)
            {
                state.localAnchor = receivedXZ;
                state.hasLocalAnchor = true;
            }

            localPosition.x = state.localAnchor.x;
            localPosition.z = state.localAnchor.y;
        }
        else
        {
            state.hasLocalAnchor = false;
        }

        LockedPivotField.SetValue(__instance, secondaryPivot);
        NetworkLocalPivotFlagField.SetValue(__instance, true);
        __instance.SetNetworkPosition(localPosition);

        // Входящее сетевое сообщение сохраняется без изменений
    }

    private static bool TryFindDeckAtXZ(
        SecondaryRaftRoot root,
        Vector3 worldPosition,
        float expectedFeetY,
        out Vector3 deckPoint,
        out float score)
    {
        deckPoint = Vector3.zero;
        score = float.MaxValue;
        if (root == null)
            return false;

        Block[] blocks = root.GetComponentsInChildren<Block>(true);
        bool found = false;

        for (int i = 0; i < blocks.Length; i++)
        {
            Block block = blocks[i];
            if (block == null || !block.hasBeenPlaced ||
                !block.gameObject.activeInHierarchy || !block.IsWalkable())
                continue;

            Transform blockTransform = block.transform;
            Plane plane = new Plane(
                blockTransform.up, blockTransform.position);
            Ray downRay = new Ray(
                new Vector3(worldPosition.x,
                    blockTransform.position.y + 4f,
                    worldPosition.z),
                Vector3.down);

            float distance;
            if (!plane.Raycast(downRay, out distance))
                continue;

            Vector3 contact = downRay.GetPoint(distance);
            Vector3 local = blockTransform.InverseTransformPoint(contact);

            if (Mathf.Abs(local.x) > 0.78f ||
                Mathf.Abs(local.z) > 0.78f)
                continue;

            float candidateScore =
                Mathf.Abs(contact.y - expectedFeetY) +
                (Mathf.Abs(local.x) + Mathf.Abs(local.z)) * 0.01f;

            if (candidateScore < score)
            {
                score = candidateScore;
                deckPoint = contact;
                found = true;
            }
        }

        return found && score < 12f;
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
