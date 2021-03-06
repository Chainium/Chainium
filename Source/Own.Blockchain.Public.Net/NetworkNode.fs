﻿namespace Own.Blockchain.Public.Net

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Threading
open Own.Common.FSharp
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Core.Events

type NetworkStats = {
    ReceivesGossip : bool
}

type NetworkNode
    (
    getNetworkId : _ -> NetworkId,
    getActivePeersFromDb : _ -> GossipPeerInfo list,
    getDeadPeersFromDb : _ -> GossipPeerInfo list,
    savePeerToDb : _ -> Result<unit, AppErrors>,
    removePeerFromDb : _ -> Result<unit, AppErrors>,
    resolveHostToIpAddress,
    initTransport,
    sendGossipDiscoveryMessage,
    sendGossipMessage,
    sendMulticastMessage,
    sendRequestMessage,
    sendResponseMessage,
    receiveMessage,
    closeConnection,
    closeAllConnections : _ -> unit,
    getRecentValidators : _ -> ValidatorSnapshot list,
    nodeConfig,
    gossipConfig : GossipNetworkConfig
    ) =

    let activePeers = new ConcurrentDictionary<NetworkAddress, GossipPeer>()
    let pendingDeadPeers = new ConcurrentDictionary<NetworkAddress, GossipPeer>()
    let excludedPeers = new ConcurrentDictionary<NetworkAddress, GossipPeer>()
    let peersStateMonitor = new ConcurrentDictionary<NetworkAddress, CancellationTokenSource>()

    let validatorsCache = new ConcurrentDictionary<NetworkAddress, _>()
    let deadPeersInfoCache = new ConcurrentDictionary<NetworkAddress, GossipPeerInfo>()

    let gossipMessages =
        new ConcurrentDictionary<NetworkMessageId, ConcurrentDictionary<NetworkAddress, _> * DateTime>()
    let peerSelectionSentRequests = new ConcurrentDictionary<NetworkMessageId, NetworkAddress list>()
    let mutable lastMessageReceivedTimestamp = DateTime.UtcNow
    let sentRequests = new ConcurrentDictionary<NetworkMessageId, DateTime>()
    let receivedRequests = new ConcurrentDictionary<RequestDataMessage, DateTime>()
    let sentGossipMessages = new ConcurrentDictionary<NetworkMessageId, DateTime>()
    let receivedGossipMessages = new ConcurrentDictionary<NetworkMessageId, DateTime>()

    let dnsResolverCache = new ConcurrentDictionary<string, NetworkAddress * DateTime>()

    let mutable cts = new CancellationTokenSource()

    let printActivePeers () =
        #if DEBUG
            Log.verbose "==================== ACTIVE CONNECTIONS ===================="
            activePeers
            |> Seq.ofDict
            |> Seq.sort
            |> Seq.iter (fun (a, m) -> Log.verbosef "%s Heartbeat:%i" a.Value m.Heartbeat)
            Log.verbose "============================================================"
        #else
            ()
        #endif

    let saveActivePeerToDb (peer: GossipPeer) =
        {
            NetworkAddress = peer.NetworkAddress
            SessionTimestamp = peer.SessionTimestamp
            IsDead = false
            DeadTimestamp = None
        }
        |> savePeerToDb

    let savePeerAsDead (peer : GossipPeer) =
        {
            NetworkAddress = peer.NetworkAddress
            SessionTimestamp = peer.SessionTimestamp
            IsDead = true
            DeadTimestamp = Utils.getMachineTimestamp () |> Some
        }
        |> tap (fun peerInfo ->
            deadPeersInfoCache.AddOrUpdate (
                peerInfo.NetworkAddress,
                peerInfo,
                fun _ _ -> peerInfo)
            |> ignore
        )
        |> savePeerToDb

    let removeDeadPeer peerAddress =
        peerAddress
        |> removePeerFromDb
        |> Result.handle
            (fun _ -> deadPeersInfoCache.TryRemove peerAddress |> ignore)
            Log.error

    let memoizedConvertToIpAddress (NetworkAddress networkAddress) =
        match dnsResolverCache.TryGetValue networkAddress with
        | true, (ip, _) -> Some ip
        | _ ->
            resolveHostToIpAddress networkAddress nodeConfig.AllowPrivateNetworkPeers
            |> Option.bind (fun ip ->
                let timestamp = DateTime.UtcNow
                dnsResolverCache.AddOrUpdate (networkAddress, (ip, timestamp), fun _ _ -> (ip, timestamp))
                |> fst
                |> Some
            )

    let nodeConfigPublicIPAddress =
        nodeConfig.PublicAddress
        |> Option.bind memoizedConvertToIpAddress

    let gossipPeerWithIpAddress (peer : GossipPeer) =
        peer.NetworkAddress
        |> memoizedConvertToIpAddress
        |> Option.map (fun ip -> {peer with NetworkAddress = ip})

    let isSelf peerAddress =
        nodeConfigPublicIPAddress = Some peerAddress

    let gossipPeerFromAddress peerAddress =
        match activePeers.TryGetValue peerAddress with
        | true, peer ->
            {
                NetworkAddress = peerAddress
                Heartbeat = peer.Heartbeat
                SessionTimestamp = peer.SessionTimestamp
            }
        | _ ->
            if isSelf peerAddress then
                {
                    NetworkAddress = peerAddress
                    Heartbeat = Utils.getMachineTimestamp ()
                    SessionTimestamp = gossipConfig.SessionTimestamp
                }
            else
                {
                    NetworkAddress = peerAddress
                    Heartbeat = 0L
                    SessionTimestamp = 0L
                }

    let isWhitelisted peerAddress =
        let bootstrapNodesIps =
            nodeConfig.BootstrapNodes
            |> List.choose memoizedConvertToIpAddress
            |> Set.ofList

        isSelf peerAddress
        || Set.contains peerAddress bootstrapNodesIps
        || validatorsCache.ContainsKey peerAddress

    let optionToList = function | Some x -> [x] | None -> []

    let peerMessageEnvelopeToDto = Mapping.peerMessageEnvelopeToDto Serialization.serializeBinary

    let isPendingDeadPeer (peer : GossipPeer) =
        match pendingDeadPeers.TryGetValue peer.NetworkAddress with
        | true, pendingDeadPeer ->
            Log.verbosef "Received a node with heartbeat %i - in dead-peers it has heartbeat %i"
                peer.Heartbeat
                pendingDeadPeer.Heartbeat
            pendingDeadPeer.Heartbeat >= peer.Heartbeat
        | _ -> false

    let getDeadPeerInfo peerAddress =
        match deadPeersInfoCache.TryGetValue peerAddress with
        | true, peerInfo -> Some peerInfo
        | _ -> None

    let cancelPeerStateMonitoring peerAddress =
        match peersStateMonitor.TryGetValue peerAddress with
        | true, cts ->
            cts.Cancel()
            peersStateMonitor.TryRemove peerAddress |> ignore
        | _ -> ()

    let markPeerAsDead peerAddress =
        async {
            do! Async.Sleep gossipConfig.MissedHeartbeatIntervalMillis
            let foundActive, _ = activePeers.TryGetValue peerAddress
            if not (foundActive || isWhitelisted peerAddress) then
                Log.warningf "Peer marked as DEAD %s" peerAddress.Value
                let foundDead, peer = pendingDeadPeers.TryGetValue peerAddress
                if foundDead then
                    peer
                    |> savePeerAsDead
                    |> Result.handle
                        (fun _ ->
                            pendingDeadPeers.TryRemove peerAddress |> ignore
                            peersStateMonitor.TryRemove peerAddress |> ignore
                        )
                        Log.error

                closeConnection peerAddress.Value

            else if isWhitelisted peerAddress then
                cancelPeerStateMonitoring peerAddress
        }

    let monitorPotentiallyDeadPeerState peerAddress =
        let cts =
            match peersStateMonitor.TryGetValue peerAddress with
            | true, cts -> cts
            | _ -> new CancellationTokenSource()

        peersStateMonitor.AddOrUpdate (peerAddress, cts, fun _ _ -> cts) |> ignore
        Async.Start ((markPeerAsDead peerAddress), cts.Token)

    let excludePeer networkAddress =
        async {
            do! Async.Sleep gossipConfig.MissedHeartbeatIntervalMillis
            let found, _ = activePeers.TryGetValue networkAddress
            if not found then
                Log.debugf "Peer %s has been excluded" networkAddress.Value
            else
                activePeers.TryRemove networkAddress |> ignore
                Log.debugf "Excluding %s peer" networkAddress.Value

            excludedPeers.TryRemove networkAddress |> ignore

            closeConnection networkAddress.Value

            networkAddress
            |> removePeerFromDb
            |> Result.iterError Log.error
        }

    let markPeerAsPotentiallyDead peerAddress =
        async {
            do! Async.Sleep gossipConfig.MissedHeartbeatIntervalMillis
            if (isWhitelisted peerAddress) then
                cancelPeerStateMonitoring peerAddress
            else
                Log.verbosef "Peer potentially DEAD: %s" peerAddress.Value
                match activePeers.TryGetValue peerAddress with
                | true, activePeer ->
                    activePeers.TryRemove peerAddress |> ignore
                    pendingDeadPeers.AddOrUpdate (peerAddress, activePeer, fun _ _ -> activePeer) |> ignore
                    monitorPotentiallyDeadPeerState peerAddress
                | _ -> ()
        }

    let monitorActivePeerState peerAddress =
        cancelPeerStateMonitoring peerAddress

        if not (isWhitelisted peerAddress) then
            let cts = new CancellationTokenSource()
            peersStateMonitor.AddOrUpdate (peerAddress, cts, fun _ _ -> cts) |> ignore
            Async.Start ((markPeerAsPotentiallyDead peerAddress), cts.Token)

    let updateActivePeer (peer : GossipPeer) =
        if not (excludedPeers.ContainsKey peer.NetworkAddress) then
            activePeers.AddOrUpdate (peer.NetworkAddress, peer, fun _ _ -> peer) |> ignore

    let saveActivePeer (peer : GossipPeer) =
        if not (excludedPeers.ContainsKey peer.NetworkAddress)
            && activePeers.Count < nodeConfig.MaxConnectedPeers then
            activePeers.AddOrUpdate (peer.NetworkAddress, peer, fun _ _ -> peer) |> ignore
            saveActivePeerToDb peer
        else
            Ok()

    let startCacheValidation
        validatityTimeMillis
        validationInterval
        (cache : ConcurrentDictionary<_, DateTime>)
        =

        let rec loop () =
            async {
                let lastValidTime = DateTime.UtcNow.AddMilliseconds(float (-validatityTimeMillis))
                cache
                |> List.ofDict
                |> List.filter (fun (_, fetchedAt) -> fetchedAt < lastValidTime)
                |> List.iter (fun (entry, _) ->
                    cache.TryRemove entry |> ignore
                )
                do! Async.Sleep(validationInterval)
                return! loop ()
            }

        if validatityTimeMillis > 0 then
            Async.Start (loop (), cts.Token)

    let startCachedPeerMessagesValidation (cache : ConcurrentDictionary<_, DateTime>) =
        startCacheValidation
            gossipConfig.PeerResponseThrottlingTime
            gossipConfig.GossipIntervalMillis
            cache

    let getGossipFanout () =
        Math.Max (4, gossipConfig.FanoutPercentage * validatorsCache.Count / 100 + 1)

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Public
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member __.GetListenAddress () =
        nodeConfig.ListeningAddress

    member __.GetPublicAddress () =
        nodeConfigPublicIPAddress

    member __.Identity =
        nodeConfig.Identity

    member __.GetNetworkStats () =
        let receivesGossip = lastMessageReceivedTimestamp.AddSeconds(30.) >= DateTime.UtcNow
        {
            ReceivesGossip = receivesGossip
        }

    member private __.Throttle (entries : ConcurrentDictionary<_, DateTime>) (entryBatch : _ list) func =
        let validEntries = new HashSet<_>()
        entryBatch
        |> List.iter (fun entry ->
            match entries.TryGetValue entry with
            | true, timestamp
                when timestamp > DateTime.UtcNow.AddMilliseconds(-gossipConfig.PeerResponseThrottlingTime |> float) ->
                () // Throttle the request
            | _ ->
                if gossipConfig.PeerResponseThrottlingTime > 0 then
                    let timestamp = DateTime.UtcNow
                    entries.AddOrUpdate(entry, timestamp, fun _ _ -> timestamp)
                    |> ignore
                    validEntries.Add entry |> ignore
        )

        validEntries
        |> Seq.toList
        |> (fun entries -> if not entries.IsEmpty then func entries)

    member private __.StartMonitoringSentRequests () =
        startCachedPeerMessagesValidation sentRequests

    member private __.StartMonitoringReceivedRequests () =
        startCachedPeerMessagesValidation receivedRequests

    member private __.StartMonitoringSentGossipMessages () =
        startCachedPeerMessagesValidation sentGossipMessages

    member private __.StartMonitoringReceivedGossipMessages () =
        startCachedPeerMessagesValidation receivedGossipMessages

    member __.StartGossip publishEvent =
        cts <- new CancellationTokenSource()
        Log.debugf "Node identity is %s" (Conversion.bytesToString nodeConfig.Identity.Value)
        let networkId = getNetworkId ()
        initTransport
            networkId.Value
            nodeConfig.Identity.Value
            nodeConfig.NetworkSendoutRetryTimeout
            nodeConfig.PeerMessageMaxSize
            (__.ReceivePeerMessage publishEvent)
        __.StartNode ()
        __.StartGossipDiscovery ()
        Log.info "Network layer initialized"

    member __.StopGossip () =
        cts.Cancel()
        closeAllConnections ()

    member __.GetActivePeers () =
        let result =
            activePeers
            |> List.ofDict
            |> List.map (fun (_, m) -> m)

        // Fallback to boostrapnodes when no peers available.
        match result with
        | [] | [_] ->
            __.AddBootstrapNodesAsPeers ()
        | _ -> ()

        result |> List.choose gossipPeerWithIpAddress

    member __.ReceivePeers msg =
        // Keep max allowed peers.
        let receivedPeers =
            msg.ActivePeers
            |> List.shuffle
            |> List.truncate nodeConfig.MaxConnectedPeers
            |> List.choose gossipPeerWithIpAddress

        // Filter the existing peers, if any, (used to refresh connection, i.e increase heartbeat).
        let existingPeers =
            receivedPeers
            |> Set.ofList
            |> Set.intersect (__.GetActivePeers() |> Set.ofList)
            |> Set.toList

        let activePeersCount = __.GetActivePeers().Length

        // Max peers count reached.
        if activePeersCount >= nodeConfig.MaxConnectedPeers then
            // Exclude non-whitelisted peer.
            __.GetActivePeers()
            |> List.shuffle
            |> List.filter (fun peer -> not (isWhitelisted peer.NetworkAddress))
            |> List.tryHead
            |> Option.iter (fun peer ->
                // Shallow exclusion.
                activePeers.TryRemove peer.NetworkAddress |> ignore
                excludedPeers.AddOrUpdate(peer.NetworkAddress, peer, fun _ _ -> peer) |> ignore
                // Deep exclusion.
                let cts = new CancellationTokenSource()
                Async.Start ((excludePeer peer.NetworkAddress), cts.Token)
            )

        // If active peers count reached max, onboard a new peer, otherwise onboard up to max.
        let take = Math.Max(1, nodeConfig.MaxConnectedPeers - activePeersCount)
        receivedPeers
        |> List.except existingPeers
        |> List.except excludedPeers.Values
        |> List.shuffle
        |> List.truncate take
        |> List.append existingPeers
        |> __.MergePeerList

    member __.SendMessage message =
        let sendMessageTask =
            async {
                match message.PeerMessage with
                | GossipDiscoveryMessage _ ->
                    __.SelectRandomPeers()
                    |> List.iter (fun m ->
                        Log.verbosef "Sending peerlist to: %s" m.NetworkAddress.Value
                        message
                        |> peerMessageEnvelopeToDto
                        |> sendGossipDiscoveryMessage m.NetworkAddress.Value
                    )

                | MulticastMessage _ ->
                    getRecentValidators ()
                    |> List.choose (fun v -> memoizedConvertToIpAddress v.NetworkAddress)
                    |> List.filter (isSelf >> not)
                    |> List.map (fun a -> a.Value)
                    |> fun multicastAddresses ->
                        message
                        |> peerMessageEnvelopeToDto
                        |> sendMulticastMessage multicastAddresses

                | GossipMessage m -> __.Throttle sentGossipMessages [ m.MessageId ] (fun _ -> __.SendGossipMessage m)

                | _ -> ()
            }
        Async.Start (sendMessageTask, cts.Token)

    member __.IsRequestPending requestId =
        peerSelectionSentRequests.ContainsKey requestId

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Gossip Discovery
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.StartCachingValidators () =
        let rec loop () =
            async {
                let newValidators =
                    getRecentValidators ()
                    |> List.choose (fun v -> memoizedConvertToIpAddress v.NetworkAddress)

                // Remove old validators.
                validatorsCache
                |> List.ofDict
                |> List.filter (fun (v, _) -> not (newValidators |> List.contains v))
                |> List.iter (fun (v, _) -> validatorsCache.TryRemove v |> ignore)

                // Add new validators.
                newValidators
                |> List.filter (validatorsCache.ContainsKey >> not)
                |> List.iter (fun v -> validatorsCache.AddOrUpdate (v, None, fun _ _ -> None) |> ignore)

                do! Async.Sleep(30_000)
                return! loop ()
            }
        Async.Start (loop (), cts.Token)

        member private __.StartMonitoringDeadPeers () =
            let rec loop () =
                async {
                    let newDeadPeers = getDeadPeersFromDb ()
                    let lastValidTimestamp =
                        DateTimeOffset.UtcNow
                            .AddHours(-float (nodeConfig.DeadPeerExpirationTime))
                            .ToUnixTimeMilliseconds()

                    // Remove expired dead peers.
                    deadPeersInfoCache
                    |> List.ofDict
                    |> List.filter (fun (_, p) -> p.DeadTimestamp.Value < lastValidTimestamp)
                    |> List.iter (fun (peerAddress, _) -> removeDeadPeer peerAddress)

                    // Remove old dead peers.
                    deadPeersInfoCache
                    |> List.ofDict
                    |> List.filter (fun (_, p) -> not (newDeadPeers |> List.contains p))
                    |> List.iter (fun (a, _) -> deadPeersInfoCache.TryRemove a |> ignore)

                    // Add new dead peers.
                    newDeadPeers
                    |> List.filter (fun p -> not (deadPeersInfoCache.ContainsKey p.NetworkAddress))
                    |> List.iter (fun p ->
                        deadPeersInfoCache.AddOrUpdate (p.NetworkAddress, p, fun _ _ -> p) |> ignore
                    )

                    do! Async.Sleep(600_000) // Every 10 minutes
                    return! loop ()
                }
            Async.Start (loop (), cts.Token)

        member private __.StartMonitoringGossipMessages () =
            let rec loop () =
                async {
                    // Wait 4x the time needed for a message to be propagated to all peers.
                    let propagationCycles = 100 / getGossipFanout ()
                    let gossipMessageExpirationTime = 4 * propagationCycles * gossipConfig.GossipIntervalMillis
                    let lastValidTimestamp = DateTime.UtcNow.AddMilliseconds (-float gossipMessageExpirationTime)

                    gossipMessages
                    |> List.ofDict
                    |> List.filter (fun (_, (_, fetchedAt)) -> fetchedAt < lastValidTimestamp)
                    |> List.iter (fun (id, _) ->
                        gossipMessages.TryRemove id |> ignore
                    )

                    do! Async.Sleep 1000
                    return! loop ()
                }

            Async.Start (loop (), cts.Token)

    member private __.StartDnsResolver () =
        let rec loop () =
            async {
                let lastValidTime = DateTime.UtcNow.AddSeconds(-float nodeConfig.DnsResolverCacheExpirationTime)
                dnsResolverCache
                |> List.ofDict
                |> List.filter (fun (_, (_, fetchedAt)) -> fetchedAt < lastValidTime)
                |> List.iter (fun (dns, _) ->
                    dnsResolverCache.TryRemove dns |> ignore
                )
                do! Async.Sleep(gossipConfig.GossipDiscoveryIntervalMillis)
                return! loop ()
            }
        Async.Start (loop (), cts.Token)

    member private __.StartNode () =
        Log.debug "Starting node..."
        __.InitializePeerList ()
        __.StartDnsResolver ()
        __.StartCachingValidators ()
        __.StartMonitoringDeadPeers ()
        __.StartMonitoringGossipMessages ()
        __.StartMonitoringSentRequests ()
        __.StartMonitoringReceivedRequests ()
        __.StartMonitoringSentGossipMessages ()
        __.StartMonitoringReceivedGossipMessages ()
        __.StartServer ()

    member private __.StartServer () =
        Log.infof "Listening to peers on: %s" nodeConfig.ListeningAddress.Value
        nodeConfigPublicIPAddress |> Option.iter (fun a -> Log.infof "Public address: %s" a.Value)
        receiveMessage nodeConfig.ListeningAddress.Value

    member private __.StartGossipDiscovery () =
        let rec loop () =
            async {
                __.Discover ()
                do! Async.Sleep(gossipConfig.GossipDiscoveryIntervalMillis)
                return! loop ()
            }
        Async.Start (loop (), cts.Token)

    member private __.Discover () =
        __.IncreaseHeartbeat()
        match nodeConfig.PublicAddress with
        | Some address ->
            address
            |> gossipPeerFromAddress
            |> fun self ->
                {
                    PeerMessageEnvelope.NetworkId = getNetworkId ()
                    PeerMessage = GossipDiscoveryMessage { ActivePeers = self :: __.GetActivePeers() }
                }
                |> __.SendMessage

        | None -> __.SendRequestDataMessage [ NetworkMessageId.PeerList ] None

        printActivePeers ()

    member private __.InitializePeerList () =
        let publicAddress = optionToList nodeConfigPublicIPAddress

        let peersFromDb =
            getActivePeersFromDb ()
            |> List.map (fun peerInfo ->
                if isSelf peerInfo.NetworkAddress then
                    Utils.getMachineTimestamp (), gossipConfig.SessionTimestamp
                else
                    0L, peerInfo.SessionTimestamp
                |> fun (heartbeat, sessionTimestamp) ->
                    {
                        NetworkAddress = peerInfo.NetworkAddress
                        Heartbeat = heartbeat
                        SessionTimestamp = sessionTimestamp
                    }
            )

        nodeConfig.BootstrapNodes @ publicAddress
        |> List.map gossipPeerFromAddress
        |> List.append peersFromDb
        |> __.AddPeersWithIps

    member private __.AddBootstrapNodesAsPeers () =
        let publicAddress = optionToList nodeConfigPublicIPAddress
        nodeConfig.BootstrapNodes @ publicAddress
        |> List.map gossipPeerFromAddress
        |> __.AddPeersWithIps

    member private __.AddPeersWithIps (peers : GossipPeer list) =
        peers
        |> List.choose (fun peer ->
            peer.NetworkAddress
            |> memoizedConvertToIpAddress
            |> Option.map (fun ip -> {peer with NetworkAddress = ip})
        )
        |> List.distinctBy (fun peer -> peer.NetworkAddress)
        |> Set.ofList
        |> Set.iter __.AddPeer

    member private __.AddPeer (peer : GossipPeer) =
        let found, _ = activePeers.TryGetValue peer.NetworkAddress
        if not found then
            peer.NetworkAddress
            |> getDeadPeerInfo
            |> function
                | Some localDeadPeer ->
                    // Ignore dead peer update if heartbeat old.
                    if localDeadPeer.DeadTimestamp >= Some peer.Heartbeat then
                        Log.verbosef "Received DEAD peer %s with Timestamp = %i"
                            peer.NetworkAddress.Value
                            peer.Heartbeat
                        ()

                    // Remove dead peer if hearbeat or session newer.
                    if localDeadPeer.DeadTimestamp.Value < peer.Heartbeat
                        || localDeadPeer.SessionTimestamp < peer.SessionTimestamp
                    then
                        Log.verbosef "DEAD peer %A received valid update %A" localDeadPeer peer
                        removeDeadPeer localDeadPeer.NetworkAddress
                | _ ->
                    // Add peer if not found (active or dead).
                    Log.verbosef "Adding new peer: %s" peer.NetworkAddress.Value
                    peer
                    |> saveActivePeer
                    |> Result.iterError Log.appErrors

                    if not (isSelf peer.NetworkAddress) then
                        monitorActivePeerState peer.NetworkAddress

    member private __.UpdatePeer (peer : GossipPeer) =
        __.GetActivePeer peer.NetworkAddress
        |> Option.iter (fun _ ->
            updateActivePeer peer
            monitorActivePeerState peer.NetworkAddress
        )

    member private __.MergePeerList peers =
        peers |> List.iter __.MergePeer

    member private __.MergePeer peer =
        if not (isSelf peer.NetworkAddress) then
            Log.verbosef "Received peer %s with heartbeat %i"
                peer.NetworkAddress.Value
                peer.Heartbeat

            match __.GetActivePeer peer.NetworkAddress with
            | Some localPeer ->
                if localPeer.SessionTimestamp = peer.SessionTimestamp
                    && localPeer.Heartbeat < peer.Heartbeat
                then
                    // Update the heartbeat.
                    __.UpdatePeer { localPeer with Heartbeat = peer.Heartbeat }

                elif localPeer.SessionTimestamp < peer.SessionTimestamp then
                    Log.debugf "Peer %s has been restarted" peer.NetworkAddress.Value
                    // Update heartbeat and session.
                    __.UpdatePeer peer

                    // Persist session to DB
                    peer
                    |> saveActivePeer
                    |> Result.iterError Log.error

            | None ->
                if not (isPendingDeadPeer peer) then
                    __.AddPeer peer
                    pendingDeadPeers.TryRemove peer.NetworkAddress |> ignore

    member private __.GetActivePeer networkAddress : GossipPeer option =
        __.GetActivePeers() |> List.tryFind (fun m -> m.NetworkAddress = networkAddress)

    member private __.IncreaseHeartbeat () =
        nodeConfigPublicIPAddress
        |> Option.map gossipPeerFromAddress
        |> Option.iter (fun peer ->
            { peer with Heartbeat = Utils.getMachineTimestamp () }
            |> updateActivePeer
        )

    member private __.SelectRandomPeers () : GossipPeer list =
        __.GetActivePeers()
        |> List.filter (fun peer -> not (isSelf peer.NetworkAddress))
        |> List.shuffle
        |> List.truncate (getGossipFanout ())

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Gossip Message Passing
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.UpdateGossipMessagesProcessingQueue networkAddresses gossipMessageId =
        match gossipMessages.TryGetValue gossipMessageId with
        | true, (processedAddresses, _) ->
            networkAddresses
            |> List.iter (fun a -> processedAddresses.TryAdd (a, None) |> ignore)
        | _ ->
            let dict = new ConcurrentDictionary<NetworkAddress, _>()
            networkAddresses |> List.iter (fun a -> dict.TryAdd (a, None) |> ignore)
            gossipMessages.TryAdd (gossipMessageId, (dict, DateTime.UtcNow)) |> ignore

    member private __.SendGossipMessageToRecipient recipientAddress (gossipMessage : GossipMessage) =
        match activePeers.TryGetValue recipientAddress with
        | true, targetPeer ->
            Log.verbosef "Sending gossip message %A to %s"
                gossipMessage.MessageId
                recipientAddress.Value

            {
                PeerMessageEnvelope.NetworkId = getNetworkId ()
                PeerMessage = GossipMessage gossipMessage
            }
            |> peerMessageEnvelopeToDto
            |> sendGossipMessage targetPeer.NetworkAddress.Value
        | _ -> ()

    member private __.ProcessGossipMessage (gossipMessage : GossipMessage) recipientAddresses =
        let recipientAddresses = recipientAddresses |> Set.toList
        match recipientAddresses with
        | [] -> gossipMessages.TryRemove gossipMessage.MessageId |> ignore
        | _ ->
            let fanoutRecipientAddresses =
                recipientAddresses
                |> List.shuffle
                |> List.truncate (getGossipFanout ())

            fanoutRecipientAddresses
            |> List.iter (fun recipientAddress ->
                __.SendGossipMessageToRecipient recipientAddress gossipMessage)

            let senderAddress = optionToList gossipMessage.SenderAddress
            __.UpdateGossipMessagesProcessingQueue
                (fanoutRecipientAddresses @ senderAddress)
                gossipMessage.MessageId

    member private __.SendGossipMessage message =
        let gossipFanout = getGossipFanout ()

        let rec loop (msg : GossipMessage) =
            async {
                let senderAddress = optionToList msg.SenderAddress
                let processedAddresses =
                    match gossipMessages.TryGetValue msg.MessageId with
                    | true, (processedAddresses, _) ->
                        (processedAddresses.Keys |> Seq.toList) @ senderAddress
                    | _ -> senderAddress
                    |> Set.ofList

                let remainingRecipientAddresses =
                    __.GetActivePeers()
                    |> List.map (fun m -> m.NetworkAddress)
                    |> List.except processedAddresses
                    |> List.filter (isSelf >> not)
                    |> Set.ofList

                __.ProcessGossipMessage msg remainingRecipientAddresses

                if remainingRecipientAddresses.Count >= gossipFanout then
                    do! Async.Sleep(gossipConfig.GossipIntervalMillis)
                    return! loop msg
            }

        Async.Start (loop message, cts.Token)

    member private __.ReceiveGossipMessage publishEvent (gossipMessage : GossipMessage) =
        match gossipMessages.TryGetValue gossipMessage.MessageId with
        | true, (processedAddresses, _) ->
            gossipMessage.SenderAddress
            |> Option.bind memoizedConvertToIpAddress
            |> Option.iter (fun senderAddress ->
                let found, _ = processedAddresses.TryGetValue senderAddress
                if not found then
                    processedAddresses.TryAdd (senderAddress, None) |> ignore
            )

        | false, _ ->
            let fromMsg =
                match gossipMessage.SenderAddress with
                | Some a -> sprintf "from %s" a.Value
                | None -> ""

            Log.verbosef "Received gossip message %A %s"
                gossipMessage.MessageId
                fromMsg

            // Make sure the message is not processed twice.
            let emptyRecipientAddresses = (new ConcurrentDictionary<NetworkAddress, _>(), DateTime.UtcNow)
            gossipMessages.AddOrUpdate(
                gossipMessage.MessageId,
                emptyRecipientAddresses,
                fun _ _ -> emptyRecipientAddresses) |> ignore

            let peerMessageReceivedEvent =
                {
                    PeerMessageEnvelope.NetworkId = getNetworkId ()
                    PeerMessage = GossipMessage gossipMessage
                }
                |> PeerMessageReceived

            // Throttle received gossip messages.
            __.Throttle receivedGossipMessages [ gossipMessage.MessageId ] (fun _ ->
                publishEvent peerMessageReceivedEvent
            )

    member private __.ReceivePeerMessage publishEvent dto =
        try
            let peerMessageEnvelope = Mapping.peerMessageEnvelopeFromDto Serialization.deserializePeerMessage dto
            match peerMessageEnvelope.PeerMessage with
            | GossipDiscoveryMessage m ->
                lastMessageReceivedTimestamp <- DateTime.UtcNow
                __.ReceivePeers m
            | GossipMessage m ->
                lastMessageReceivedTimestamp <- DateTime.UtcNow
                __.ReceiveGossipMessage publishEvent m
            | MulticastMessage m ->
                lastMessageReceivedTimestamp <- DateTime.UtcNow
                __.ReceiveMulticastMessage publishEvent m
            | RequestDataMessage m -> __.ReceiveRequestMessage publishEvent m
            | ResponseDataMessage m -> __.ReceiveResponseMessage publishEvent m
        with
        | ex when ex.Message.Contains("code is invalid") ->
            Log.warningf "Cannot deserialize peer message"
            Log.debug ex.AllMessagesAndStackTraces
        | ex ->
            Log.warning ex.AllMessages
            Log.debug ex.AllMessagesAndStackTraces

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Multicast Message Passing
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member private __.ReceiveMulticastMessage publishEvent multicastMessage =
        {
            PeerMessageEnvelope.NetworkId = getNetworkId ()
            PeerMessage = MulticastMessage multicastMessage
        }
        |> PeerMessageReceived
        |> publishEvent

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Request/Response
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    member __.SendRequestDataMessage requestIds (preferredPeer : NetworkAddress option) =
        __.Throttle sentRequests requestIds (fun validRequestIds ->
            Stats.increment Stats.Counter.PeerRequests
            Log.debugf "Sending request for %A" validRequestIds
            let rec loop messageIds preferredPeer =
                async {

                    let usedAddresses = new HashSet<NetworkAddress>()
                    messageIds
                    |> List.iter (fun messageId ->
                        match messageId with
                        | Tx _ -> Stats.increment Stats.Counter.RequestedTxs
                        | Block _ -> Stats.increment Stats.Counter.RequestedBlocks
                        | _ -> ()

                        match peerSelectionSentRequests.TryGetValue messageId with
                        | true, addresses -> usedAddresses.UnionWith(addresses)
                        | _ -> ()
                    )

                    let targetedAddresses = usedAddresses |> Seq.toList
                    let selectedPeer =
                        __.GetActivePeers()
                        |> List.map (fun m -> m.NetworkAddress)
                        |> List.except targetedAddresses
                        |> List.filter (isSelf >> not)
                        |> __.SelectPeer preferredPeer
                        |> tap (fun address ->
                            messageIds
                            |> List.iter (fun messageId ->
                                match address with
                                | Some networkAddress ->
                                    peerSelectionSentRequests.AddOrUpdate(
                                        messageId,
                                        networkAddress :: targetedAddresses,
                                        fun _ _ -> networkAddress :: targetedAddresses)
                                    |> ignore
                                | None ->
                                    Stats.increment Stats.Counter.PeerRequestFailures
                                    Log.errorf "Cannot retrieve data from peers for %A" messageId
                                    peerSelectionSentRequests.TryRemove messageId |> ignore
                                )
                        )
                    match selectedPeer with
                    | Some address ->
                        {
                            PeerMessageEnvelope.NetworkId = getNetworkId ()
                            PeerMessage =
                                {
                                    Items = validRequestIds
                                    SenderIdentity = nodeConfig.Identity
                                }
                                |> RequestDataMessage
                        }
                        |> peerMessageEnvelopeToDto
                        |> sendRequestMessage address.Value

                        do! Async.Sleep(4 * gossipConfig.GossipIntervalMillis)

                        (*
                            If no answer is received within 2 cycles (request - response i.e 4xtCycle),
                            repeat (i.e choose another peer).
                        *)
                        let requestsWithNoReplies =
                            messageIds
                            |> List.choose (fun messageId ->
                                match (peerSelectionSentRequests.TryGetValue messageId) with
                                | true, addresses when not (addresses.IsEmpty) -> Some messageId
                                | _ -> None
                            )

                        if not requestsWithNoReplies.IsEmpty then
                            Stats.increment Stats.Counter.PeerRequestTimeouts
                            return! loop requestsWithNoReplies None
                    | None -> ()
                }

            let preferredPeerIp = preferredPeer |> Option.bind memoizedConvertToIpAddress
            Async.Start (loop validRequestIds preferredPeerIp, cts.Token)
        )

    member __.SendResponseDataMessage (targetIdentity : PeerNetworkIdentity) peerMessageEnvelope =
        Stats.increment Stats.Counter.PeerResponses
        match peerMessageEnvelope.PeerMessage with
        | ResponseDataMessage responseMessage ->
            Log.debugf "Sending response (to %A request) to %s"
                responseMessage.Items.Head.MessageId
                (Conversion.bytesToString targetIdentity.Value)

            let sendResponse =
                async {
                    peerMessageEnvelope
                    |> peerMessageEnvelopeToDto
                    |> sendResponseMessage targetIdentity.Value
                }
            Async.Start (sendResponse, cts.Token)
        | _ -> ()

    member private __.ReceiveRequestMessage publishEvent (requestDataMessage : RequestDataMessage) =
        __.Throttle receivedRequests [ requestDataMessage ] (fun _ ->
            Log.debugf "Received request for %A from %s"
                requestDataMessage.Items.Head
                (Conversion.bytesToString requestDataMessage.SenderIdentity.Value)
            {
                PeerMessageEnvelope.NetworkId = getNetworkId ()
                PeerMessage = RequestDataMessage requestDataMessage
            }
            |> PeerMessageReceived
            |> publishEvent
        )

    member private __.ReceiveResponseMessage publishEvent (responseDataMessage : ResponseDataMessage) =
        Log.debugf "Received response to %A request" responseDataMessage.Items.Head.MessageId
        {
            PeerMessageEnvelope.NetworkId = getNetworkId ()
            PeerMessage = ResponseDataMessage responseDataMessage
        }
        |> PeerMessageReceived
        |> publishEvent

        responseDataMessage.Items
        |> List.iter (fun response -> peerSelectionSentRequests.TryRemove response.MessageId |> ignore)

    member private __.SelectPeer preferredPeer peerList =
        match preferredPeer with
        | Some _ -> preferredPeer
        | None ->
            peerList
            |> List.shuffle
            |> List.tryHead
