using HarmonyLib;
using HMLLibrary;
using System;
using UnityEngine;

[Serializable]
public class Message_SecondaryRaftState
{
    public int protocolVersion = 2;
    public int sequence;
    public uint debugSequence;
    public int debugHostFrame;
    public float debugHostTime;
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

    public Message_SecondaryRaftState(SecondaryRaftRoot raft)
    {
        debugSequence = raft.NextNetworkDebugSequence();
        debugHostFrame = Time.frameCount;
        debugHostTime = Time.time;
        raftId = raft.RaftId;
        Vector3 position = raft.transform.position;
        float raftYaw = raft.transform.eulerAngles.y;
        Transform mainPivot = SecondaryRaftRoot.GetMainNetworkAnchor();

        if (mainPivot != null)
        {
            Vector3 delta = position - mainPivot.position;
            Quaternion inverseMainYaw = Quaternion.Euler(0f, -mainPivot.eulerAngles.y, 0f);
            Vector3 relative = inverseMainYaw * new Vector3(delta.x, 0f, delta.z);
            position = new Vector3(relative.x, position.y, relative.z);
            raftYaw = Mathf.DeltaAngle(mainPivot.eulerAngles.y, raftYaw);
            relativeToMain = true;
        }

        x = position.x;
        y = position.y;
        z = position.z;

        Transform wavePivot = raft.BuildPivot;
        Vector3 waveRotation = wavePivot != null ? wavePivot.localEulerAngles : Vector3.zero;
        pitch = waveRotation.x;
        yaw = raftYaw;
        roll = waveRotation.z;

        Rigidbody body = raft.Body;
        if (body != null)
        {
            velocityX = body.velocity.x;
            velocityZ = body.velocity.z;
            angularVelocityY = body.angularVelocity.y;
        }
    }
}

[Serializable]
public class Message_RaftName
{
    public int protocolVersion = 2;
    public int raftId;
    public bool request;
    public string raftName;

    public Message_RaftName()
    {
    }

    public Message_RaftName(int raftId, string raftName, bool request)
    {
        this.raftId = raftId;
        this.raftName = RaftNameRegistry.NormalizeName(raftName, raftId);
        this.request = request;
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

        Transform mainPivot =
            MultiRaftRegistry.MainPivot;

        if (mainPivot != null)
        {
            __instance.RaftAsParent = true;
            __instance.Position = mainPivot.InverseTransformPoint(
                personController.transform.position
            );
        }
        else
        {
            __instance.RaftAsParent = false;
            __instance.Position = personController.transform.position;
        }
    }
}
