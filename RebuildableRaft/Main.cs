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
    internal const string MultiRaftNetworkChannel = "el.rebuildableraft.network.v2";
    private const int NetworkProtocolVersion = 2;

    private static RebuildableRaft activeInstance;
    private static readonly Dictionary<int, int> outgoingSequences = new Dictionary<int, int>();
    private static readonly Dictionary<int, int> incomingSequences = new Dictionary<int, int>();
    private static int sentStates;
    private static int receivedStates;
    private static int appliedStates;
    private static int unknownRafts;
    private static int obsoleteStates;
    private static float nextStatusLogTime;
    private static float nextWarningTime;

    private const float NetworkSyncInterval = 0.05f;
    private const float RaftNameSyncInterval = 2f;

    private Harmony harmony;
    private float networkSyncTimer;
    private float raftNameSyncTimer;

    public void Awake()
    {
        activeInstance = this;
        outgoingSequences.Clear();
        incomingSequences.Clear();
        sentStates = 0;
        receivedStates = 0;
        appliedStates = 0;
        unknownRafts = 0;
        obsoleteStates = 0;
        nextStatusLogTime = Time.realtimeSinceStartup + 5f;
        nextWarningTime = 0f;

        SubscribeToNetworkChannel(MultiRaftNetworkChannel);
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

        RaftRenameWindow.Update();

        if (Time.realtimeSinceStartup >= nextStatusLogTime)
        {
            nextStatusLogTime = Time.realtimeSinceStartup + 5f;
            Debug.Log("[RebuildableRaft][NetChannel] role=" +
                (Raft_Network.IsHost ? "HOST" : "CLIENT") +
                " sent=" + sentStates +
                " recv=" + receivedStates +
                " applied=" + appliedStates +
                " missingRoot=" + unknownRafts +
                " stale=" + obsoleteStates);

            foreach (SecondaryRaftRoot raft in MultiRaftRegistry.GetRoots())
            {
                if (raft != null && raft.HasPlacedBlocks)
                    Debug.Log(raft.BuildPeriodicDiagnosticLine());
            }
        }

        if (!Raft_Network.IsHost)
        {
            return;
        }

        networkSyncTimer += Time.deltaTime;
        raftNameSyncTimer += Time.deltaTime;

        if (networkSyncTimer >= NetworkSyncInterval)
        {
            networkSyncTimer = Mathf.Clamp(
                networkSyncTimer - NetworkSyncInterval,
                0f,
                NetworkSyncInterval
            );
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
        incomingSequences.Clear();
        outgoingSequences.Clear();
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
        incomingSequences.Clear();
        outgoingSequences.Clear();
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
        activeInstance = null;
        outgoingSequences.Clear();
        incomingSequences.Clear();

        Debug.Log("[RebuildableRaft] Unloaded");
    }

    private static void SendRaftStates()
    {
        foreach (SecondaryRaftRoot raft in MultiRaftRegistry.GetRoots())
        {
            if (raft == null || !raft.HasPlacedBlocks)
                continue;

            Message_SecondaryRaftState state = raft.CreateStateMessage();
            state.protocolVersion = NetworkProtocolVersion;

            int previous;
            outgoingSequences.TryGetValue(state.raftId, out previous);
            state.sequence = previous + 1;
            outgoingSequences[state.raftId] = state.sequence;

            if (state.sequence % 40 == 0)
                Debug.Log(raft.BuildPacketDiagnosticLine("SEND", state));

            SendNetworkPayload(state, EP2PSend.k_EP2PSendUnreliable);
            sentStates++;
        }
    }

    private static void SendNetworkPayload(object payload, EP2PSend sendType)
    {
        if (activeInstance == null || payload == null)
            return;

        // Сообщение отправляется через подписку RML без устаревшей очереди oldmsg_
        activeInstance.SendNetworkMessage(
            payload,
            Target.Other,
            sendType,
            MultiRaftNetworkChannel
        );
    }

    public override bool OnNetworkMessage(
        object message,
        Network_UserId from,
        string channel)
    {
        if (channel != MultiRaftNetworkChannel)
            return false;

        Message_SecondaryRaftState state = message as Message_SecondaryRaftState;
        if (state != null)
        {
            ReceiveRaftState(state);
            return true;
        }

        Message_RaftName raftName = message as Message_RaftName;
        if (raftName != null)
        {
            ReceiveRaftName(raftName);
            return true;
        }

        if (Time.realtimeSinceStartup >= nextWarningTime)
        {
            nextWarningTime = Time.realtimeSinceStartup + 10f;
            Debug.LogWarning("[RebuildableRaft][NetChannel] Unsupported packet: " +
                (message == null ? "null" : message.GetType().FullName));
        }

        return false;
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

        SendNetworkPayload(
            new Message_RaftName(
                raftId,
                normalized,
                true
            ),
            EP2PSend.k_EP2PSendReliable
        );
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
        SendNetworkPayload(
            new Message_RaftName(
                raftId,
                raftName,
                false
            ),
            EP2PSend.k_EP2PSendReliable
        );
    }

    private static void ReceiveRaftState(Message_SecondaryRaftState state)
    {
        if (state == null || Raft_Network.IsHost)
            return;

        receivedStates++;
        if (state.protocolVersion != NetworkProtocolVersion)
        {
            if (Time.realtimeSinceStartup >= nextWarningTime)
            {
                nextWarningTime = Time.realtimeSinceStartup + 10f;
                Debug.LogWarning("[RebuildableRaft][NetChannel] Protocol mismatch for raft=" +
                    state.raftId + " got=" + state.protocolVersion +
                    " expected=" + NetworkProtocolVersion);
            }
            return;
        }

        int previous;
        if (incomingSequences.TryGetValue(state.raftId, out previous) &&
            state.sequence <= previous)
        {
            obsoleteStates++;
            return;
        }

        SecondaryRaftRoot raft = MultiRaftRegistry.GetRoot(state.raftId);
        if (raft == null)
        {
            unknownRafts++;
            if (Time.realtimeSinceStartup >= nextWarningTime)
            {
                nextWarningTime = Time.realtimeSinceStartup + 10f;
                Debug.LogWarning("[RebuildableRaft][NetChannel] Missing raft root id=" +
                    state.raftId + " seq=" + state.sequence);
            }
            // Пакет не фиксируется как принятый, следующий сможет примениться после создания плота
            return;
        }

        incomingSequences[state.raftId] = state.sequence;
        raft.ApplyNetworkState(state);
        appliedStates++;

        if (state.sequence % 40 == 0)
            Debug.Log(raft.BuildPacketDiagnosticLine("RECV", state));
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
