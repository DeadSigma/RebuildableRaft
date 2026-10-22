using HarmonyLib;
using System;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

[HarmonyPatch(typeof(Axe), "Update")]
public static class Axe_Update_RebuildableRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Axe __instance,
        Network_Player ___playerNetwork,
        CanvasHelper ___canvas,
        InputActionReference ___inputUse,
        ref RaycastHit ___rayHit,
        ref Block ___aimedAtBlock,
        ref Block ___currentBlockToRemove,
        ref float ___chopTimer,
        float ___chopBlockTime)
    {
        if (__instance == null ||
            ___playerNetwork == null ||
            CanvasHelper.ActiveMenu != MenuType.None ||
            !___playerNetwork.IsLocalPlayer)
        {
            return true;
        }

        Physics.SyncTransforms();

        RaycastHit hit;

        if (!TryGetAxeHit(
                ___playerNetwork,
                __instance.hitmask,
                out hit
            ))
        {
            if (___aimedAtBlock != null &&
                MultiRaftRegistry.IsSecondaryBlock(
                    ___aimedAtBlock
                ))
            {
                ClearSecondaryTarget(
                    ___canvas,
                    ref ___aimedAtBlock,
                    ref ___currentBlockToRemove,
                    ref ___chopTimer
                );

                return false;
            }

            return true;
        }

        Block block =
            hit.collider.GetComponentInParent<Block>();

        if (block == null ||
            !MultiRaftRegistry.IsSecondaryBlock(block) ||
            block.buildableItem == null ||
            block.buildableItem
                .settings_buildable.Placeable)
        {
            return true;
        }

        ___rayHit = hit;

        if (___aimedAtBlock == null)
        {
            ___aimedAtBlock = block;
            ___aimedAtBlock.SetInstanceOutline(true);
        }
        else if (___aimedAtBlock != block)
        {
            ___aimedAtBlock.SetInstanceOutline(false);
            ___aimedAtBlock = block;
            ___aimedAtBlock.SetInstanceOutline(true);
        }

        if (___inputUse == null ||
            !SimpleMonoBehaviourSingleton
                <CustomInputConfig>.Instance
                .IsPressed(
                    ___inputUse.action,
                    "LMB"
                ))
        {
            if (___currentBlockToRemove != null)
            {
                ___currentBlockToRemove = null;
                ___chopTimer = 0f;

                if (___canvas != null)
                {
                    ___canvas.SetLoadCircle(false);
                }
            }

            return false;
        }

        if (___aimedAtBlock !=
                ___currentBlockToRemove ||
            ___currentBlockToRemove == null)
        {
            ___chopTimer = 0f;
            ___currentBlockToRemove =
                ___aimedAtBlock;
        }

        ___chopTimer +=
            Application.isEditor
                ? Time.deltaTime * 4f
                : Time.deltaTime;

        float duration =
            ___chopBlockTime /
            GameModeValueManager
                .GetCurrentGameModeValue()
                .toolVariables
                .removeSpeedMultiplier;

        if (___chopTimer >= duration)
        {
            Block target =
                ___currentBlockToRemove;

            ___chopTimer = 0f;

            Message_BlockCreator_RemoveBlock message =
                new Message_BlockCreator_RemoveBlock(
                    Messages.Axe_RemoveBlock,
                    ___playerNetwork,
                    0UL,
                    target,
                    true
                );

            if (Raft_Network.IsHost)
            {
                ___playerNetwork.Network.RPC(
                    message,
                    Target.Other,
                    EP2PSend.k_EP2PSendReliable,
                    NetworkChannel.Channel_Game
                );

                __instance.DestroyBlock(
                    target,
                    true
                );
            }
            else
            {
                ___playerNetwork.SendP2P(
                    message,
                    EP2PSend.k_EP2PSendReliable,
                    NetworkChannel.Channel_Game
                );
            }
        }

        if (___canvas != null)
        {
            ___canvas.SetLoadCircle(
                ___chopTimer > 0f
            );

            ___canvas.SetLoadCircle(
                ___chopTimer / duration
            );
        }

        return false;
    }

    private static bool TryGetAxeHit(
        Network_Player player,
        LayerMask hitMask,
        out RaycastHit selectedHit)
    {
        selectedHit = default(RaycastHit);

        if (player == null ||
            player.CameraTransform == null)
        {
            return false;
        }

        Ray ray =
            new Ray(
                player.CameraTransform.position,
                player.CameraTransform.forward
            );

        RaycastHit[] hits =
            Physics.RaycastAll(
                ray,
                5f,
                hitMask,
                QueryTriggerInteraction.UseGlobal
            );

        if (hits == null ||
            hits.Length == 0)
        {
            return false;
        }

        Array.Sort(
            hits,
            delegate (
                RaycastHit left,
                RaycastHit right)
            {
                return left.distance
                    .CompareTo(right.distance);
            }
        );

        for (int i = 0;
             i < hits.Length;
             i++)
        {
            Collider collider =
                hits[i].collider;

            if (collider == null ||
                IsSyntheticProxy(collider))
            {
                continue;
            }

            selectedHit = hits[i];
            return true;
        }

        return false;
    }

    private static bool IsSyntheticProxy(
        Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        return
            collider.GetComponent
                <SecondaryRaftCollisionProxy>() != null ||
            collider.GetComponent
                <SecondaryRaftBlockRayProxy>() != null ||
            collider.GetComponent
                <SecondaryRaftInteractionProxy>() != null;
    }

    private static void ClearSecondaryTarget(
        CanvasHelper canvas,
        ref Block aimedAtBlock,
        ref Block currentBlockToRemove,
        ref float chopTimer)
    {
        if (aimedAtBlock != null)
        {
            aimedAtBlock.SetInstanceOutline(false);
            aimedAtBlock = null;
        }

        currentBlockToRemove = null;
        chopTimer = 0f;

        if (canvas != null)
        {
            canvas.SetLoadCircle(false);
        }
    }

}
