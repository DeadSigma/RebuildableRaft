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

public class RebuildableRaft : Mod
{
    internal const int MultiRaftNetworkChannel = 71;

    private const float NetworkSyncInterval = 0.1f;
    private const float RaftNameSyncInterval = 2f;

    private Harmony harmony;
    private float networkSyncTimer;
    private float raftNameSyncTimer;

    public void Awake()
    {
        MultiRaftRegistry.Reset();
        RaftNameRegistry.Reset();

        harmony = new Harmony("el.rebuildableraft.multiraft");
        harmony.PatchAll();

        Debug.Log("[RebuildableRaft] Loaded");
    }

    public void Update()
    {
        if (!RAPI.IsCurrentSceneGame())
        {
            return;
        }

        ReceiveNetworkMessages();
        RaftRenameWindow.Update();

        if (!Raft_Network.IsHost)
        {
            return;
        }

        networkSyncTimer += Time.deltaTime;
        raftNameSyncTimer += Time.deltaTime;

        if (networkSyncTimer >= NetworkSyncInterval)
        {
            networkSyncTimer = 0f;
            SendRaftStates();
        }

        if (raftNameSyncTimer >= RaftNameSyncInterval)
        {
            raftNameSyncTimer = 0f;
            BroadcastAllRaftNames();
        }
    }

    public override void WorldEvent_WorldLoaded()
    {
        MultiRaftRegistry.RebuildFromScene();
        RaftNameRegistry.LoadCurrentWorld();

        if (Raft_Network.IsHost)
        {
            BroadcastAllRaftNames();
        }
    }

    [Obsolete]
    public override void WorldEvent_WorldUnloaded()
    {
        RaftRenameWindow.Close(false);
        RaftReceiverRadar.Reset();
        RaftNameRegistry.Reset();
        MultiRaftRegistry.Reset();
    }

    public void OnModUnload()
    {
        RaftRenameWindow.Close(false);
        RaftReceiverRadar.Reset();

        if (harmony != null)
        {
            harmony.UnpatchAll(harmony.Id);
        }

        RaftNameRegistry.Reset();
        MultiRaftRegistry.Reset();

        Debug.Log("[RebuildableRaft] Unloaded");
    }

    private static void SendRaftStates()
    {
        foreach (SecondaryRaftRoot raft in MultiRaftRegistry.GetRoots())
        {
            if (raft == null || !raft.HasPlacedBlocks)
            {
                continue;
            }

#pragma warning disable CS0618
            RAPI.SendNetworkMessage(
                raft.CreateStateMessage(),
                MultiRaftNetworkChannel,
                EP2PSend.k_EP2PSendReliable,
                Target.Other
            );
#pragma warning restore CS0618
        }
    }

    internal static void RequestRaftNameChange(
        int raftId,
        string raftName)
    {
        string normalized =
            RaftNameRegistry.NormalizeName(
                raftName,
                raftId
            );

        RaftNameRegistry.SetLocalName(
            raftId,
            normalized,
            Raft_Network.IsHost
        );

        if (Raft_Network.IsHost)
        {
            BroadcastRaftName(
                raftId,
                normalized
            );

            return;
        }

#pragma warning disable CS0618
        RAPI.SendNetworkMessage(
            new Message_RaftName(
                raftId,
                normalized,
                true
            ),
            MultiRaftNetworkChannel,
            EP2PSend.k_EP2PSendReliable,
            Target.Other
        );
#pragma warning restore CS0618
    }

    private static void BroadcastAllRaftNames()
    {
        BroadcastRaftName(
            0,
            RaftNameRegistry.GetName(0)
        );

        foreach (SecondaryRaftRoot raft in MultiRaftRegistry.GetRoots())
        {
            if (raft == null || !raft.HasPlacedBlocks)
            {
                continue;
            }

            BroadcastRaftName(
                raft.RaftId,
                RaftNameRegistry.GetName(
                    raft.RaftId
                )
            );
        }
    }

    private static void BroadcastRaftName(
        int raftId,
        string raftName)
    {
#pragma warning disable CS0618
        RAPI.SendNetworkMessage(
            new Message_RaftName(
                raftId,
                raftName,
                false
            ),
            MultiRaftNetworkChannel,
            EP2PSend.k_EP2PSendReliable,
            Target.Other
        );
#pragma warning restore CS0618
    }

#pragma warning disable CS0618
    private static void ReceiveNetworkMessages()
    {
        for (int i = 0; i < 64; i++)
        {
            NetworkMessage networkMessage =
                RAPI.ListenForNetworkMessagesOnChannel(
                    MultiRaftNetworkChannel
                );

            if (networkMessage == null)
            {
                break;
            }

            Message message =
                networkMessage.message;

            if (message == null)
            {
                continue;
            }

            if (message.Type ==
                (Messages)RebuildableRaftMessage.SecondaryRaftState)
            {
                ReceiveRaftState(
                    message as Message_SecondaryRaftState
                );

                continue;
            }

            if (message.Type ==
                (Messages)RebuildableRaftMessage.RaftName)
            {
                ReceiveRaftName(
                    message as Message_RaftName
                );
            }
        }
    }
#pragma warning restore CS0618

    private static void ReceiveRaftState(
        Message_SecondaryRaftState state)
    {
        if (state == null ||
            Raft_Network.IsHost)
        {
            return;
        }

        SecondaryRaftRoot raft =
            MultiRaftRegistry.GetRoot(
                state.raftId
            );

        if (raft != null)
        {
            raft.ApplyNetworkState(state);
        }
    }

    private static void ReceiveRaftName(
        Message_RaftName message)
    {
        if (message == null)
        {
            return;
        }

        if (message.request)
        {
            if (!Raft_Network.IsHost ||
                !RaftNameRegistry.IsValidRaftId(
                    message.raftId
                ))
            {
                return;
            }

            string normalized =
                RaftNameRegistry.NormalizeName(
                    message.raftName,
                    message.raftId
                );

            RaftNameRegistry.SetLocalName(
                message.raftId,
                normalized,
                true
            );

            BroadcastRaftName(
                message.raftId,
                normalized
            );

            return;
        }

        if (Raft_Network.IsHost)
        {
            return;
        }

        RaftNameRegistry.SetLocalName(
            message.raftId,
            message.raftName,
            false
        );
    }
}
