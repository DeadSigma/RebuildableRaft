using HarmonyLib;
using Steamworks;
using UnityEngine;

public static class SecondaryRaftPropulsionContext
{
    public static SecondaryRaftRoot PaddleRoot;
    public static Vector3 PaddleDirection;
    public static float PaddleForce;
    public static SecondaryRaftRoot MotorRoot;

    public static SecondaryRaftRoot ResolvePlayerRoot(
        Network_Player player)
    {
        if (player == null)
        {
            return null;
        }

        SecondaryRaftRoot root =
            MultiRaftRegistry
                .GetRootFromTransform(
                    player.transform
                );

        if (root != null)
        {
            return root;
        }

        if (player.PersonController != null &&
            player.PersonController.HasRaftAsParent)
        {
            return null;
        }

        SecondaryRaftRoot bestRoot = null;
        float bestScore = float.MaxValue;

        foreach (SecondaryRaftRoot candidate
            in MultiRaftRegistry.GetRoots())
        {
            if (candidate == null)
            {
                continue;
            }

            float score =
                candidate.GetGroundPointScore(
                    player.transform.position
                );

            if (score < bestScore)
            {
                bestScore = score;
                bestRoot = candidate;
            }
        }

        return bestScore < float.MaxValue
            ? bestRoot
            : null;
    }

    public static MotorWheel.WeightStrength
        GetMotorWeightStrength(
            SecondaryRaftRoot root)
    {
        if (root == null ||
            root.IsAnchored)
        {
            return MotorWheel.WeightStrength
                .NotStrongEnough;
        }

        int foundationWeight = 0;

        Block[] blocks =
            root.GetComponentsInChildren<Block>(
                true
            );

        for (int i = 0;
             i < blocks.Length;
             i++)
        {
            Block block = blocks[i];

            if (block == null ||
                block.buildableItem == null ||
                !block.hasBeenPlaced ||
                !block.gameObject.activeSelf)
            {
                continue;
            }

            if (Block.IsBlockIndexFoundation(
                    block.buildableItem.UniqueIndex
                ))
            {
                foundationWeight++;
            }
        }

        int motorStrength = 0;
        int extraMotorStrength = 0;

        MotorWheel[] motors =
            root.GetComponentsInChildren<MotorWheel>(
                true
            );

        for (int i = 0;
             i < motors.Length;
             i++)
        {
            MotorWheel motor = motors[i];

            if (motor == null)
            {
                continue;
            }

            motorStrength +=
                motor.MotorStrength;

            extraMotorStrength +=
                motor.ExtraMotorStrength;
        }

        if (foundationWeight <=
            motorStrength)
        {
            return MotorWheel.WeightStrength
                .StrongEnough;
        }

        if (foundationWeight <=
            motorStrength +
            extraMotorStrength)
        {
            return MotorWheel.WeightStrength.Weak;
        }

        return MotorWheel.WeightStrength
            .NotStrongEnough;
    }
}

[HarmonyPatch(typeof(Paddle), "OnPaddle")]
public static class Paddle_OnPaddle_SecondaryRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        Paddle __instance,
        Network_Player ___playerNetwork,
        Transform ___particleTransform,
        float ___paddleForce)
    {
        if (__instance == null ||
            ___playerNetwork == null ||
            !___playerNetwork.IsLocalPlayer)
        {
            return true;
        }

        SecondaryRaftRoot root =
            SecondaryRaftPropulsionContext
                .ResolvePlayerRoot(
                    ___playerNetwork
                );

        if (root == null)
        {
            return true;
        }

        Transform pivot = root.BuildPivot;

        if (pivot != null)
        {
            float localY =
                pivot.InverseTransformPoint(
                    ___playerNetwork.transform.position
                ).y;

            if (localY > 2.5f)
            {
                return false;
            }
        }

        if (___playerNetwork.playerPivot == null ||
            ___playerNetwork.playerPivot
                .localEulerAngles.x > 100f ||
            ___particleTransform == null)
        {
            return false;
        }

        Vector3 forward =
            ___playerNetwork.transform.forward;

        Message_Paddle message =
            new Message_Paddle(
                Messages.Paddle,
                ___playerNetwork,
                ___particleTransform.position,
                forward,
                ___paddleForce
            );

        if (Raft_Network.IsHost)
        {
            ___playerNetwork.Network.RPC(
                message,
                Target.Other,
                EP2PSend.k_EP2PSendReliable,
                NetworkChannel.Channel_Game
            );

            __instance.PaddlePaddle(
                ___particleTransform.position,
                forward,
                ___paddleForce
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

        return false;
    }
}

[HarmonyPatch(typeof(Paddle), "PaddlePaddle")]
public static class Paddle_PaddlePaddle_SecondaryRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        Network_Player ___playerNetwork,
        Vector3 direction,
        float force)
    {
        SecondaryRaftRoot root =
            SecondaryRaftPropulsionContext
                .ResolvePlayerRoot(
                    ___playerNetwork
                );

        SecondaryRaftPropulsionContext.PaddleRoot =
            root;

        SecondaryRaftPropulsionContext.PaddleDirection =
            direction;

        SecondaryRaftPropulsionContext.PaddleForce =
            force;
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        SecondaryRaftPropulsionContext.PaddleRoot =
            null;

        SecondaryRaftPropulsionContext.PaddleDirection =
            Vector3.zero;

        SecondaryRaftPropulsionContext.PaddleForce =
            0f;
    }
}

[HarmonyPatch(typeof(Raft), "AddForce")]
public static class Raft_AddForce_Paddle_SecondaryRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        ForceMode forceMode)
    {
        SecondaryRaftRoot root =
            SecondaryRaftPropulsionContext
                .PaddleRoot;

        if (root == null ||
            root.Body == null)
        {
            return true;
        }

        if (Raft_Network.IsHost)
        {
            Vector3 direction =
                SecondaryRaftPropulsionContext
                    .PaddleDirection;

            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();

                root.Body.AddForce(
                    direction *
                    SecondaryRaftPropulsionContext
                        .PaddleForce,
                    forceMode
                );
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(MotorWheel), "Update")]
public static class MotorWheel_Update_SecondaryRaft
{
    [HarmonyPrefix]
    public static void Prefix(
        MotorWheel __instance)
    {
        SecondaryRaftPropulsionContext.MotorRoot =
            __instance == null
                ? null
                : MultiRaftRegistry
                    .GetRootFromTransform(
                        __instance.transform
                    );
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        SecondaryRaftPropulsionContext.MotorRoot =
            null;
    }
}

[HarmonyPatch(
    typeof(RaftVelocityManager),
    "get_MotorWheelWeightStrength"
)]
public static class RaftVelocityManager_MotorWeight_SecondaryRaft
{
    [HarmonyPrefix]
    public static bool Prefix(
        ref MotorWheel.WeightStrength __result)
    {
        SecondaryRaftRoot root =
            SecondaryRaftPropulsionContext
                .MotorRoot;

        if (root == null)
        {
            return true;
        }

        __result =
            SecondaryRaftPropulsionContext
                .GetMotorWeightStrength(root);

        return false;
    }
}
