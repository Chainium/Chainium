﻿namespace Own.Blockchain.Public.Core

open System
open Own.Common.FSharp
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Core.Dtos
open Own.Blockchain.Public.Core.Events

module Workflows =

    let getDetailedChxBalance
        getChxAddressState
        (getTotalChxStaked : BlockchainAddress -> ChxAmount)
        getValidatorState
        validatorDeposit
        senderAddress
        : DetailedChxBalanceDto
        =

        let chxBalance =
            senderAddress
            |> getChxAddressState
            |> Option.map (Mapping.chxAddressStateFromDto >> fun state -> state.Balance)
            |? ChxAmount 0m

        let chxStaked = getTotalChxStaked senderAddress

        let validatorDeposit =
            getValidatorState senderAddress
            |> Option.map Mapping.validatorStateFromDto
            |> Option.map (fun _ -> validatorDeposit)
            |? ChxAmount 0m
            |> min (chxBalance - chxStaked)

        {
            Total = chxBalance.Value
            Staked = chxStaked.Value
            Deposit = validatorDeposit.Value
            Available = (chxBalance - chxStaked - validatorDeposit).Value
        }

    let getAvailableChxBalance
        getChxAddressState
        getTotalChxStaked
        getValidatorState
        validatorDeposit
        senderAddress
        : ChxAmount
        =

        let detailedBalance =
            getDetailedChxBalance
                getChxAddressState
                getTotalChxStaked
                getValidatorState
                validatorDeposit
                senderAddress

        ChxAmount detailedBalance.Available

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Blockchain
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let createGenesisBlock
        decodeHash
        createHash
        createMerkleTree
        zeroHash
        zeroAddress
        genesisChxSupply
        genesisAddress
        genesisValidators
        configurationBlockDelta
        validatorDepositLockTime
        validatorBlacklistTime
        maxTxCountPerBlock
        =

        let genesisValidators =
            genesisValidators
            |> List.map (fun (ba, na) ->
                BlockchainAddress ba,
                    (
                        {
                            ValidatorState.NetworkAddress = NetworkAddress na
                            SharedRewardPercent = 0m
                            TimeToLockDeposit = validatorDepositLockTime
                            TimeToBlacklist = 0s
                            IsEnabled = true
                            LastProposedBlockNumber = None
                            LastProposedBlockTimestamp = None
                        },
                        ValidatorChange.Add
                    )
            )
            |> Map.ofList

        let genesisState = Blocks.createGenesisState genesisChxSupply genesisAddress genesisValidators

        let genesisBlock =
            Blocks.assembleGenesisBlock
                decodeHash
                createHash
                createMerkleTree
                zeroHash
                zeroAddress
                configurationBlockDelta
                validatorDepositLockTime
                validatorBlacklistTime
                maxTxCountPerBlock
                genesisState

        genesisBlock, genesisState

    let signGenesisBlock
        (createGenesisBlock : unit -> Block * ProcessingOutput)
        createConsensusMessageHash
        signHash
        privateKey
        : Signature
        =

        let block, _ = createGenesisBlock ()

        createConsensusMessageHash
            block.Header.Number
            (ConsensusRound 0)
            (block.Header.Hash |> Some |> Commit)
        |> signHash privateKey

    let initBlockchainState
        (tryGetLastAppliedBlockNumber : unit -> BlockNumber option)
        (createGenesisBlock : unit -> Block * ProcessingOutput)
        (getBlock : BlockNumber -> Result<BlockEnvelopeDto, AppErrors>)
        saveBlock
        saveBlockToDb
        persistStateChanges
        createConsensusMessageHash
        verifySignature
        genesisSignatures
        =

        if tryGetLastAppliedBlockNumber () = None then
            let genesisBlock, genesisState = createGenesisBlock ()
            let genesisBlockFromDisk =
                getBlock genesisBlock.Header.Number
                |> Result.map Blocks.extractBlockFromEnvelopeDto

            let genesisBlockExists =
                match genesisBlockFromDisk with
                | Ok blockFromDisk ->
                    if blockFromDisk <> genesisBlock then
                        failwith "Stored genesis block is invalid"
                    true
                | Error _ -> false

            let result =
                result {
                    let blockEnvelopeDto =
                        {
                            Block = genesisBlock |> Mapping.blockToDto
                            ConsensusRound = 0
                            Signatures = genesisSignatures
                        }

                    let! genesisSigners =
                        blockEnvelopeDto
                        |> Mapping.blockEnvelopeFromDto
                        |> Blocks.verifyBlockSignatures createConsensusMessageHash verifySignature

                    match genesisBlock.Configuration with
                    | None -> return! Result.appError "Genesis block must have configuration"
                    | Some c ->
                        let validators = c.Validators |> List.map (fun v -> v.ValidatorAddress)
                        if (set validators) <> (set genesisSigners) then
                            return! Result.appError "Genesis signatures don't match genesis validators"

                    if not genesisBlockExists then
                        do! saveBlock genesisBlock.Header.Number blockEnvelopeDto

                    do! genesisBlock.Header
                        |> Mapping.blockHeaderToBlockInfoDto (genesisBlock.Configuration <> None)
                        |> saveBlockToDb

                    do! genesisState
                        |> Mapping.outputToDto genesisBlock.Header.Number
                        |> persistStateChanges
                            genesisBlock.Header.Number
                            genesisBlock.Header.Timestamp
                            genesisBlock.Header.ProposerAddress
                }

            match result with
            | Ok _ ->
                Log.info "Blockchain state initialized"
            | Error errors ->
                Log.appErrors errors
                failwith "Cannot initialize blockchain state"

    let rebuildBlockchainState
        (getLastAppliedBlockNumber : unit -> BlockNumber)
        (getLastStoredBlockNumber : unit -> BlockNumber option)
        getBlock
        saveBlockToDb
        getTx
        saveTxToDb
        txExists
        txExistsInDb
        txResultExists
        getTxResult
        deleteTxResult
        getEquivocationProof
        saveEquivocationProofToDb
        equivocationProofExists
        equivocationProofExistsInDb
        equivocationProofResultExists
        getEquivocationProofResult
        deleteEquivocationProofResult
        createConsensusMessageHash
        decodeHash
        createHash
        verifySignature
        isValidHash
        isValidAddress
        maxActionCountPerTx
        =

        let logOnce = memoize Log.info

        let lastAppliedBlockNumber = getLastAppliedBlockNumber ()
        let lastStoredBlockNumber = getLastStoredBlockNumber () |? lastAppliedBlockNumber

        lastAppliedBlockNumber + 1
        |> Seq.unfold (fun n ->
            getBlock n
            |> Result.map Blocks.extractBlockFromEnvelopeDto
            |> function
                | Ok b ->
                    logOnce "Rebuilding blockchain state..."
                    Some (b, n + 1)
                | _ ->
                    None
        )
        |> Seq.iter (fun block ->
            block.Header
            |> Mapping.blockHeaderToBlockInfoDto block.Configuration.IsSome
            |> (fun blockInfo ->
                Log.infof "Restoring info for block %i" block.Header.Number.Value
                (
                    if block.Header.Number > lastStoredBlockNumber then
                        saveBlockToDb blockInfo
                    else
                        Ok ()
                )
                >>= (fun _ ->
                    result {
                        for txHash in block.TxSet do
                            if txExists txHash then
                                Log.debugf "Loading info for TX %s" txHash.Value
                                Processing.getTxBody
                                    getTx
                                    createHash
                                    verifySignature
                                    isValidHash
                                    isValidAddress
                                    maxActionCountPerTx
                                    txHash
                                >>= (fun tx ->
                                    if not (txExistsInDb txHash) then
                                        Log.debugf "Saving info for TX %s" txHash.Value
                                        tx |> Mapping.txToTxInfoDto |> saveTxToDb true
                                    else
                                        Ok ()
                                )
                                >>= (fun _ ->
                                    if txResultExists txHash then
                                        getTxResult txHash
                                        |> Result.map Mapping.txResultFromDto
                                        |> Result.bind (fun txResult ->
                                            if txResult.BlockNumber <= lastAppliedBlockNumber then
                                                failwithf "Cannot delete TX %s result - it belongs to applied block %i"
                                                    txHash.Value
                                                    txResult.BlockNumber.Value
                                            else
                                                if txResult.BlockNumber <> block.Header.Number then
                                                    Log.warningf "Block %i contains TX %s with result pointing to %i"
                                                        block.Header.Number.Value
                                                        txHash.Value
                                                        txResult.BlockNumber.Value
                                                Log.debugf "Deleting TX result %s" txHash.Value
                                                deleteTxResult txHash
                                        )
                                    else
                                        Ok ()
                                )
                                |> Result.iterError (fun e ->
                                    Log.appErrors e
                                    failwithf "Failed rebuilding the state - cannot restore TX info: %s"
                                        txHash.Value
                                )

                        for eqHash in block.EquivocationProofs do
                            if equivocationProofExists eqHash then
                                Log.debugf "Loading info for equivocation proof %s" eqHash.Value
                                getEquivocationProof eqHash
                                >>= Validation.validateEquivocationProof
                                    verifySignature
                                    createConsensusMessageHash
                                    decodeHash
                                    createHash
                                >>= (fun proof ->
                                    if not (equivocationProofExistsInDb eqHash) then
                                        Log.debugf "Saving info for equivocation proof %s" eqHash.Value
                                        proof
                                        |> Mapping.equivocationProofToEquivocationInfoDto
                                        |> saveEquivocationProofToDb
                                    else
                                        Ok ()
                                )
                                >>= (fun _ ->
                                    if equivocationProofResultExists eqHash then
                                        getEquivocationProofResult eqHash
                                        |> Result.map Mapping.equivocationProofResultFromDto
                                        |> Result.bind (fun eqResult ->
                                            if eqResult.BlockNumber <= lastAppliedBlockNumber then
                                                failwithf "Cannot delete EQ %s result - it belongs to applied block %i"
                                                    eqHash.Value
                                                    eqResult.BlockNumber.Value
                                            else
                                                if eqResult.BlockNumber <> block.Header.Number then
                                                    Log.warningf "Block %i contains EQ %s with result pointing to %i"
                                                        block.Header.Number.Value
                                                        eqHash.Value
                                                        eqResult.BlockNumber.Value
                                                Log.debugf "Deleting equivocation proof result %s" eqHash.Value
                                                deleteEquivocationProofResult eqHash
                                        )
                                    else
                                        Ok ()
                                )
                                |> Result.iterError (fun e ->
                                    Log.appErrors e
                                    failwithf "Failed rebuilding the state - cannot restore equivocation proof info: %s"
                                        eqHash.Value
                                )
                    }
                )
            )
            |> Result.iterError (fun e ->
                Log.appErrors e
                failwithf "Failed rebuilding the state - cannot restore info for block %i" block.Header.Number.Value
            )
        )

    let deleteTxsBelowMinFee
        (deleteTxsBelowFee : ChxAmount -> TxHash list)
        txResultExists
        deleteTx
        minTxActionFee
        =

        deleteTxsBelowFee minTxActionFee
        |> List.iter (fun txHash ->
            if txResultExists txHash then
                failwithf "Cannot delete TX with existing result: %s" txHash.Value

            deleteTx txHash
            |> Result.iterError Log.appErrors
        )

    let createBlock
        getTx
        getEquivocationProof
        verifySignature
        isValidHash
        isValidAddress
        getChxAddressStateFromStorage
        getHoldingStateFromStorage
        getVoteStateFromStorage
        getEligibilityStateFromStorage
        getAssetKycProvidersFromStorage
        getAccountStateFromStorage
        getAssetStateFromStorage
        getAssetHashByCodeFromStorage
        getValidatorStateFromStorage
        getStakeStateFromStorage
        getStakersFromStorage
        getTotalChxStakedFromStorage
        getTopStakersFromStorage
        getTradingPairControllersFromStorage
        getTradingPairStateFromStorage
        getTradeOrderStateFromStorage
        getTradeOrdersFromStorage
        getExpiredTradeOrdersFromStorage
        getIneligibleTradeOrdersFromStorage
        getHoldingInTradeOrdersFromStorage
        (getValidatorsAtHeight : BlockNumber -> ValidatorSnapshot list)
        getLockedAndBlacklistedValidators
        deriveHash
        decodeHash
        createHash
        createConsensusMessageHash
        createMerkleTree
        maxActionCountPerTx
        validatorDeposit
        validatorAddress
        (previousBlockHash : BlockHash)
        (blockNumber : BlockNumber)
        timestamp
        configurationBlockNumber
        validatorDepositLockTime
        validatorBlacklistTime
        txSet
        equivocationProofs
        blockchainConfiguration
        =

        let getChxAddressState = memoize (getChxAddressStateFromStorage >> Option.map Mapping.chxAddressStateFromDto)
        let getHoldingState = memoize (getHoldingStateFromStorage >> Option.map Mapping.holdingStateFromDto)
        let getVoteState = memoize (getVoteStateFromStorage >> Option.map Mapping.voteStateFromDto)
        let getEligibilityState = memoize (getEligibilityStateFromStorage >> Option.map Mapping.eligibilityStateFromDto)
        let getKycProviders = memoize getAssetKycProvidersFromStorage
        let getAccountState = memoize (getAccountStateFromStorage >> Option.map Mapping.accountStateFromDto)
        let getAssetHashByCode = memoize getAssetHashByCodeFromStorage
        let getAssetState = memoize (getAssetStateFromStorage >> Option.map Mapping.assetStateFromDto)
        let getValidatorState = memoize (getValidatorStateFromStorage >> Option.map Mapping.validatorStateFromDto)
        let getStakeState = memoize (getStakeStateFromStorage >> Option.map Mapping.stakeStateFromDto)
        let getStakers = memoize getStakersFromStorage
        let getTotalChxStaked = memoize getTotalChxStakedFromStorage

        let getTopStakers = getTopStakersFromStorage >> List.map Mapping.stakerInfoFromDto

        let getTradingPairControllers =
            let controllers = lazy (getTradingPairControllersFromStorage ())
            fun () -> controllers.Value
        let getTradingPairState = memoize (getTradingPairStateFromStorage >> Option.map Mapping.tradingPairStateFromDto)
        let getTradeOrderState = memoize (getTradeOrderStateFromStorage >> Option.map Mapping.tradeOrderStateFromDto)
        let getTradeOrders = memoize (getTradeOrdersFromStorage >> List.map Mapping.tradeOrderInfoFromDto)
        let getExpiredTradeOrders = memoize (getExpiredTradeOrdersFromStorage >> List.map Mapping.tradeOrderInfoFromDto)
        let getIneligibleTradeOrders =
            memoize (getIneligibleTradeOrdersFromStorage >> List.map Mapping.tradeOrderInfoFromDto)
        let getHoldingInTradeOrders = memoize getHoldingInTradeOrdersFromStorage

        let validators = getValidatorsAtHeight (blockNumber - 1)

        let sharedRewardPercent =
            match validators |> List.filter (fun v -> v.ValidatorAddress = validatorAddress) with
            | [] ->
                failwithf "Validator %s not found to create block %i"
                    validatorAddress.Value
                    blockNumber.Value
            | [v] ->
                v.SharedRewardPercent
            | vs ->
                failwithf "%i entries found for validator %s while creating block %i"
                    vs.Length
                    validatorAddress.Value
                    blockNumber.Value

        let validators = validators |> List.map (fun v -> v.ValidatorAddress)

        let output =
            Processing.processChanges
                getTx
                getEquivocationProof
                verifySignature
                isValidHash
                isValidAddress
                deriveHash
                decodeHash
                createHash
                createConsensusMessageHash
                getChxAddressState
                getHoldingState
                getVoteState
                getEligibilityState
                getKycProviders
                getAccountState
                getAssetState
                getAssetHashByCode
                getValidatorState
                getStakeState
                getStakers
                getTotalChxStaked
                getTopStakers
                getTradingPairControllers
                getTradingPairState
                getTradeOrderState
                getTradeOrders
                getExpiredTradeOrders
                getIneligibleTradeOrders
                getHoldingInTradeOrders
                getLockedAndBlacklistedValidators
                maxActionCountPerTx
                validatorDeposit
                validatorDepositLockTime
                validatorBlacklistTime
                validators
                validatorAddress
                sharedRewardPercent
                blockNumber
                timestamp
                blockchainConfiguration
                equivocationProofs
                txSet

        let block =
            Blocks.assembleBlock
                decodeHash
                createHash
                createMerkleTree
                validatorAddress
                blockNumber
                timestamp
                previousBlockHash
                configurationBlockNumber
                txSet
                equivocationProofs
                output
                blockchainConfiguration

        (block, output)

    let proposeBlock
        (getLastAppliedBlockNumber : unit -> BlockNumber)
        createBlock
        createNewBlockchainConfiguration
        getBlock
        getPendingTxs
        (getPendingEquivocationProofs : BlockNumber -> EquivocationInfoDto list)
        getChxAddressStateFromStorage
        getAvailableChxBalanceFromStorage
        addressFromPrivateKey
        minTxActionFee
        maxTxSetFetchIterations
        createEmptyBlocks
        minEmptyBlockTime
        minValidatorCount
        validatorPrivateKey
        (blockNumber : BlockNumber)
        : Result<Block, AppErrors> option
        =

        let timestamp = Utils.getNetworkTimestamp () |> Timestamp

        let getChxAddressState = memoize (getChxAddressStateFromStorage >> Option.map Mapping.chxAddressStateFromDto)
        let getAvailableChxBalance = memoize getAvailableChxBalanceFromStorage

        let lastAppliedBlockNumber = getLastAppliedBlockNumber ()

        if blockNumber <> lastAppliedBlockNumber + 1 then
            sprintf "Cannot propose block %i due to block %i being last applied block"
                blockNumber.Value
                lastAppliedBlockNumber.Value
            |> Result.appError
            |> Some
        else
            let lastAppliedBlockResult =
                getBlock lastAppliedBlockNumber
                |> Result.map Blocks.extractBlockFromEnvelopeDto

            match lastAppliedBlockResult with
            | Error _ -> Some lastAppliedBlockResult
            | Ok lastAppliedBlock ->
                let configBlockNumber, currentConfiguration =
                    Blocks.getConfigurationAtHeight getBlock lastAppliedBlock.Header.Number

                let txSet =
                    Processing.getTxSetForNewBlock
                        getPendingTxs
                        getChxAddressState
                        getAvailableChxBalance
                        minTxActionFee
                        maxTxSetFetchIterations
                        currentConfiguration.MaxTxCountPerBlock
                    |> fun (txSet, iteration) ->
                        Log.debugf "TX set (%i) for proposal fetched in %i iteration(s)" txSet.Length iteration
                        Processing.orderTxSet txSet

                let equivocationProofs =
                    getPendingEquivocationProofs blockNumber
                    |> List.sortBy (fun p ->
                        p.BlockNumber,
                        p.ConsensusRound,
                        p.ConsensusStep,
                        p.ValidatorAddress,
                        p.EquivocationProofHash
                    )
                    |> List.map (fun p -> p.EquivocationProofHash |> EquivocationProofHash)

                let earliestValidEmptyBlockTimestamp =
                    Blocks.earliestValidEmptyBlockTimestamp minEmptyBlockTime lastAppliedBlock.Header.Timestamp

                if txSet.IsEmpty && equivocationProofs.IsEmpty
                    && (timestamp < earliestValidEmptyBlockTimestamp || not createEmptyBlocks)
                then
                    None // Nothing to propose.
                else
                    let validatorAddress = addressFromPrivateKey validatorPrivateKey

                    let newConfiguration =
                        if configBlockNumber + currentConfiguration.ConfigurationBlockDelta = blockNumber then
                            let newConfiguration : BlockchainConfiguration =
                                createNewBlockchainConfiguration
                                    currentConfiguration.ConfigurationBlockDelta
                                    currentConfiguration.ValidatorDepositLockTime
                                    currentConfiguration.ValidatorBlacklistTime
                                    currentConfiguration.MaxTxCountPerBlock
                                    (currentConfiguration.Validators |> List.map (fun v -> v.ValidatorAddress))
                                    blockNumber
                                    timestamp
                                    validatorAddress

                            if newConfiguration.Validators.Length < minValidatorCount then
                                String.Format(
                                    "Due to insufficient number of validators ({0})"
                                        + " configuration block {1} is taking over previous configuration",
                                    newConfiguration.Validators.Length,
                                    blockNumber.Value
                                )
                                |> Log.warning

                                currentConfiguration
                            else
                                newConfiguration
                            |> Some
                        else
                            None

                    let block, _ =
                        createBlock
                            validatorAddress
                            lastAppliedBlock.Header.Hash
                            blockNumber
                            timestamp
                            configBlockNumber
                            currentConfiguration.ValidatorDepositLockTime
                            currentConfiguration.ValidatorBlacklistTime
                            txSet
                            equivocationProofs
                            newConfiguration

                    block |> Ok |> Some

    let storeReceivedBlock
        isValidHash
        isValidAddress
        getBlock
        createConsensusMessageHash
        verifySignature
        blockExists
        saveBlock
        saveBlockToDb
        minValidatorCount
        blockEnvelopeDto
        =

        result {
            let! blockEnvelope = Validation.validateBlockEnvelope isValidHash isValidAddress blockEnvelopeDto
            let block = blockEnvelope.Block

            if not (blockExists block.Header.ConfigurationBlockNumber) then
                Synchronization.unverifiedBlocks.TryAdd(block.Header.Number, blockEnvelopeDto) |> ignore
                return!
                    sprintf "Missing configuration block %i for block %i"
                        block.Header.ConfigurationBlockNumber.Value
                        block.Header.Number.Value
                    |> Result.appError

            let! configBlock =
                getBlock block.Header.ConfigurationBlockNumber
                |> Result.map Blocks.extractBlockFromEnvelopeDto

            match configBlock.Configuration with
            | None -> failwithf "Missing configuration in existing block %i" configBlock.Header.Number.Value
            | Some c ->
                let validators =
                    c.Validators
                    |> List.map (fun v -> v.ValidatorAddress)
                    |> Set.ofList

                if validators.Count < minValidatorCount then
                    failwithf "Block %i must have at least %i validators in configuration to verify block %i. Found %i"
                        configBlock.Header.Number.Value
                        minValidatorCount
                        block.Header.Number.Value
                        validators.Count

                let blacklist = c.ValidatorsBlacklist

                // Verify proposer
                if blacklist |> List.contains block.Header.ProposerAddress then
                    return!
                        sprintf "Block %i (%s) is proposed by a blacklisted validator %s"
                            block.Header.Number.Value
                            block.Header.Hash.Value
                            block.Header.ProposerAddress.Value
                        |> Result.appError

                // Verify signatures
                let! blockSigners =
                    Blocks.verifyBlockSignatures createConsensusMessageHash verifySignature blockEnvelope
                    |> Result.map (Set.ofList >> Set.intersect validators >> Set.toList)

                let qualifiedMajority = Validators.calculateQualifiedMajority validators.Count

                if blockSigners.Length < qualifiedMajority then
                    return!
                        sprintf "Block %i (%s) is not signed by qualified majority. Expected (min): %i / Actual: %i"
                            block.Header.Number.Value
                            block.Header.Hash.Value
                            qualifiedMajority
                            blockSigners.Length
                        |> Result.appError

                if (blockSigners |> List.except blacklist).Length < qualifiedMajority then
                    return!
                        sprintf "Block %i (%s) is signed by blacklisted validator(s)"
                            block.Header.Number.Value
                            block.Header.Hash.Value
                        |> Result.appError

                // Validate configuration of the incoming block
                if configBlock.Header.Number + c.ConfigurationBlockDelta = block.Header.Number then
                    match block.Configuration with
                    | None ->
                        return!
                            sprintf "Configuration missing from incoming block %i (%s)"
                                block.Header.Number.Value
                                block.Header.Hash.Value
                            |> Result.appError
                    | Some c ->
                        if c.Validators.Length < minValidatorCount then
                            return!
                                sprintf "Config block %i (%s) must have at least %i validators in the configuration"
                                    block.Header.Number.Value
                                    block.Header.Hash.Value
                                    minValidatorCount
                                |> Result.appError
                        if c.Validators |> List.exists (fun v -> blacklist |> List.contains v.ValidatorAddress) then
                            return!
                                sprintf "Config block %i (%s) contains blacklisted validators in the validator snapshot"
                                    block.Header.Number.Value
                                    block.Header.Hash.Value
                                |> Result.appError

            do! saveBlock block.Header.Number blockEnvelopeDto

            do! block.Header
                |> Mapping.blockHeaderToBlockInfoDto (block.Configuration <> None)
                |> saveBlockToDb

            Synchronization.unverifiedBlocks.TryRemove(block.Header.Number) |> ignore

            return block.Header.Number
        }

    let persistTxResults saveTxResult txResults =
        txResults
        |> Map.toList
        |> List.fold (fun result (txHash, txResult) ->
            result >>= fun _ ->
                Log.noticef "Saving TxResult %s" txHash
                saveTxResult (TxHash txHash) txResult
        ) (Ok ())

    let removeOrphanTxResults getAllPendingTxHashes txResultExists deleteTxResult =
        let pendingTxHashes = getAllPendingTxHashes ()
        for (h : TxHash) in pendingTxHashes do
            if txResultExists h then
                Log.warningf "Deleting orphan TxResult: %s" h.Value
                deleteTxResult h
                |> Result.iterError Log.appErrors

    let persistEquivocationProofResults saveEquivocationProofResult equivocationProofResults =
        equivocationProofResults
        |> Map.toList
        |> List.fold (fun result (equivocationProofHash, equivocationProofResult) ->
            result >>= fun _ ->
                Log.noticef "Saving EquivocationProofResult %s" equivocationProofHash
                saveEquivocationProofResult (EquivocationProofHash equivocationProofHash) equivocationProofResult
        ) (Ok ())

    let removeOrphanEquivocationProofResults
        getAllPendingEquivocationProofHashes
        equivocationProofResultExists
        deleteEquivocationProofResult
        =

        let pendingEquivocationProofHashes = getAllPendingEquivocationProofHashes ()
        for (h : EquivocationProofHash) in pendingEquivocationProofHashes do
            if equivocationProofResultExists h then
                Log.warningf "Deleting orphan EquivocationProofResult: %s" h.Value
                deleteEquivocationProofResult h
                |> Result.iterError Log.appErrors

    let persistClosedTradeOrders saveClosedTradeOrder closedTradeOrders =
        closedTradeOrders
        |> Map.toList
        |> List.fold (fun result (tradeOrderHash, closedTradeOrder) ->
            result >>= fun _ ->
                Log.noticef "Saving closed trade order %s" tradeOrderHash
                saveClosedTradeOrder (TradeOrderHash tradeOrderHash) closedTradeOrder
        ) (Ok ())

    let removeOrphanClosedTradeOrders getAllOpenTradeOrderHashes closedTradeOrderExists deleteClosedTradeOrder =
        let openTradeOrderHashes = getAllOpenTradeOrderHashes ()
        for (h : TradeOrderHash) in openTradeOrderHashes do
            if closedTradeOrderExists h then
                Log.warningf "Deleting orphan closed trade order: %s" h.Value
                deleteClosedTradeOrder h
                |> Result.iterError Log.appErrors

    let applyBlockToCurrentState
        getBlock
        isValidSuccessorBlock
        txResultExists
        equivocationProofResultExists
        createNewBlockchainConfiguration
        createBlock
        minValidatorCount
        (block : Block)
        =

        result {
            let! previousBlock =
                getBlock (block.Header.Number - 1)
                |> Result.map Blocks.extractBlockFromEnvelopeDto

            if block.Header.Timestamp <= previousBlock.Header.Timestamp then
                return!
                    sprintf
                        "Block %i timestamp (%i) must be greater than the previous block timestamp (%i)"
                        block.Header.Number.Value
                        block.Header.Timestamp.Value
                        previousBlock.Header.Timestamp.Value
                    |> Result.appError

            if not (isValidSuccessorBlock previousBlock.Header.Hash block) then
                return!
                    sprintf "Block %i is not a valid successor of the previous block" block.Header.Number.Value
                    |> Result.appError

            for txHash in block.TxSet do
                if txResultExists txHash then
                    return!
                        sprintf
                            "TX %s cannot be included in the block %i because it is already processed"
                            txHash.Value
                            block.Header.Number.Value
                        |> Result.appError

            for equivocationProofHash in block.EquivocationProofs do
                if equivocationProofResultExists equivocationProofHash then
                    return!
                        sprintf
                            "EquivocationProof %s cannot be included in the block %i because it is already processed"
                            equivocationProofHash.Value
                            block.Header.Number.Value
                        |> Result.appError

            let configBlockNumber, currentConfiguration =
                Blocks.getConfigurationAtHeight getBlock previousBlock.Header.Number

            if configBlockNumber + currentConfiguration.ConfigurationBlockDelta = block.Header.Number then
                match block.Configuration with
                | None ->
                    return!
                        sprintf "Configuration missing from block %i" block.Header.Number.Value
                        |> Result.appError
                | Some c ->
                    if c.Validators.Length < minValidatorCount then
                        return!
                            sprintf "Config block %i must have at least %i validators in the configuration"
                                block.Header.Number.Value
                                minValidatorCount
                            |> Result.appError

                    let newConfiguration : BlockchainConfiguration =
                        createNewBlockchainConfiguration
                            currentConfiguration.ConfigurationBlockDelta
                            currentConfiguration.ValidatorDepositLockTime
                            currentConfiguration.ValidatorBlacklistTime
                            currentConfiguration.MaxTxCountPerBlock
                            (currentConfiguration.Validators |> List.map (fun v -> v.ValidatorAddress))
                            block.Header.Number
                            block.Header.Timestamp
                            block.Header.ProposerAddress

                    let expectedConfiguration =
                        if newConfiguration.Validators.Length < minValidatorCount then
                            currentConfiguration
                        else
                            newConfiguration

                    if c <> expectedConfiguration then
                        Log.warningf "RECEIVED CONFIGURATION:\n%A" c
                        Log.warningf "EXPECTED CONFIGURATION:\n%A" expectedConfiguration
                        return!
                            sprintf "Configuration in block %i is different than expected"
                                block.Header.Number.Value
                            |> Result.appError

            let createdBlock, output =
                createBlock
                    block.Header.ProposerAddress
                    previousBlock.Header.Hash
                    block.Header.Number
                    block.Header.Timestamp
                    configBlockNumber
                    currentConfiguration.ValidatorDepositLockTime
                    currentConfiguration.ValidatorBlacklistTime
                    block.TxSet
                    block.EquivocationProofs
                    block.Configuration

            if block <> createdBlock then
                Log.debugf "RECEIVED BLOCK:\n%A" block
                Log.debugf "CREATED BLOCK:\n%A" createdBlock
                Log.debugf "STATE CHANGES:\n%A" output
                return!
                    sprintf "Applying of block %i didn't result in expected blockchain state" block.Header.Number.Value
                    |> Result.appError

            return output
        }

    let applyBlock
        getBlock
        applyBlockToCurrentState
        persistTxResults
        persistEquivocationProofResults
        persistClosedTradeOrders
        persistStateChanges
        blockNumber
        =

        result {
            let! block =
                getBlock blockNumber
                |> Result.map Blocks.extractBlockFromEnvelopeDto

            let! output = applyBlockToCurrentState block

            #if DEBUG
            sprintf "Data/Block_%i_output_apply" block.Header.Number.Value
            |> fun outputFileName -> System.IO.File.WriteAllText(outputFileName, sprintf "%A" output)
            #endif

            let outputDto = Mapping.outputToDto blockNumber output
            do! persistTxResults outputDto.TxResults
            do! persistEquivocationProofResults outputDto.EquivocationProofResults
            do! persistClosedTradeOrders outputDto.ClosedTradeOrders
            do! persistStateChanges blockNumber block.Header.Timestamp block.Header.ProposerAddress outputDto
        }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Consensus
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let persistConsensusMessage
        (saveConsensusMessage : ConsensusMessageInfoDto -> Result<unit, AppErrors>)
        (consensusMessageEnvelope : ConsensusMessageEnvelope)
        =

        {
            ConsensusMessageInfoDto.BlockNumber = consensusMessageEnvelope.BlockNumber.Value
            ConsensusRound = consensusMessageEnvelope.Round.Value
            ConsensusStep =
                consensusMessageEnvelope.ConsensusMessage
                |> Mapping.consensusStepFromMessage
                |> Mapping.consensusStepToCode
                |> Convert.ToInt16
            MessageEnvelope =
                consensusMessageEnvelope
                |> Mapping.consensusMessageEnvelopeToDto
                |> Serialization.serializeBinary
                |> Convert.ToBase64String
        }
        |> saveConsensusMessage

    let restoreConsensusMessages (getConsensusMessages : unit -> ConsensusMessageInfoDto list) =
        [
            for messageInfo in getConsensusMessages () do
                let envelope =
                    messageInfo.MessageEnvelope
                    |> Convert.FromBase64String
                    |> Serialization.deserializeBinary
                    |> Mapping.consensusMessageEnvelopeFromDto

                if messageInfo.BlockNumber <> envelope.BlockNumber.Value then
                    failwithf "BlockNumber mismatch in persisted consensus message: %i vs %i"
                        messageInfo.BlockNumber
                        envelope.BlockNumber.Value

                if messageInfo.ConsensusRound <> envelope.Round.Value then
                    failwithf "ConsensusRound mismatch in persisted consensus message: %i vs %i"
                        messageInfo.ConsensusRound
                        envelope.Round.Value

                let consensusStep =
                    envelope.ConsensusMessage
                    |> Mapping.consensusStepFromMessage
                    |> Mapping.consensusStepToCode
                    |> Convert.ToInt16

                if messageInfo.ConsensusStep <> consensusStep then
                    failwithf "ConsensusStep mismatch in persisted consensus message: %i vs %i"
                        messageInfo.ConsensusStep
                        consensusStep

                yield envelope
        ]

    let persistConsensusState
        (saveConsensusState : ConsensusStateInfoDto -> Result<unit, AppErrors>)
        (consensusStateInfo : ConsensusStateInfo)
        =

        {
            ConsensusStateInfoDto.BlockNumber = consensusStateInfo.BlockNumber.Value
            ConsensusRound = consensusStateInfo.ConsensusRound.Value
            ConsensusStep =
                consensusStateInfo.ConsensusStep
                |> Mapping.consensusStepToCode
                |> Convert.ToInt16
            LockedBlock =
                consensusStateInfo.LockedBlock
                |> Option.map (Mapping.blockToDto >> Serialization.serializeBinary >> Convert.ToBase64String)
                |> Option.toObj
            LockedRound = consensusStateInfo.LockedRound.Value
            ValidBlock =
                consensusStateInfo.ValidBlock
                |> Option.map (Mapping.blockToDto >> Serialization.serializeBinary >> Convert.ToBase64String)
                |> Option.toObj
            ValidRound = consensusStateInfo.ValidRound.Value
            ValidBlockSignatures =
                consensusStateInfo.ValidBlockSignatures
                |> List.map (fun s -> s.Value)
                |> fun signatures -> String.Join(',', signatures)
        }
        |> saveConsensusState

    let restoreConsensusState (getConsensusState : unit -> ConsensusStateInfoDto option) =
        getConsensusState ()
        |> Option.map (fun s ->
            {
                ConsensusStateInfo.BlockNumber = BlockNumber s.BlockNumber
                ConsensusRound = ConsensusRound s.ConsensusRound
                ConsensusStep =
                    s.ConsensusStep
                    |> Convert.ToByte
                    |> Mapping.consensusStepFromCode
                LockedBlock =
                    s.LockedBlock
                    |> Option.ofObj
                    |> Option.map (Convert.FromBase64String >> Serialization.deserializeBinary >> Mapping.blockFromDto)
                LockedRound = ConsensusRound s.LockedRound
                ValidBlock =
                    s.ValidBlock
                    |> Option.ofObj
                    |> Option.map (Convert.FromBase64String >> Serialization.deserializeBinary >> Mapping.blockFromDto)
                ValidRound = ConsensusRound s.ValidRound
                ValidBlockSignatures =
                    if s.ValidBlockSignatures.IsNullOrWhiteSpace() then
                        []
                    else
                        s.ValidBlockSignatures.Split(',')
                        |> Seq.map Signature
                        |> Seq.toList
            }
        )

    let verifyConsensusMessage
        decodeHash
        createHash
        (getValidators : unit -> ValidatorSnapshot list)
        (verifySignature : Signature -> string -> BlockchainAddress option)
        (envelopeDto : ConsensusMessageEnvelopeDto)
        =

        let envelope = Mapping.consensusMessageEnvelopeFromDto envelopeDto

        let messageHash =
            Consensus.createConsensusMessageHash
                decodeHash
                createHash
                envelope.BlockNumber
                envelope.Round
                envelope.ConsensusMessage

        result {
            let! senderAddress =
                match verifySignature (Signature envelopeDto.Signature) messageHash with
                | Some a -> Ok a
                | None ->
                    sprintf "Cannot verify signature for consensus message: %A" envelope
                    |> Result.appError

            let isSenderValidator =
                getValidators ()
                |> Seq.exists (fun v -> v.ValidatorAddress = senderAddress)

            if not isSenderValidator then
                return!
                    sprintf "%A is not a validator. Consensus message ignored: %A" senderAddress envelope
                    |> Result.appError

            return senderAddress, envelope
        }

    let storeEquivocationProof
        verifySignature
        decodeHash
        createHash
        saveEquivocationProof
        saveEquivocationProofToDb
        equivocationProofDto
        =

        result {
            let! equivocationProof =
                Validation.validateEquivocationProof
                    verifySignature
                    Consensus.createConsensusMessageHash
                    decodeHash
                    createHash
                    equivocationProofDto

            do! saveEquivocationProof equivocationProof.EquivocationProofHash equivocationProofDto
            do! equivocationProof
                |> Mapping.equivocationProofToEquivocationInfoDto
                |> saveEquivocationProofToDb

            return equivocationProof.EquivocationProofHash
        }

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Network
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let propagateTx
        publicAddress
        sendMessageToPeers
        getTx
        getNetworkId
        (txHash : TxHash)
        (txEnvelopeDto : TxEnvelopeDto)
        =

        {
            PeerMessageEnvelope.NetworkId = getNetworkId ()
            PeerMessage =
                {
                    MessageId = Tx txHash
                    SenderAddress = publicAddress |> Option.map NetworkAddress
                    Data = Serialization.serializeBinary txEnvelopeDto
                }
                |> GossipMessage
        }
        |> sendMessageToPeers

    let repropagatePendingTx
        getTx
        getPendingTxs
        getChxAddressStateFromStorage
        getAvailableChxBalanceFromStorage
        publishEvent
        minTxActionFee
        maxTxSetFetchIterations
        maxTxCountPerBlock
        txRepropagationCount
        =

        let getChxAddressState = memoize (getChxAddressStateFromStorage >> Option.map Mapping.chxAddressStateFromDto)
        let getAvailableChxBalance = memoize getAvailableChxBalanceFromStorage

        Processing.getTxSetForNewBlock
            getPendingTxs
            getChxAddressState
            getAvailableChxBalance
            minTxActionFee
            maxTxSetFetchIterations
            maxTxCountPerBlock // Fetching more at once significantly reduces number of DB calls when TX pool is large.
        |> fun (txSet, iteration) ->
            Log.debugf "TX set (%i) for re-propagation fetched in %i iteration(s)" txSet.Length iteration
            Processing.orderTxSet txSet
        |> List.truncate txRepropagationCount // Yes, it's actually more efficient in combination with change above.
        |> List.iter (fun txHash ->
            match getTx txHash with
            | Ok txEnvelopeDto -> (txHash, txEnvelopeDto) |> TxVerified |> publishEvent
            | _ -> Log.errorf "TX %s does not exist" txHash.Value
        )

    let propagateEquivocationProof
        publicAddress
        sendMessageToPeers
        getEquivocationProof
        getNetworkId
        (equivocationProofHash : EquivocationProofHash)
        =

        match getEquivocationProof equivocationProofHash with
        | Ok (equivocationProofDto : EquivocationProofDto) ->
            {
                PeerMessageEnvelope.NetworkId = getNetworkId ()
                PeerMessage =
                    {
                        MessageId = EquivocationProof equivocationProofHash
                        SenderAddress = publicAddress |> Option.map NetworkAddress
                        Data = Serialization.serializeBinary equivocationProofDto
                    }
                    |> GossipMessage
            }
            |> sendMessageToPeers
        | _ -> Log.errorf "EquivocationProof %s does not exist" equivocationProofHash.Value

    let propagateBlock
        publicAddress
        sendMessageToPeers
        getBlock
        getNetworkId
        (blockNumber : BlockNumber)
        =

        match getBlock blockNumber with
        | Ok (blockEnvelopeDto : BlockEnvelopeDto) ->
            {
                PeerMessageEnvelope.NetworkId = getNetworkId ()
                PeerMessage =
                    {
                        MessageId = Block blockNumber
                        SenderAddress = publicAddress |> Option.map NetworkAddress
                        Data = Serialization.serializeBinary blockEnvelopeDto
                    }
                    |> GossipMessage
            }
            |> sendMessageToPeers
        | _ -> Log.errorf "Block %i does not exist" blockNumber.Value

    let requestConsensusState
        validatorPrivateKey
        getNetworkId
        getIdentity
        sendMessageToPeers
        isValidator
        addressFromPrivateKey
        consensusRound
        targetValidatorAddress
        =

        let validatorAddress = addressFromPrivateKey validatorPrivateKey

        if isValidator validatorAddress then
            let consensusStateRequest =
                {
                    ConsensusStateRequest.ValidatorAddress = validatorAddress
                    ConsensusRound = consensusRound
                    TargetValidatorAddress = targetValidatorAddress
                }

            {
                PeerMessageEnvelope.NetworkId = getNetworkId ()
                PeerMessage =
                    {
                        MulticastMessage.MessageId = NetworkMessageId.ConsensusState
                        SenderIdentity = getIdentity () |> Some
                        Data =
                            consensusStateRequest
                            |> Mapping.consensusStateRequestToDto
                            |> Serialization.serializeBinary
                    }
                    |> MulticastMessage
            }
            |> sendMessageToPeers

            Log.debug "Consensus state requested"

    let sendConsensusState
        getNetworkId
        respondToPeer
        targetIdentity
        consensusStateResponse
        =

        let responseItem =
            {
                ResponseItemMessage.MessageId = NetworkMessageId.ConsensusState
                Data =
                    consensusStateResponse
                    |> Mapping.consensusStateResponseToDto
                    |> Serialization.serializeBinary
            }

        {
            PeerMessageEnvelope.NetworkId = getNetworkId ()
            PeerMessage = ResponseDataMessage {ResponseDataMessage.Items = [ responseItem ]}
        }
        |> respondToPeer targetIdentity

    let processPeerMessage
        getTx
        getEquivocationProof
        getBlock
        getLastAppliedBlockNumber
        verifyConsensusMessage
        respondToPeer
        getPeerList
        getNetworkId
        (peerMessageEnvelope : PeerMessageEnvelope)
        =

        let processTxFromPeer isResponse txHash data =
            match getTx txHash with
            | Ok _ -> None
            | _ ->
                data
                |> Serialization.deserializeBinary<TxEnvelopeDto>
                |> fun txEnvelopeDto ->
                    (txHash, txEnvelopeDto)
                    |> if isResponse then TxFetched else TxReceived
                    |> Some
            |> Ok

        let processEquivocationProofFromPeer isResponse equivocationProofHash data =
            match getEquivocationProof equivocationProofHash with
            | Ok _ -> None
            | _ ->
                data
                |> Serialization.deserializeBinary<EquivocationProofDto>
                |> fun equivocationProofDto ->
                    equivocationProofDto
                    |> if isResponse then EquivocationProofFetched else EquivocationProofReceived
                    |> Some
            |> Ok

        let processBlockFromPeer isResponse blockNr data =
            let blockEnvelopeDto = data |> Serialization.deserializeBinary<BlockEnvelopeDto>

            result {
                let receivedBlock = Blocks.extractBlockFromEnvelopeDto blockEnvelopeDto

                return
                    match getBlock receivedBlock.Header.Number with
                    | Ok _ -> None
                    | _ ->
                        (receivedBlock.Header.Number, blockEnvelopeDto)
                        |> if isResponse then BlockFetched else BlockReceived
                        |> Some
            }

        let processConsensusMessageFromPeer data =
            data
            |> Serialization.deserializeBinary<ConsensusMessageEnvelopeDto>
            |> verifyConsensusMessage
            |> Result.map (ConsensusCommand.Message >> ConsensusMessageReceived >> Some)

        let processConsensusStateFromPeer isResponse senderIdentity data =
            if isResponse then
                data
                |> Serialization.deserializeBinary<ConsensusStateResponseDto>
                |> Mapping.consensusStateResponseFromDto
                |> ConsensusStateResponseReceived
                |> Some
                |> Ok
            else
                match senderIdentity with
                | None -> failwith "SenderIdentity is missing from ConsensusStateRequest"
                | Some identity ->
                    data
                    |> Serialization.deserializeBinary<ConsensusStateRequestDto>
                    |> fun request -> Mapping.consensusStateRequestFromDto request, identity
                    |> ConsensusStateRequestReceived
                    |> Some
                    |> Ok

        let processBlockchainHeadMessageFromPeer data =
            data
            |> Serialization.deserializeBinary<BlockchainHeadInfoDto>
            |> fun info -> BlockNumber info.BlockNumber
            |> BlockchainHeadReceived
            |> Some
            |> Ok

        let processPeerListMessageFromPeer data =
            data
            |> Serialization.deserializeBinary<GossipDiscoveryMessageDto>
            |> fun m ->
                m.ActiveMembers
                |> List.map Mapping.gossipPeerFromDto
                |> PeerListReceived
                |> Some
                |> Ok

        let processDataMessage isResponse messageId (senderIdentity : PeerNetworkIdentity option) (data : byte[]) =
            match messageId with
            | Tx txHash -> processTxFromPeer isResponse txHash data
            | EquivocationProof proofHash -> processEquivocationProofFromPeer isResponse proofHash data
            | Block blockNr -> processBlockFromPeer isResponse blockNr data
            | Consensus _ -> processConsensusMessageFromPeer data
            | ConsensusState -> processConsensusStateFromPeer isResponse senderIdentity data
            | BlockchainHead -> processBlockchainHeadMessageFromPeer data
            | PeerList -> processPeerListMessageFromPeer data

        let processRequestMessage messageIds senderIdentity =
            let responseResult =
                messageIds
                |> List.map (fun messageId ->
                    match messageId with
                    | Tx txHash ->
                        match getTx txHash with
                        | Ok txEvenvelopeDto ->
                            {
                                MessageId = messageId
                                Data = txEvenvelopeDto |> Serialization.serializeBinary
                            }
                            |> Ok
                        | _ -> Result.appError (sprintf "Requested TX %s not found" txHash.Value)
                    | EquivocationProof equivocationProofHash ->
                        match getEquivocationProof equivocationProofHash with
                        | Ok equivocationProofDto ->
                            {
                                MessageId = messageId
                                Data = equivocationProofDto |> Serialization.serializeBinary
                            }
                            |> Ok
                        | _ ->
                            Result.appError (
                                sprintf "Requested equivocation proof %s not found" equivocationProofHash.Value
                            )
                    | Block blockNr ->
                        let blockNr =
                            if blockNr = BlockNumber -1L then
                                getLastAppliedBlockNumber ()
                            else
                                blockNr

                        match getBlock blockNr with
                        | Ok blockEnvelopeDto ->
                            {
                                MessageId = messageId
                                Data = blockEnvelopeDto |> Serialization.serializeBinary
                            }
                            |> Ok
                        | _ -> Result.appError (sprintf "Requested block %i not found" blockNr.Value)
                    | Consensus _ -> Result.appError "Cannot request consensus message from Peer"
                    | ConsensusState -> Result.appError "Cannot request consensus state from Peer"
                    | BlockchainHead ->
                        {
                            MessageId = messageId
                            Data =
                                {
                                    BlockchainHeadInfoDto.BlockNumber =
                                        getLastAppliedBlockNumber ()
                                        |> fun (BlockNumber blockNr) -> blockNr
                                }
                                |> Serialization.serializeBinary
                        }
                        |> Ok
                    | PeerList ->
                        {
                            MessageId = messageId
                            Data =
                                {
                                    GossipDiscoveryMessageDto.ActiveMembers =
                                        getPeerList ()
                                        |> List.map Mapping.gossipPeerToDto
                                }
                                |> Serialization.serializeBinary
                        }
                        |> Ok
                )

            let responseItems =
                responseResult
                |> List.choose (function
                    | Ok responseItem -> Some responseItem
                    | _ -> None
                )

            let errors =
                responseResult
                |> List.choose (function
                    | Ok _ -> None
                    | Error e -> Some e.Head.Message
                )

            if not responseItems.IsEmpty then
                {
                    PeerMessageEnvelope.NetworkId = getNetworkId ()
                    PeerMessage = ResponseDataMessage {ResponseDataMessage.Items = responseItems}
                }
                |> respondToPeer senderIdentity

            match errors with
            | [] -> Ok None
            | _ -> Result.appErrors errors

        let processResponseMessage (responseItems : ResponseItemMessage list) =
            responseItems
            |> List.map (fun response -> processDataMessage true response.MessageId None response.Data)

        match peerMessageEnvelope.PeerMessage with
        | GossipDiscoveryMessage _ -> None
        | GossipMessage m -> [ processDataMessage false m.MessageId None m.Data ] |> Some
        | MulticastMessage m -> [ processDataMessage false m.MessageId m.SenderIdentity m.Data ] |> Some
        | RequestDataMessage m -> [ processRequestMessage m.Items m.SenderIdentity ] |> Some
        | ResponseDataMessage m -> processResponseMessage m.Items |> Some

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // API
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let getNodeInfoApi
        (addressFromPrivateKey : PrivateKey -> BlockchainAddress)
        versionNumber
        versionHash
        networkCode
        (publicAddress : string option)
        validatorPrivateKey
        minTxActionFee
        =

        let validatorAddress =
            validatorPrivateKey
            |> Option.ofObj
            |> Option.map (fun pk ->
                let address = PrivateKey pk |> addressFromPrivateKey
                address.Value
            )
            |> Option.toObj

        {
            VersionNumber = versionNumber
            VersionHash = versionHash
            NetworkCode = networkCode
            PublicAddress = publicAddress |> Option.toObj
            ValidatorAddress = validatorAddress
            MinTxActionFee = minTxActionFee
        }

    let getConsensusInfo () =
        match Consensus.getConsensusInfo () with
        | Some s -> Ok s
        | None -> Result.appError "Consensus state does not exist"

    let getPeerListApi
        (getPeerList : unit -> GossipPeer list)
        : Result<GetPeerListApiDto, AppErrors>
        =

        let peers =
            getPeerList ()
            |> List.map (fun m -> m.NetworkAddress.Value)
            |> List.sort

        { GetPeerListApiDto.Peers = peers }
        |> Ok

    let getTxPoolByAddressApi
        (getTxPoolByAddress : BlockchainAddress -> TxByAddressInfoDto list)
        (address : BlockchainAddress)
        : Result<GetTxPoolByAddressApiDto, AppErrors>
        =

        let txs = getTxPoolByAddress address

        {
            GetTxPoolByAddressApiDto.SenderAddress = address.Value
            PendingTxCount = int64 txs.Length
            PendingTxs = txs
        }
        |> Ok

    let submitTx
        verifySignature
        isValidHash
        isValidAddress
        createHash
        (getChxAddressState : BlockchainAddress -> ChxAddressStateDto option)
        getAvailableChxBalance
        getTotalFeeForPendingTxs
        saveTx
        saveTxToDb
        maxActionCountPerTx
        minTxActionFee
        publishEvent
        isIncludedInBlock
        txEnvelopeDto
        : Result<TxHash * TxEnvelopeDto, AppErrors>
        =

        result {
            let! txEnvelope = Validation.validateTxEnvelope txEnvelopeDto
            let! senderAddress = Validation.verifyTxSignature createHash verifySignature txEnvelope
            let txHash = txEnvelope.RawTx |> createHash |> TxHash

            let! txDto = Serialization.deserializeTx txEnvelope.RawTx
            let! tx = Validation.validateTx isValidHash isValidAddress maxActionCountPerTx senderAddress txHash txDto

            // TXs included in verified blocks are considered to be valid, hence shouldn't be rejected.
            if not isIncludedInBlock then
                // Check nonce
                let addressNonce =
                    getChxAddressState tx.Sender
                    |> Option.map (fun s -> s.Nonce)
                    |? 0L
                    |> Nonce
                if tx.Nonce <= addressNonce then
                    return!
                        addressNonce.Value
                        |> sprintf "TX nonce must be greater than current address nonce: %i"
                        |> Result.appError

                // Check fee
                if tx.ActionFee < minTxActionFee then
                    return! Result.appError "ActionFee is too low"

                do!
                    Validation.checkIfBalanceCanCoverFees
                        getAvailableChxBalance
                        getTotalFeeForPendingTxs
                        senderAddress
                        tx.TotalFee

                (txHash, txEnvelopeDto) |> TxVerified |> publishEvent

            do! saveTx txHash txEnvelopeDto
            do! tx
                |> Mapping.txToTxInfoDto
                |> saveTxToDb isIncludedInBlock

            return (txHash, txEnvelopeDto)
        }

    let getTxApi
        getTx
        getTxInfo
        getTxResult
        createHash
        verifySignature
        (txHash : TxHash)
        : Result<GetTxApiResponseDto, AppErrors>
        =

        result {
            let! txEnvelope =
                getTx txHash
                |> Result.map Mapping.txEnvelopeFromDto

            let! senderAddress = Validation.verifyTxSignature createHash verifySignature txEnvelope

            let! txDto = Serialization.deserializeTx txEnvelope.RawTx

            let! txResult =
                // If the TX is still in the pool (DB), we respond with Pending status and ignore the TxResult file.
                // This avoids the lag in the state changes (when TxResult file is persisted, but DB not yet updated).
                match getTxInfo txHash with
                | Some _ -> Ok None
                | None -> getTxResult txHash |> Result.map Some

            return Mapping.txToGetTxApiResponseDto txHash senderAddress txDto txResult
        }

    let getEquivocationProofApi
        getEquivocationProof
        getEquivocationInfo
        getEquivocationProofResult
        decodeHash
        createHash
        verifySignature
        (equivocationProofHash : EquivocationProofHash)
        : Result<GetEquivocationProofApiResponseDto, AppErrors>
        =

        result {
            let! equivocationProofDto = getEquivocationProof equivocationProofHash
            let! equivocationProof =
                Validation.validateEquivocationProof
                    verifySignature
                    Consensus.createConsensusMessageHash
                    decodeHash
                    createHash
                    equivocationProofDto

            let! equivocationProofResult =
                // If the EquivocationProof is still in the pool (DB),
                // we respond with Pending status and ignore the EquivocationProofResult file.
                // This avoids the lag in the state changes
                // (when EquivocationProofResult file is persisted, but DB not yet updated).
                match getEquivocationInfo equivocationProofHash with
                | Some _ -> Ok None
                | None -> getEquivocationProofResult equivocationProofHash |> Result.map Some

            return
                Mapping.equivocationProofToGetEquivocationProofApiResponseDto
                    equivocationProofHash
                    equivocationProof.ValidatorAddress
                    equivocationProofDto
                    equivocationProofResult
        }

    let getBlockApi
        getLastAppliedBlockNumber
        (getBlock : BlockNumber -> Result<BlockEnvelopeDto, AppErrors>)
        (blockNumber : BlockNumber)
        : Result<GetBlockApiResponseDto, AppErrors>
        =

        let lastAppliedBlockNumber = getLastAppliedBlockNumber ()
        if blockNumber > lastAppliedBlockNumber then
            sprintf "Last applied block on this node is %i" lastAppliedBlockNumber.Value
            |> Result.appError
        else
            getBlock blockNumber
            >>= (Mapping.blockEnvelopeDtoToGetBlockApiResponseDto >> Ok)

    let getValidatorsApi
        (getCurrentValidators : unit -> ValidatorSnapshot list)
        (getAllValidators : unit -> GetValidatorApiResponseDto list)
        (activeOnly : bool option)
        : Result<GetValidatorsApiDto, AppErrors>
        =

        let currentValidators =
            getCurrentValidators ()
            |> List.map (fun v -> v.ValidatorAddress.Value)
            |> Set.ofList

        let allValidators =
            getAllValidators ()
            |> List.map (fun v -> { v with IsActive = currentValidators.Contains v.ValidatorAddress })

        let validators =
            match activeOnly with
            | Some isActive when isActive -> allValidators |> List.filter (fun v -> v.IsActive)
            | _ -> allValidators

        Ok {Validators = validators}

    let getValidatorStakesApi
        getValidatorState
        (getValidatorStakes : BlockchainAddress -> ValidatorStakeInfoDto list)
        (address : BlockchainAddress)
        : Result<GetValidatorStakesApiResponseDto, AppErrors>
        =

        match getValidatorState address with
        | None ->
            sprintf "Validator %s does not exist" address.Value
            |> Result.appError
        | Some _ ->
            {
                ValidatorAddress = address.Value
                GetValidatorStakesApiResponseDto.Stakes = getValidatorStakes address
            }
            |> Ok

    let getValidatorApi
        (getValidatorState : BlockchainAddress -> ValidatorStateDto option)
        (getCurrentValidators : unit -> ValidatorSnapshot list)
        (address : BlockchainAddress)
        : Result<GetValidatorApiResponseDto, AppErrors>
        =

        match getValidatorState address with
        | None ->
            sprintf "Validator %s does not exist" address.Value
            |> Result.appError
        | Some state ->
            {
                ValidatorAddress = address.Value
                NetworkAddress = state.NetworkAddress
                SharedRewardPercent = state.SharedRewardPercent
                IsDepositLocked = state.TimeToLockDeposit > 0s
                IsBlacklisted = state.TimeToBlacklist > 0s
                IsEnabled = state.IsEnabled
                IsActive = getCurrentValidators () |> List.exists (fun v -> v.ValidatorAddress = address)
                LastProposedBlockNumber = state.LastProposedBlockNumber
                LastProposedBlockTimestamp = state.LastProposedBlockTimestamp
            }
            |> Ok

    let getAddressStakesApi
        (getAddressStakes : BlockchainAddress -> AddressStakeInfoDto list)
        (address : BlockchainAddress)
        : Result<GetAddressStakesApiResponseDto, AppErrors>
        =

        {
            BlockchainAddress = address.Value
            GetAddressStakesApiResponseDto.Stakes = getAddressStakes address
        }
        |> Ok

    let getAddressApi
        (getChxAddressState : BlockchainAddress -> ChxAddressStateDto option)
        getDetailedChxBalance
        (blockchainAddress : BlockchainAddress)
        : Result<GetAddressApiResponseDto, AppErrors>
        =

        let detailedChxBalance = getDetailedChxBalance blockchainAddress
        let nonce =
            match getChxAddressState blockchainAddress with
            | Some state -> state.Nonce
            | None -> 0L

        {
            BlockchainAddress = blockchainAddress.Value
            Nonce = nonce
            Balance = detailedChxBalance
        }
        |> Ok

    let getAddressAccountsApi
        (getAddressAccounts : BlockchainAddress -> AccountHash list)
        (address : BlockchainAddress)
        : Result<GetAddressAccountsApiResponseDto, AppErrors>
        =

        let accounts =
            getAddressAccounts address
            |> List.map (fun (AccountHash h) -> h)

        {
            BlockchainAddress = address.Value
            GetAddressAccountsApiResponseDto.Accounts = accounts
        }
        |> Ok

    let getAddressAssetsApi
        (getAddressAssets : BlockchainAddress -> AssetHash list)
        (address : BlockchainAddress)
        : Result<GetAddressAssetsApiResponseDto, AppErrors>
        =

        let assets =
            getAddressAssets address
            |> List.map (fun (AssetHash h) -> h)

        {
            BlockchainAddress = address.Value
            GetAddressAssetsApiResponseDto.Assets = assets
        }
        |> Ok

    let getAccountApi
        (getAccountState : AccountHash -> AccountStateDto option)
        getAccountHoldings
        (accountHash : AccountHash)
        (assetHash : AssetHash option)
        : Result<GetAccountApiResponseDto, AppErrors>
        =

        match getAccountState accountHash with
        | None ->
            sprintf "Account %s does not exist" accountHash.Value
            |> Result.appError
        | Some accountState ->
            getAccountHoldings accountHash assetHash
            |> Mapping.accountHoldingDtosToGetAccountHoldingsResponseDto accountHash accountState
            |> Ok

    let getAccountVotesApi
        (getAccountState : AccountHash -> AccountStateDto option)
        getAccountVotes
        (accountHash : AccountHash)
        (assetHash : AssetHash option)
        : Result<GetAccountApiVoteDto, AppErrors>
        =

        match getAccountState accountHash with
        | None ->
            sprintf "Account %s does not exist" accountHash.Value
            |> Result.appError
        | Some _ ->
            {
                AccountHash = accountHash.Value
                Votes = getAccountVotes accountHash assetHash
            }
            |> Ok

    let getAccountEligibilitiesApi
        (getAccountState : AccountHash -> AccountStateDto option)
        getAccountEligibilities
        (accountHash : AccountHash)
        : Result<GetAccountApiEligibilitiesDto, AppErrors>
        =

        match getAccountState accountHash with
        | None ->
            sprintf "Account %s does not exist" accountHash.Value
            |> Result.appError
        | Some _ ->
            {
                AccountHash = accountHash.Value
                Eligibilities = getAccountEligibilities accountHash
            }
            |> Ok

    let getAccountKycProvidersApi
        (getAccountState : AccountHash -> AccountStateDto option)
        (getAccountKycProviders : AccountHash -> BlockchainAddress list)
        (accountHash : AccountHash)
        : Result<GetAccountApiKycProvidersDto, AppErrors>
        =

        match getAccountState accountHash with
        | None ->
            sprintf "Account %s does not exist" accountHash.Value
            |> Result.appError
        | Some _ ->
            let kycProviders =
                getAccountKycProviders accountHash
                |> List.map (fun address -> address.Value)
            {
                AccountHash = accountHash.Value
                KycProviders = kycProviders
            }
            |> Ok

    let getAssetApi
        (getAssetState : AssetHash -> AssetStateDto option)
        (assetHash : AssetHash)
        : Result<AssetInfoDto, AppErrors>
        =

        match getAssetState assetHash with
        | None ->
            sprintf "Asset %s does not exist" assetHash.Value
            |> Result.appError
        | Some assetState ->
            {
                AssetHash = assetHash.Value
                AssetCode = assetState.AssetCode
                ControllerAddress = assetState.ControllerAddress
                IsEligibilityRequired = assetState.IsEligibilityRequired
            }
            |> Ok

    let getAssetByCodeApi
        (getAssetHashByCode : AssetCode -> AssetHash option)
        (getAssetState : AssetHash -> AssetStateDto option)
        (assetCode : AssetCode)
        : Result<AssetInfoDto, AppErrors>
        =

        match getAssetHashByCode assetCode with
        | None ->
            sprintf "Asset with code %s does not exist" assetCode.Value
            |> Result.appError
        | Some assetHash ->
            match getAssetState assetHash with
            | None ->
                sprintf "Asset %s does not exist" assetHash.Value
                |> Result.appError
            | Some assetState ->
                {
                    AssetHash = assetHash.Value
                    AssetCode = assetState.AssetCode
                    ControllerAddress = assetState.ControllerAddress
                    IsEligibilityRequired = assetState.IsEligibilityRequired
                }
                |> Ok

    let getAssetKycProvidersApi
        (getAssetState : AssetHash -> AssetStateDto option)
        (getAssetKycProviders : AssetHash -> BlockchainAddress list)
        (assetHash : AssetHash)
        : Result<GetAssetApiKycProvidersDto, AppErrors>
        =

        match getAssetState assetHash with
        | None ->
            sprintf "Asset %s does not exist" assetHash.Value
            |> Result.appError
        | Some _ ->
            let kycProviders =
                getAssetKycProviders assetHash
                |> List.map (fun address -> address.Value)
            {
                AssetHash = assetHash.Value
                KycProviders = kycProviders
            }
            |> Ok

    let getTradingPairsApi
        (getTradingPairs : unit -> TradingPairApiDto list)
        =

        getTradingPairs ()
        |> List.sortBy (fun p ->
            p.BaseAssetCode.IsNullOrWhiteSpace(),
            p.QuoteAssetCode.IsNullOrWhiteSpace(),
            p.BaseAssetCode |??? "",
            p.QuoteAssetCode |??? "",
            p.BaseAssetHash,
            p.QuoteAssetHash
        )

    let getTradingPairApi
        (getTradingPair : AssetHash * AssetHash -> TradingPairApiDto option)
        (baseAssetHash : AssetHash, quoteAssetHash : AssetHash)
        =

        match getTradingPair (baseAssetHash, quoteAssetHash) with
        | None ->
            sprintf "Trading pair %s / %s does not exist" baseAssetHash.Value quoteAssetHash.Value
            |> Result.appError
        | Some s ->
            Ok s

    let getTradeOrderBookAggregatedApi
        (getExecutableTradeOrdersAggregated : AssetHash * AssetHash -> TradeOrderAggregatedDto list)
        (baseAssetHash : AssetHash, quoteAssetHash : AssetHash)
        =

        let buyOrderSideCode = Mapping.tradeOrderSideToCode Buy
        let bids, asks =
            getExecutableTradeOrdersAggregated (baseAssetHash, quoteAssetHash)
            |> List.partition (fun o -> o.Side = buyOrderSideCode)

        {|
            Bids =
                bids
                |> List.sortBy (fun o -> -o.LimitPrice)
                |> List.map (fun o -> {| Price = o.LimitPrice; Amount = o.Amount |})
            Asks =
                asks
                |> List.sortBy (fun o -> o.LimitPrice)
                |> List.map (fun o -> {| Price = o.LimitPrice; Amount = o.Amount |})
        |}

    let getTradeOrderBookApi
        (getExecutableTradeOrders : AssetHash * AssetHash -> TradeOrderInfoDto list)
        (baseAssetHash : AssetHash, quoteAssetHash : AssetHash)
        =

        let orderBook = Trading.getTradeOrderBook getExecutableTradeOrders (baseAssetHash, quoteAssetHash)

        {|
            BuyOrders = orderBook.BuyOrders |> List.map Mapping.tradeOrderInfoToTradeOrderApiDto
            SellOrders = orderBook.SellOrders |> List.map Mapping.tradeOrderInfoToTradeOrderApiDto
        |}

    let getAccountTradeOrdersApi
        (getAccountTradeOrders : AccountHash -> TradeOrderInfoDto list)
        (accountHash : AccountHash)
        =

        getAccountTradeOrders accountHash
        |> List.map Mapping.tradeOrderInfoFromDto
        |> List.sortBy (fun o ->
            o.BaseAssetHash,
            o.QuoteAssetHash,
            o.Side,
            (if o.Side = Buy then -o.LimitPrice.Value else o.LimitPrice.Value),
            o.BlockNumber,
            o.TradeOrderHash
        )
        |> List.map Mapping.tradeOrderInfoToTradeOrderApiDto

    let getTradeOrderApi
        getTradeOrderState
        getClosedTradeOrder
        (tradeOrderHash : TradeOrderHash)
        : Result<TradeOrderApiDto, AppErrors>
        =

        // If the trade order is still open, we return it and ignore closed trade order.
        // This avoids the lag in the state changes, when closed trade order is persisted,
        // but state not yet updated (order not yet removed from the order book).
        match getTradeOrderState tradeOrderHash with
        | Some dto ->
            dto
            |> Mapping.tradeOrderStateFromDto
            |> fun o -> Mapping.tradeOrderStateToInfo (tradeOrderHash, o)
            |> Mapping.tradeOrderInfoToTradeOrderApiDto
            |> Ok
        | None ->
            getClosedTradeOrder tradeOrderHash
            |> Result.map (fun o -> Mapping.tradeOrderApiDtoFromClosedTradeOrderDto (tradeOrderHash, o))
