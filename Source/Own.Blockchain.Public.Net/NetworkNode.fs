﻿namespace Own.Blockchain.Public.Net

open System.Collections.Concurrent
open System.Threading
open Own.Common
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Core.Events

type NetworkNode
    (
    getAllPeerNodes,
    savePeerNode : NetworkAddress -> Result<unit, AppErrors>,
    removePeerNode : NetworkAddress -> Result<unit, AppErrors>,
    sendGossipDiscoveryMessage,
    sendGossipMessage,
    sendMulticastMessage,
    sendUnicastMessage,
    receiveMessage,
    closeConnection,
    closeAllConnections,
    getCurrentValidators : unit -> ValidatorSnapshot list,
    config : NetworkNodeConfig,
    fanout,
    tCycle,
    tFail
    ) =

    let activeMembers = new ConcurrentDictionary<NetworkAddress, GossipMember>()
    let deadMembers = new ConcurrentDictionary<NetworkAddress, GossipMember>()
    let gossipMessages = new ConcurrentDictionary<NetworkMessageId, NetworkAddress list>()
    let pendingDataRequests = new ConcurrentDictionary<NetworkMessageId, NetworkAddress list>()
    let memberStateMonitor = new ConcurrentDictionary<NetworkAddress, CancellationTokenSource>()
    let cts = new CancellationTokenSource()

    let printActiveMembers () =
        #if DEBUG
            Log.debug "====================== ACTIVE CONNECTIONS ======================"
            for m in activeMembers do
                Log.debugf "%s Heartbeat:%i" m.Key.Value m.Value.Heartbeat
            Log.debug "================================================================"
        #else
            ()
        #endif

    let isSelf networkAddress =
        config.PublicAddress.IsSome && networkAddress = config.PublicAddress.Value

    let optionToList = function | Some x -> [x] | None -> []

    (*
        A member is dead if it's in the list of dead-members and
        the heartbeat the local node is bigger than the one passed by argument.
    *)
    let isDead inputMember =
        match deadMembers.TryGetValue inputMember.NetworkAddress with
        | true, deadMember ->
            Log.debugf "Received a node with heartbeat %i, in dead-members it has heartbeat %i"
                inputMember.Heartbeat
                deadMember.Heartbeat
            deadMember.Heartbeat >= inputMember.Heartbeat
        | _ -> false

    (*
        Once a member has been declared dead and it hasn't recovered in
        2xTFail time is removed from the dead-members list.
        So if node has been down for a while and come back it can be added again.
        Here this will be scheduled right after a node is declared, so total time
        elapsed is 2xTFail
    *)
    let setFinalDeadMember networkAddress =
        async {
            do! Async.Sleep (tFail)

            match activeMembers.TryGetValue networkAddress with
            | false, _ ->
                Log.debugf "*** Member marked as DEAD %s" networkAddress.Value

                deadMembers.TryRemove networkAddress |> ignore
                memberStateMonitor.TryRemove networkAddress |> ignore
                networkAddress.Value |> closeConnection

                match removePeerNode networkAddress with
                | Ok () -> ()
                | _ -> Log.errorf "Error removing member %A" networkAddress
            | _ -> ()
        }

    let monitorPendingDeadMember networkAddress =
        let cts = new CancellationTokenSource()
        Async.Start ((setFinalDeadMember networkAddress), cts.Token)
        memberStateMonitor.AddOrUpdate (networkAddress, cts, fun _ _ -> cts) |> ignore

    (*
        It declares a member as dead.
        - remove it from active nodes
        - add it to dead nodes
        - remove its timers
        - set to be removed from the dead-nodes. so that if it recovers can be added
    *)
    let setPendingDeadMember (networkAddress : NetworkAddress) =
        async {
            do! Async.Sleep (tFail)
            Log.debugf "*** Member potentially DEAD: %s" networkAddress.Value
            match activeMembers.TryGetValue networkAddress with
            | true, activeMember ->
                activeMembers.TryRemove networkAddress |> ignore
                deadMembers.AddOrUpdate (networkAddress, activeMember, fun _ _ -> activeMember) |> ignore
                monitorPendingDeadMember networkAddress
            | false, _ -> ()
        }

    let monitorActiveMember address =
        match memberStateMonitor.TryGetValue address with
        | true, cts -> cts.Cancel()
        | _ -> ()

        let cts = new CancellationTokenSource()
        Async.Start ((setPendingDeadMember address), cts.Token)
        memberStateMonitor.AddOrUpdate (address, cts, fun _ _ -> cts) |> ignore

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Public
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member __.StartGossip publishEvent =
        __.StartNode publishEvent
        __.StartGossipDiscovery ()
        Log.info "Network layer initialized"

    member __.StopGossip () =
        closeAllConnections ()
        cts.Cancel()

    member __.GetActiveMembers () =
        activeMembers
        |> List.ofDict
        |> List.map (fun (_, m) -> m)

    member __.GetListenAddress () =
        config.ListeningAddress

    member __.SendMessage message =
        let sendMessageTask =
            async {
                match message with
                | GossipDiscoveryMessage _ ->
                    __.SelectRandomMembers()
                    |> Option.iter (fun members ->
                        for m in members do
                            Log.debugf "Sending memberlist to: %s" m.NetworkAddress.Value
                            let peerMessageDto = Mapping.peerMessageToDto Serialization.serializeBinary message
                            sendGossipDiscoveryMessage
                                peerMessageDto
                                m.NetworkAddress.Value
                    )

                | MulticastMessage _ ->
                    let peerMessageDto = Mapping.peerMessageToDto Serialization.serializeBinary message
                    let multicastAddresses =
                        getCurrentValidators()
                        |> List.map (fun v -> v.NetworkAddress)
                        |> List.filter (fun a -> not (isSelf a))
                        |> List.map (fun a -> a.Value)

                    sendMulticastMessage
                        peerMessageDto
                        multicastAddresses

                | GossipMessage m -> __.SendGossipMessage m
                | _ -> ()
            }
        Async.Start (sendMessageTask, cts.Token)

    member __.IsRequestPending requestId =
        pendingDataRequests.ContainsKey requestId

    member __.SendRequestDataMessage requestId =
        Stats.increment Stats.Counter.PeerRequests
        let rec loop messageId =
            async {
                let expiredAddresses =
                    match pendingDataRequests.TryGetValue messageId with
                    | true, expiredAddresses -> expiredAddresses
                    | false, _ -> []

                let targetAddress =
                    let networkAddressPool =
                        __.GetActiveMembers()
                        |> List.map (fun m -> m.NetworkAddress)
                        |> List.filter (fun a -> not (isSelf a))
                        |> List.except expiredAddresses

                    let selectedUnicastPeer = __.SelectNewUnicastPeer networkAddressPool
                    match selectedUnicastPeer with
                    | None ->
                        Log.errorf "Cannot retrieve data from peers for %A" messageId
                        pendingDataRequests.TryRemove messageId |> ignore
                    | Some networkAddress ->
                        pendingDataRequests.AddOrUpdate(
                            messageId,
                            networkAddress :: expiredAddresses,
                            fun _ _ -> networkAddress :: expiredAddresses)
                        |> ignore
                    selectedUnicastPeer

                targetAddress |> Option.iter (fun address ->
                    let unicastMessage = RequestDataMessage {
                        MessageId = messageId
                        SenderAddress = config.ListeningAddress
                    }
                    let peerMessageDto = Mapping.peerMessageToDto Serialization.serializeBinary unicastMessage
                    sendUnicastMessage peerMessageDto address.Value
                )

                do! Async.Sleep(4 * tCycle)

                (*
                    If no answer is received within 2 cycles (request - response i.e 4xtCycle),
                    repeat (i.e choose another peer).
                *)
                match (pendingDataRequests.TryGetValue messageId) with
                | true, addresses ->
                    if not (addresses.IsEmpty) then
                        return! loop messageId
                | false, _ -> ()
            }
        Async.Start (loop requestId, cts.Token)

    member __.SendResponseDataMessage (targetAddress : NetworkAddress) responseMessage =
        let unicastMessageTask =
            async {
                let peerMessageDto = Mapping.peerMessageToDto Serialization.serializeBinary responseMessage
                sendUnicastMessage peerMessageDto targetAddress.Value
            }
        Async.Start (unicastMessageTask, cts.Token)

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Gossip Discovery
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.StartNode publishEvent =
        Log.debug "Start node..."
        __.InitializeMemberList()
        __.StartServer publishEvent

    member private __.StartServer publishEvent =
        Log.infof "Listen on: %s" config.ListeningAddress.Value
        config.PublicAddress |> Option.iter (fun a -> Log.infof "Public address: %s" a.Value)
        receiveMessage
            config.ListeningAddress.Value
            (__.ReceivePeerMessage publishEvent)

    member private __.StartGossipDiscovery () =
        let rec loop () =
            async {
                __.SendMembership ()
                do! Async.Sleep(tCycle)
                return! loop ()
            }
        if config.PublicAddress.IsSome then
            Async.Start (loop (), cts.Token)

    member private __.SendMembership () =
        __.IncreaseHeartbeat()
        GossipDiscoveryMessage {
            ActiveMembers = __.GetActiveMembers()
        }
        |> __.SendMessage
        printActiveMembers ()

    member private __.InitializeMemberList () =
        let publicAddress = config.PublicAddress |> optionToList
        getAllPeerNodes () @ config.BootstrapNodes @ publicAddress
        |> Set.ofList
        |> Set.iter (fun n -> __.AddMember { NetworkAddress = n; Heartbeat = 0L })

    member private __.AddMember inputMember =
        let rec loop (mem : GossipMember) =
            Log.debugf "Adding new member: %s" mem.NetworkAddress.Value
            activeMembers.AddOrUpdate (mem.NetworkAddress, mem, fun _ _ -> mem) |> ignore
            match savePeerNode mem.NetworkAddress with
            | Ok () -> ()
            | _ -> Log.errorf "Error saving member %A" mem.NetworkAddress

            if not (isSelf mem.NetworkAddress) then
                monitorActiveMember mem.NetworkAddress
        loop inputMember

    member private __.ReceiveActiveMember inputMember =
        match __.GetActiveMember inputMember.NetworkAddress with
        | Some m ->
            let localMember = {
                NetworkAddress = m.NetworkAddress
                Heartbeat = inputMember.Heartbeat
            }
            activeMembers.AddOrUpdate (inputMember.NetworkAddress, localMember, fun _ _ -> localMember) |> ignore
            match savePeerNode inputMember.NetworkAddress with
            | Ok () -> ()
            | _ -> Log.errorf "Error saving member %A" inputMember.NetworkAddress

            monitorActiveMember inputMember.NetworkAddress
        | None -> ()

    member private __.ReceiveMembers msg =
        __.MergeMemberList msg.ActiveMembers

    member private __.MergeMember inputMember =
        if not (isSelf inputMember.NetworkAddress) then
            Log.debugf "Receive member: %s" inputMember.NetworkAddress.Value
            match __.GetActiveMember inputMember.NetworkAddress with
            | Some localMember ->
                if localMember.Heartbeat < inputMember.Heartbeat then
                    __.ReceiveActiveMember inputMember
            | None ->
                if not (isDead inputMember) then
                    __.AddMember inputMember
                    deadMembers.TryRemove inputMember.NetworkAddress |> ignore

    member private __.MergeMemberList members =
        members |> List.iter (fun m -> __.MergeMember m)

    member private __.GetActiveMember networkAddress =
        match activeMembers.TryGetValue networkAddress with
        | true, localMember -> Some localMember
        | false, _ -> None

    member private __.IncreaseHeartbeat () =
        config.PublicAddress
        |> Option.iter (fun publicAddress ->
            __.GetActiveMember publicAddress
            |> Option.iter (fun m ->
                let localMember = {
                    NetworkAddress = m.NetworkAddress
                    Heartbeat = m.Heartbeat + 1L
                }
                activeMembers.AddOrUpdate (publicAddress, localMember, fun _ _ -> localMember) |> ignore
            )
        )

    member private __.SelectRandomMembers () =
        let connectedMembers =
            activeMembers
            |> List.ofDict
            |> List.filter (fun (a, _) -> not (isSelf a))

        match connectedMembers with
        | [] -> None
        | _ ->
            connectedMembers
            |> Seq.shuffle
            |> Seq.chunkBySize fanout
            |> Seq.head
            |> Seq.map (fun (_, m) -> m)
            |> Some

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Gossip Message Passing
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.UpdateGossipMessagesProcessingQueue networkAddresses gossipMessageId =
        let found, processedAddresses = gossipMessages.TryGetValue gossipMessageId
        let newProcessedAddresses = if found then networkAddresses @ processedAddresses else networkAddresses

        gossipMessages.AddOrUpdate(
            gossipMessageId,
            newProcessedAddresses,
            fun _ _ -> newProcessedAddresses) |> ignore

    member private __.SendGossipMessageToRecipient recipientAddress (gossipMessage : GossipMessage) =
        match activeMembers.TryGetValue recipientAddress with
        | true, recipientMember ->
            Log.debugf "Sending gossip message %A to %s"
                gossipMessage.MessageId
                recipientAddress.Value

            let peerMessage = GossipMessage gossipMessage
            let peerMessageDto = Mapping.peerMessageToDto Serialization.serializeBinary peerMessage
            let recipientMemberDto = Mapping.gossipMemberToDto recipientMember
            sendGossipMessage peerMessageDto recipientMemberDto
        | false, _ -> ()

    member private __.ProcessGossipMessage (gossipMessage : GossipMessage) recipientAddresses =
        match recipientAddresses with
        (*
            No recipients left to send message to, remove gossip message from the processing queue
        *)
        | [] -> gossipMessages.TryRemove gossipMessage.MessageId |> ignore
        (*
            If two or more recipients left, select randomly a subset (fanout) of recipients to send the
            gossip message to.
            If gossip message was processed before, append the selected recipients to the processed recipients list
            If not, add the gossip message (and the corresponding recipient) to the processing queue
        *)
        | _ ->
            let fanoutRecipientAddresses =
                recipientAddresses
                |> Seq.shuffle
                |> Seq.chunkBySize fanout
                |> Seq.head
                |> Seq.toList

            fanoutRecipientAddresses |> List.iter (fun recipientAddress ->
                __.SendGossipMessageToRecipient recipientAddress gossipMessage)

            let senderAddress = gossipMessage.SenderAddress |> optionToList
            __.UpdateGossipMessagesProcessingQueue
                (fanoutRecipientAddresses @ senderAddress)
                gossipMessage.MessageId

    member private __.SendGossipMessage message =
        let rec loop (msg : GossipMessage) =
            async {
                let recipientAddresses =
                    __.GetActiveMembers()
                    |> List.map (fun m -> m.NetworkAddress)

                let senderAddress = msg.SenderAddress |> optionToList
                let remainingrecipientAddresses =
                    match gossipMessages.TryGetValue msg.MessageId with
                    | true, processedAddresses ->
                        List.except (processedAddresses @ senderAddress) recipientAddresses
                    | false, _ ->
                        List.except senderAddress recipientAddresses

                __.ProcessGossipMessage msg remainingrecipientAddresses

                if remainingrecipientAddresses.Length >= fanout then
                    do! Async.Sleep(tCycle)
                    return! loop msg
            }

        Async.Start (loop message, cts.Token)

    member private __.ReceiveGossipMessage publishEvent (gossipMessage : GossipMessage) =
        match gossipMessages.TryGetValue gossipMessage.MessageId with
        | true, processedAddresses ->
            gossipMessage.SenderAddress |> Option.iter (fun senderAddress ->
                if not (processedAddresses |> List.contains senderAddress) then
                    let addresses = senderAddress :: processedAddresses
                    gossipMessages.AddOrUpdate(
                        gossipMessage.MessageId,
                        addresses,
                        fun _ _ -> addresses) |> ignore
            )

        | false, _ ->
            let fromMsg =
                match gossipMessage.SenderAddress with
                | Some a -> sprintf "from %s" a.Value
                | None -> ""

            Log.debugf "Received gossip message %A %s"
                gossipMessage.MessageId
                fromMsg

            // Make sure the message is not processed twice.
            gossipMessages.AddOrUpdate(
                gossipMessage.MessageId,
                [],
                fun _ _ -> []) |> ignore

            GossipMessage gossipMessage
            |> PeerMessageReceived
            |> publishEvent

            // Once a node is infected, propagate the message further.
            GossipMessage {
                MessageId = gossipMessage.MessageId
                SenderAddress = config.PublicAddress
                Data = gossipMessage.Data
            }
            |> __.SendMessage

    member private __.ReceivePeerMessage publishEvent dto =
        let peerMessage = Mapping.peerMessageFromDto dto
        match peerMessage with
        | GossipDiscoveryMessage m -> __.ReceiveMembers m
        | GossipMessage m -> __.ReceiveGossipMessage publishEvent m
        | MulticastMessage m -> __.ReceiveMulticastMessage publishEvent m
        | RequestDataMessage m -> __.ReceiveRequestMessage publishEvent m
        | ResponseDataMessage m -> __.ReceiveResponseMessage publishEvent m

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Multicast Message Passing
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.ReceiveMulticastMessage publishEvent multicastMessage =
        MulticastMessage multicastMessage
        |> PeerMessageReceived
        |> publishEvent

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Request/Response
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.ReceiveRequestMessage publishEvent (requestDataMessage : RequestDataMessage) =
        RequestDataMessage requestDataMessage
        |> PeerMessageReceived
        |> publishEvent

    member private __.ReceiveResponseMessage publishEvent (requestDataMessage : ResponseDataMessage) =
        ResponseDataMessage requestDataMessage
        |> PeerMessageReceived
        |> publishEvent

        pendingDataRequests.TryRemove requestDataMessage.MessageId |> ignore

    member private __.SelectNewUnicastPeer networkAddressPool =
        networkAddressPool
        |> List.toSeq
        |> Seq.shuffle
        |> Seq.tryHead