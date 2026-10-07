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

public enum RebuildableRaftMessage
{
    SecondaryRaftState = 8127,
    RaftName = 8128
}

[Serializable]
public class Message_SecondaryRaftState : Message
{
    public int raftId;
    public bool relativeToMain;
    public float x;
    public float y;
    public float z;
    public float pitch;
    public float yaw;
    public float roll;
    public float velocityX;
    public float velocityZ;
    public float angularVelocityY;

    public Message_SecondaryRaftState()
    {
    }

    public Message_SecondaryRaftState(
        SecondaryRaftRoot raft)
        : base(
            (Messages)RebuildableRaftMessage.SecondaryRaftState
        )
    {
        raftId = raft.RaftId;

        Vector3 position =
            raft.transform.position;

        float raftYaw =
            raft.transform.eulerAngles.y;

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot != null)
        {
            position =
                mainPivot.InverseTransformPoint(
                    position
                );

            raftYaw =
                Mathf.DeltaAngle(
                    mainPivot.eulerAngles.y,
                    raftYaw
                );

            relativeToMain = true;
        }

        x = position.x;
        y = position.y;
        z = position.z;

        Transform wavePivot =
            raft.BuildPivot;

        Vector3 waveRotation =
            wavePivot != null
                ? wavePivot.localEulerAngles
                : Vector3.zero;

        pitch = waveRotation.x;
        yaw = raftYaw;
        roll = waveRotation.z;

        Rigidbody body =
            raft.Body;

        if (body != null)
        {
            velocityX = body.velocity.x;
            velocityZ = body.velocity.z;
            angularVelocityY = body.angularVelocity.y;
        }
    }

    public override void SerializeFast(
        FastBufferWriter writer)
    {
        base.SerializeFast(writer);

        if (!writer.TryBeginWrite(41))
        {
            throw new OverflowException(
                "Not enough space in the buffer"
            );
        }

        writer.WriteValue<int>(
            raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<bool>(
            relativeToMain,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            x,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            y,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            z,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            pitch,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            yaw,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            roll,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            velocityX,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            velocityZ,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<float>(
            angularVelocityY,
            default(FastBufferWriter.ForPrimitives)
        );
    }

    public override void DeserializeFast(
        FastBufferReader reader)
    {
        base.DeserializeFast(reader);

        reader.ReadValue<int>(
            out raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<bool>(
            out relativeToMain,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out x,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out y,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out z,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out pitch,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out yaw,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out roll,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out velocityX,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out velocityZ,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<float>(
            out angularVelocityY,
            default(FastBufferWriter.ForPrimitives)
        );
    }
}


[Serializable]
public class Message_RaftName : Message
{
    public int raftId;
    public bool request;
    public string raftName;

    public Message_RaftName()
    {
    }

    public Message_RaftName(
        int raftId,
        string raftName,
        bool request)
        : base(
            (Messages)RebuildableRaftMessage.RaftName
        )
    {
        this.raftId = raftId;
        this.raftName =
            RaftNameRegistry.NormalizeName(
                raftName,
                raftId
            );
        this.request = request;
    }

    public override void SerializeFast(
        FastBufferWriter writer)
    {
        base.SerializeFast(writer);

        byte[] bytes =
            System.Text.Encoding.UTF8.GetBytes(
                raftName ?? string.Empty
            );

        if (bytes.Length > 96)
        {
            Array.Resize(
                ref bytes,
                96
            );
        }

        ushort length =
            (ushort)bytes.Length;

        if (!writer.TryBeginWrite(
                7 + length
            ))
        {
            throw new OverflowException(
                "Not enough space in the buffer"
            );
        }

        writer.WriteValue<int>(
            raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<bool>(
            request,
            default(FastBufferWriter.ForPrimitives)
        );

        writer.WriteValue<ushort>(
            length,
            default(FastBufferWriter.ForPrimitives)
        );

        for (int i = 0; i < bytes.Length; i++)
        {
            writer.WriteValue<byte>(
                bytes[i],
                default(FastBufferWriter.ForPrimitives)
            );
        }
    }

    public override void DeserializeFast(
        FastBufferReader reader)
    {
        base.DeserializeFast(reader);

        reader.ReadValue<int>(
            out raftId,
            default(FastBufferWriter.ForPrimitives)
        );

        reader.ReadValue<bool>(
            out request,
            default(FastBufferWriter.ForPrimitives)
        );

        ushort length;

        reader.ReadValue<ushort>(
            out length,
            default(FastBufferWriter.ForPrimitives)
        );

        int safeLength =
            Mathf.Min(
                (int)length,
                96
            );

        byte[] bytes =
            new byte[safeLength];

        for (int i = 0; i < length; i++)
        {
            byte value;

            reader.ReadValue<byte>(
                out value,
                default(FastBufferWriter.ForPrimitives)
            );

            if (i < safeLength)
            {
                bytes[i] = value;
            }
        }

        raftName =
            System.Text.Encoding.UTF8.GetString(
                bytes
            );
    }
}

[HarmonyPatch(
    typeof(Message_Player_Update),
    MethodType.Constructor,
    new Type[]
    {
        typeof(Messages),
        typeof(MonoBehaviour_Network),
        typeof(Network_Player)
    }
)]
public static class Message_Player_Update_Constructor_RebuildableRaft
{
    [HarmonyPostfix]
    public static void Postfix(
        Message_Player_Update __instance,
        Network_Player playerNetwork)
    {
        if (__instance == null ||
            playerNetwork == null ||
            playerNetwork.PersonController == null)
        {
            return;
        }

        PersonController personController =
            playerNetwork.PersonController;

        SecondaryRaftRoot root =
            personController.transform
                .GetComponentInParent
                    <SecondaryRaftRoot>();

        if (root == null)
        {
            root =
                MultiRaftRegistry
                    .FindRootAtGroundPoint(
                        playerNetwork.FeetPosition
                    );
        }

        if (root == null)
        {
            return;
        }

        __instance.RaftAsParent =
            false;

        __instance.Position =
            personController.transform.position;
    }
}
