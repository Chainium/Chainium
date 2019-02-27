namespace Own.Blockchain.Public.Core.Tests

open Xunit
open Swensen.Unquote
open Own.Common
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Core.Dtos
open Own.Blockchain.Public.Crypto

module ProcessingTests =

    type ProcessChangesDependencies =
        {
            GetTx : TxHash -> Result<TxEnvelopeDto, AppErrors>
            GetEquivocationProof : EquivocationProofHash -> Result<EquivocationProofDto, AppErrors>
            VerifySignature : Signature -> string -> BlockchainAddress option
            IsValidAddress : BlockchainAddress -> bool
            DeriveHash : BlockchainAddress -> Nonce -> TxActionNumber -> string
            DecodeHash : string -> byte[]
            CreateHash : byte[] -> string
            CreateConsensusMessageHash :
                (string -> byte[]) -> (byte[] -> string) -> BlockNumber -> ConsensusRound -> ConsensusMessage -> string
            GetChxBalanceStateFromStorage : BlockchainAddress -> ChxBalanceState option
            GetHoldingStateFromStorage : AccountHash * AssetHash -> HoldingState option
            GetVoteStateFromStorage : VoteId -> VoteState option
            GetEligibilityStateFromStorage : AccountHash * AssetHash -> EligibilityState option
            GetKycProvidersFromStorage : AssetHash -> BlockchainAddress list
            GetAccountStateFromStorage : AccountHash -> AccountState option
            GetAssetStateFromStorage : AssetHash -> AssetState option
            GetAssetHashByCodeFromStorage : AssetCode -> AssetHash option
            GetValidatorStateFromStorage : BlockchainAddress -> ValidatorState option
            GetStakeStateFromStorage : BlockchainAddress * BlockchainAddress -> StakeState option
            GetStakersFromStorage : BlockchainAddress -> BlockchainAddress list
            GetTotalChxStakedFromStorage : BlockchainAddress -> ChxAmount
            GetTopStakers : BlockchainAddress -> StakerInfo list
            GetLockedAndBlacklistedValidators : unit -> BlockchainAddress list
            MaxActionCountPerTx : int
            ValidatorDeposit : ChxAmount
            ValidatorDepositLockTime : int16
            ValidatorBlacklistTime : int16
            Validators : BlockchainAddress list
            ValidatorAddress : BlockchainAddress
            SharedRewardPercent : decimal
            BlockNumber : BlockNumber
            BlockchainConfiguration : BlockchainConfiguration option
            EquivocationProofs : EquivocationProofHash list
            TxSet : TxHash list
        }

    let processChangesMockedDeps =
        let unexpectedInvocation functionName =
            failwithf "%s unexpectedly invoked." functionName
        {
            ProcessChangesDependencies.GetTx = fun _ -> unexpectedInvocation "GetTx"
            GetEquivocationProof = fun _ -> unexpectedInvocation "GetEquivocationProof"
            VerifySignature = Helpers.verifySignature
            IsValidAddress = Hashing.isValidBlockchainAddress
            DeriveHash = Hashing.deriveHash
            DecodeHash = Hashing.decode
            CreateHash = Hashing.hash
            CreateConsensusMessageHash = fun _ -> unexpectedInvocation "CreateConsensusMessageHash"
            GetChxBalanceStateFromStorage = fun _ -> unexpectedInvocation "GetChxBalanceStateFromStorage"
            GetHoldingStateFromStorage = fun _ -> unexpectedInvocation "GetHoldingStateFromStorage"
            GetVoteStateFromStorage = fun _ -> unexpectedInvocation "GetVoteStateFromStorage"
            GetEligibilityStateFromStorage = fun _ -> unexpectedInvocation "GetEligibilityStateFromStorage"
            GetKycProvidersFromStorage = fun _ -> unexpectedInvocation "GetKycProvidersFromStorage"
            GetAccountStateFromStorage = fun _ -> unexpectedInvocation "GetAccountStateFromStorage"
            GetAssetStateFromStorage = fun _ -> unexpectedInvocation "GetAssetStateFromStorage"
            GetAssetHashByCodeFromStorage = fun _ -> unexpectedInvocation "GetAssetHashByCodeFromStorage"
            GetValidatorStateFromStorage = fun _ -> unexpectedInvocation "GetValidatorStateFromStorage"
            GetStakeStateFromStorage = fun _ -> unexpectedInvocation "GetStakeStateFromStorage"
            GetStakersFromStorage = fun _ -> unexpectedInvocation "GetStakersFromStorage"
            GetTotalChxStakedFromStorage = fun _ -> unexpectedInvocation "GetTotalChxStakedFromStorage"
            GetTopStakers = fun _ -> unexpectedInvocation "GetTopStakers"
            GetLockedAndBlacklistedValidators = fun _ -> unexpectedInvocation "GetLockedAndBlacklistedValidators"
            MaxActionCountPerTx = Helpers.maxActionCountPerTx
            ValidatorDeposit = ChxAmount 0m
            ValidatorDepositLockTime = 0s
            ValidatorBlacklistTime = 0s
            Validators = []
            ValidatorAddress = BlockchainAddress ""
            SharedRewardPercent = 0m
            BlockNumber = BlockNumber 1L
            BlockchainConfiguration = None
            EquivocationProofs = []
            TxSet = []
        }

    let processChanges mockedDeps =
        Processing.processChanges
            mockedDeps.GetTx
            mockedDeps.GetEquivocationProof
            mockedDeps.VerifySignature
            mockedDeps.IsValidAddress
            mockedDeps.DeriveHash
            mockedDeps.DecodeHash
            mockedDeps.CreateHash
            mockedDeps.CreateConsensusMessageHash
            mockedDeps.GetChxBalanceStateFromStorage
            mockedDeps.GetHoldingStateFromStorage
            mockedDeps.GetVoteStateFromStorage
            mockedDeps.GetEligibilityStateFromStorage
            mockedDeps.GetKycProvidersFromStorage
            mockedDeps.GetAccountStateFromStorage
            mockedDeps.GetAssetStateFromStorage
            mockedDeps.GetAssetHashByCodeFromStorage
            mockedDeps.GetValidatorStateFromStorage
            mockedDeps.GetStakeStateFromStorage
            mockedDeps.GetStakersFromStorage
            mockedDeps.GetTotalChxStakedFromStorage
            mockedDeps.GetTopStakers
            mockedDeps.GetLockedAndBlacklistedValidators
            mockedDeps.MaxActionCountPerTx
            mockedDeps.ValidatorDeposit
            mockedDeps.ValidatorDepositLockTime
            mockedDeps.ValidatorBlacklistTime
            mockedDeps.Validators
            mockedDeps.ValidatorAddress
            mockedDeps.SharedRewardPercent
            mockedDeps.BlockNumber
            mockedDeps.BlockchainConfiguration
            mockedDeps.EquivocationProofs
            mockedDeps.TxSet

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Tx preparation
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.excludeUnprocessableTxs excludes txs after nonce gap`` () =
        let w1 = Signing.generateWallet ()
        let w2 = Signing.generateWallet ()

        let getChxBalanceState =
            let data =
                [
                    w1.Address, { ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L }
                    w2.Address, { ChxBalanceState.Amount = ChxAmount 200m; Nonce = Nonce 20L }
                ]
                |> Map.ofSeq

            fun (address : BlockchainAddress) -> data |> Map.tryFind address

        let getAvailableChxBalance address =
            (getChxBalanceState address).Value.Amount

        let txSet =
            [
                Helpers.newPendingTxInfo (TxHash "Tx2") w1.Address (Nonce 12L) (ChxAmount 1m) 1s 2L
                Helpers.newPendingTxInfo (TxHash "Tx3") w1.Address (Nonce 10L) (ChxAmount 1m) 1s 3L
                Helpers.newPendingTxInfo (TxHash "Tx4") w1.Address (Nonce 14L) (ChxAmount 1m) 1s 4L
                Helpers.newPendingTxInfo (TxHash "Tx5") w1.Address (Nonce 11L) (ChxAmount 1m) 1s 5L
                Helpers.newPendingTxInfo (TxHash "Tx1") w2.Address (Nonce 21L) (ChxAmount 1m) 1s 1L
            ]

        // ACT
        let txHashes =
            txSet
            |> Processing.excludeUnprocessableTxs getChxBalanceState getAvailableChxBalance
            |> List.map (fun tx -> tx.TxHash.Value)

        test <@ txHashes = ["Tx1"; "Tx2"; "Tx3"; "Tx5"] @>

    [<Theory>]
    [<InlineData(1, 0, "Tx1")>]
    [<InlineData(3, 0, "Tx1; Tx3")>]
    [<InlineData(4, 1, "Tx1; Tx3")>]
    [<InlineData(4, 0, "Tx1; Tx3; Tx5")>]
    [<InlineData(5, 1, "Tx1; Tx3; Tx5")>]
    let ``Processing.excludeUnprocessableTxs excludes txs if CHX balance cannot cover the fees``
        (balance : decimal, staked : decimal, txHashes : string)
        =

        let balance = ChxAmount balance
        let expectedHashes = txHashes.Split("; ") |> Array.toList

        let w1 = Signing.generateWallet ()
        let w2 = Signing.generateWallet ()

        let getChxBalanceState =
            let data =
                [
                    w1.Address, { ChxBalanceState.Amount = balance; Nonce = Nonce 10L }
                    w2.Address, { ChxBalanceState.Amount = ChxAmount 200m; Nonce = Nonce 20L }
                ]
                |> Map.ofSeq

            fun (address : BlockchainAddress) -> data |> Map.tryFind address

        let getAvailableChxBalance =
            let data =
                [
                    w1.Address, balance - staked
                    w2.Address, ChxAmount 200m
                ]
                |> Map.ofSeq

            fun (address : BlockchainAddress) -> data.[address]

        let txSet =
            [
                Helpers.newPendingTxInfo (TxHash "Tx2") w1.Address (Nonce 12L) (ChxAmount 1m) 1s 2L
                Helpers.newPendingTxInfo (TxHash "Tx3") w1.Address (Nonce 10L) (ChxAmount 1.5m) 2s 3L
                Helpers.newPendingTxInfo (TxHash "Tx4") w1.Address (Nonce 14L) (ChxAmount 1m) 1s 4L
                Helpers.newPendingTxInfo (TxHash "Tx5") w1.Address (Nonce 11L) (ChxAmount 1m) 1s 5L
                Helpers.newPendingTxInfo (TxHash "Tx1") w2.Address (Nonce 21L) (ChxAmount 1m) 1s 1L
            ]

        // ACT
        let txHashes =
            txSet
            |> Processing.excludeUnprocessableTxs getChxBalanceState getAvailableChxBalance
            |> List.map (fun tx -> tx.TxHash.Value)

        test <@ txHashes = expectedHashes @>

    [<Fact>]
    let ``Processing.orderTxSet puts txs in correct order`` () =
        let w1 = Signing.generateWallet ()
        let w2 = Signing.generateWallet ()

        let getChxBalanceState =
            let data =
                [
                    w1.Address, { ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L }
                    w2.Address, { ChxBalanceState.Amount = ChxAmount 200m; Nonce = Nonce 20L }
                ]
                |> Map.ofSeq

            fun (address : BlockchainAddress) -> data.[address]

        let txSet =
            [
                Helpers.newPendingTxInfo (TxHash "Tx1") w2.Address (Nonce 21L) (ChxAmount 1m) 1s 1L
                Helpers.newPendingTxInfo (TxHash "Tx2") w1.Address (Nonce 12L) (ChxAmount 1m) 1s 2L
                Helpers.newPendingTxInfo (TxHash "Tx3") w1.Address (Nonce 10L) (ChxAmount 1m) 1s 3L
                Helpers.newPendingTxInfo (TxHash "Tx6") w2.Address (Nonce 21L) (ChxAmount 2m) 1s 6L
                Helpers.newPendingTxInfo (TxHash "Tx5") w1.Address (Nonce 11L) (ChxAmount 1m) 1s 5L
            ]

        // ACT
        let txHashes =
            txSet
            |> Processing.orderTxSet
            |> List.map (fun (TxHash hash) -> hash)

        test <@ txHashes = ["Tx6"; "Tx1"; "Tx3"; "Tx5"; "Tx2"] @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Reward distribution
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges distributes rewards to stakers`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let staker1Wallet = Signing.generateWallet ()
        let staker2Wallet = Signing.generateWallet ()
        let staker3Wallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
                staker1Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
                staker2Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
                staker3Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
            ]
            |> Map.ofList

        let stake1 = ChxAmount 150m
        let stake2 = ChxAmount 100m
        let stake3 = ChxAmount 50m

        let actionFee = ChxAmount 1m
        let sharedRewardPercent = 60m
        let validatorReward = ChxAmount 0.4m
        let staker1Reward = ChxAmount 0.3m
        let staker2Reward = ChxAmount 0.2m
        let staker3Reward = ChxAmount 0.1m

        // PREPARE TX
        let nonce = Nonce 11L
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ =
            [
                {StakerInfo.StakerAddress = staker1Wallet.Address; Amount = stake1}
                {StakerInfo.StakerAddress = staker2Wallet.Address; Amount = stake2}
                {StakerInfo.StakerAddress = staker3Wallet.Address; Amount = stake3}
            ]

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                SharedRewardPercent = sharedRewardPercent
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - amountToTransfer - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount + amountToTransfer
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + validatorReward
        let staker1ChxBalance = initialChxState.[staker1Wallet.Address].Amount + staker1Reward
        let staker2ChxBalance = initialChxState.[staker2Wallet.Address].Amount + staker2Reward
        let staker3ChxBalance = initialChxState.[staker3Wallet.Address].Amount + staker3Reward

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>

        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker1Wallet.Address].Nonce = initialChxState.[staker1Wallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker2Wallet.Address].Nonce = initialChxState.[staker2Wallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker3Wallet.Address].Nonce = initialChxState.[staker3Wallet.Address].Nonce @>

        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.ChxBalances.[staker1Wallet.Address].Amount = staker1ChxBalance @>
        test <@ output.ChxBalances.[staker2Wallet.Address].Amount = staker2ChxBalance @>
        test <@ output.ChxBalances.[staker3Wallet.Address].Amount = staker3ChxBalance @>

    [<Fact>]
    let ``Processing.processChanges distributes rewards to stakers with proper decimalization`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let staker1Wallet = Signing.generateWallet ()
        let staker2Wallet = Signing.generateWallet ()
        let staker3Wallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
                staker1Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
                staker2Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
                staker3Wallet.Address, {ChxBalanceState.Amount = ChxAmount 1000m; Nonce = Nonce 0L}
            ]
            |> Map.ofList

        let stake1 = ChxAmount 100m
        let stake2 = ChxAmount 100m
        let stake3 = ChxAmount 100m

        let actionFee = ChxAmount 0.0000010m
        let sharedRewardPercent = 100m
        let validatorReward = ChxAmount 0.0000001m // Due to rounding
        let staker1Reward = ChxAmount 0.0000003m
        let staker2Reward = ChxAmount 0.0000003m
        let staker3Reward = ChxAmount 0.0000003m

        // PREPARE TX
        let nonce = Nonce 11L
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ =
            [
                {StakerInfo.StakerAddress = staker1Wallet.Address; Amount = stake1}
                {StakerInfo.StakerAddress = staker2Wallet.Address; Amount = stake2}
                {StakerInfo.StakerAddress = staker3Wallet.Address; Amount = stake3}
            ]

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                SharedRewardPercent = sharedRewardPercent
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - amountToTransfer - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount + amountToTransfer
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + validatorReward
        let staker1ChxBalance = initialChxState.[staker1Wallet.Address].Amount + staker1Reward
        let staker2ChxBalance = initialChxState.[staker2Wallet.Address].Amount + staker2Reward
        let staker3ChxBalance = initialChxState.[staker3Wallet.Address].Amount + staker3Reward

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>

        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker1Wallet.Address].Nonce = initialChxState.[staker1Wallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker2Wallet.Address].Nonce = initialChxState.[staker2Wallet.Address].Nonce @>
        test <@ output.ChxBalances.[staker3Wallet.Address].Nonce = initialChxState.[staker3Wallet.Address].Nonce @>

        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.ChxBalances.[staker1Wallet.Address].Amount = staker1ChxBalance @>
        test <@ output.ChxBalances.[staker2Wallet.Address].Amount = staker2ChxBalance @>
        test <@ output.ChxBalances.[staker3Wallet.Address].Amount = staker3ChxBalance @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // TransferChx
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges TransferChx`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - amountToTransfer - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount + amountToTransfer
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferChx with insufficient balance`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 9m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.InsufficientChxBalance) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferChx with insufficient balance to cover fee`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 10.5m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.InsufficientChxBalance) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferChx with insufficient balance to cover fee - simulated invalid state`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 0.5m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        let processChanges () =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        raisesWith<exn>
            <@ processChanges () @>
            (fun ex -> <@ ex.Message.StartsWith "Cannot process validator reward" @>)

    [<Fact>]
    let ``Processing.processChanges TransferChx with insufficient balance to cover fee due to the staked CHX`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let recipientWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 11m; Nonce = Nonce 10L}
                recipientWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 20L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = ChxAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferChx"
                    ActionData =
                        {
                            RecipientAddress = recipientWallet.Address.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked address =
            if address = senderWallet.Address then
                ChxAmount 1m
            else
                ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let recipientChxBalance = initialChxState.[recipientWallet.Address].Amount
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.InsufficientChxBalance) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[recipientWallet.Address].Nonce = initialChxState.[recipientWallet.Address].Nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[recipientWallet.Address].Amount = recipientChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // TransferAsset
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges TransferAsset`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let senderAssetBalance = initialHoldingState.[senderAccountHash, assetHash].Amount - amountToTransfer
        let recipientAssetBalance = initialHoldingState.[recipientAccountHash, assetHash].Amount + amountToTransfer

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = senderAssetBalance @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = recipientAssetBalance @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].IsEmission = false @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset success if transfer from emission to EligibleInPrimary`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = true}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = false}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = true}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderAssetBalance = initialHoldingState.[senderAccountHash, assetHash].Amount - amountToTransfer
        let recipientAssetBalance = initialHoldingState.[recipientAccountHash, assetHash].Amount + amountToTransfer

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = senderAssetBalance @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = recipientAssetBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset success if transfer from non-emission to EligibleInSecondary`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = true}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderAssetBalance = initialHoldingState.[senderAccountHash, assetHash].Amount - amountToTransfer
        let recipientAssetBalance = initialHoldingState.[recipientAccountHash, assetHash].Amount + amountToTransfer

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = senderAssetBalance @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = recipientAssetBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset fails if transfer from emission to NotEligibleInPrimary`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = true}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = false; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = true}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.NotEligibleInPrimary)
            |> TxActionError
            |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = AssetAmount 50m @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = AssetAmount 0m @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset fails if transfer from non-emission to NotEligibleInSecondary`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = false}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = true}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.NotEligibleInSecondary)
            |> TxActionError
            |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = AssetAmount 50m @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = AssetAmount 0m @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset with insufficient balance`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 9m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let senderAssetBalance = initialHoldingState.[senderAccountHash, assetHash].Amount
        let recipientAssetBalance = initialHoldingState.[recipientAccountHash, assetHash].Amount
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.InsufficientAssetHoldingBalance)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings.[senderAccountHash, assetHash].Amount = senderAssetBalance @>
        test <@ output.Holdings.[recipientAccountHash, assetHash].Amount = recipientAssetBalance @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset fails if source account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 9m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getAccountState accountHash =
            if accountHash = recipientAccountHash then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SourceAccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges TransferAsset fails if destination account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let senderAccountHash = AccountHash "Acc1"
        let recipientAccountHash = AccountHash "Acc2"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (senderAccountHash, assetHash), {HoldingState.Amount = AssetAmount 9m; IsEmission = false}
                (recipientAccountHash, assetHash), {HoldingState.Amount = AssetAmount 0m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let amountToTransfer = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "TransferAsset"
                    ActionData =
                        {
                            FromAccountHash = senderAccountHash.Value
                            ToAccountHash = recipientAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = amountToTransfer.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getAccountState accountHash =
            if accountHash = senderAccountHash then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.DestinationAccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings = Map.empty @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SubmitVote
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SubmitVote success insert`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (accountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHash.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getVoteState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT

        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>

        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

        test <@ output.Votes.Count = 1 @>
        test <@ output.Votes.[{AccountHash = accountHash; AssetHash = assetHash; ResolutionHash = resolutionHash}]
            = {VoteHash = voteHash; VoteWeight = None} @>

    [<Fact>]
    let ``Processing.processChanges SubmitVote success insert and update unweighted`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let rsh1, rsh2 = VotingResolutionHash "RS1", VotingResolutionHash "RS2"
        let voteHashYes, voteHashNo = VoteHash "Yes", VoteHash "No"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (accountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce1 = Nonce 11L
        let nonce2 = nonce1 + 1L

        let actionFee = ChxAmount 1m

        // Vote Yes on RS1
        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = rsh1.Value
                            VoteHash = voteHashYes.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce1 actionFee

        // Vote Yes on RS2
        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = rsh2.Value
                            VoteHash = voteHashYes.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce2 actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid tx hash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getVoteState (voteId: VoteId) =
            // No vote for rsh1
            if voteId.ResolutionHash = rsh1 then
                None
            // Existing non weighted vote for rsh2
            elif voteId.ResolutionHash = rsh2 then
                Some {VoteState.VoteHash = voteHashNo; VoteWeight = None}
            else
                None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = Success @>
        test <@ output.TxResults.[txHash2].Status = Success @>
        test <@ output.Votes.Count = 2 @>
        test <@ output.Votes.[{AccountHash = accountHash; AssetHash = assetHash; ResolutionHash = rsh1}]
            = {VoteHash = voteHashYes; VoteWeight = None} @>
        test <@ output.Votes.[{AccountHash = accountHash; AssetHash = assetHash; ResolutionHash = rsh2}]
            = {VoteHash = voteHashYes; VoteWeight = None} @>

    [<Fact>]
    let ``Processing.processChanges SubmitVote fails if no holding`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHash.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getVoteState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.HoldingNotFound) |> TxActionError |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>

        test <@ output.Votes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SubmitVote fails if asset or account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash1, accountHash2 =
            AccountHash "Acc1", AccountHash "Acc2"
        let assetHash1, assetHash2 =
            AssetHash "EQ1", AssetHash "EQ2"

        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash1.Value
                            AssetHash = assetHash1.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHash.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash2.Value
                            AssetHash = assetHash2.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHash.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid tx hash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getAccountState accountHash =
            if accountHash = accountHash1 then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            elif accountHash = accountHash2 then
                None
            else
                None

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            elif assetHash = assetHash2 then
                {
                    AssetState.AssetCode = None
                    ControllerAddress = senderWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound) |> TxActionError |> Failure
        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.Votes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SubmitVote fails if sender is not account controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHash.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = otherWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotSourceAccountController) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Votes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SubmitVote fails if vote is already weighted`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let resolutionHash = VotingResolutionHash "RS1"
        let voteHashYes = VoteHash "Yes"
        let voteHashNo = VoteHash "No"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (accountHash, assetHash), {HoldingState.Amount = AssetAmount 50m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVote"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteHash = voteHashYes.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getVoteState _ =
            Some {VoteState.VoteHash = voteHashNo; VoteWeight = 1m |> VoteWeight |> Some}

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.VoteIsAlreadyWeighted) |> TxActionError |> Failure

        let voteId = {AccountHash = accountHash; AssetHash = assetHash; ResolutionHash = resolutionHash}
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Votes.Count = 1 @>
        test <@ output.Votes.[voteId].VoteHash = voteHashNo @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SubmitVoteWeight
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SubmitVoteWeight success update`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"
        let voteWeight = VoteWeight 1m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVoteWeight"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteWeight = voteWeight.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            Some {VoteState.VoteHash = voteHash; VoteWeight = None}

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT

        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>

        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

        test <@ output.Votes.Count = 1 @>
        test <@ output.Votes.[{AccountHash = accountHash; AssetHash = assetHash; ResolutionHash = resolutionHash}]
            = {VoteHash = voteHash; VoteWeight = Some voteWeight} @>

    [<Fact>]
    let ``Processing.processChanges SubmitVoteWeight fails if vote not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"
        let voteWeight = VoteWeight 1m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVoteWeight"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteWeight = voteWeight.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.VoteNotFound) |> TxActionError |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Votes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SubmitVoteWeight fails is sender is not asset controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"
        let resolutionHash = VotingResolutionHash "RS1"
        let voteHash = VoteHash "Yes"
        let voteWeight = VoteWeight 1m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SubmitVoteWeight"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            ResolutionHash = resolutionHash.Value
                            VoteWeight = voteWeight.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            Some {VoteState.VoteHash = voteHash; VoteWeight = None}

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = otherWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController) |> TxActionError |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Votes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SubmitVoteWeight if asset or account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash1, accountHash2 =
            AccountHash "Acc1", AccountHash "Acc2"
        let assetHash1, assetHash2 =
            AssetHash "EQ1", AssetHash "EQ2"

        let resolutionHash = VotingResolutionHash "RS1"
        let voteWeight = VoteWeight 1m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "SubmitVoteWeight"
                    ActionData =
                        {
                            AccountHash = accountHash1.Value
                            AssetHash = assetHash1.Value
                            ResolutionHash = resolutionHash.Value
                            VoteWeight = voteWeight.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "SubmitVoteWeight"
                    ActionData =
                        {
                            AccountHash = accountHash2.Value
                            AssetHash = assetHash2.Value
                            ResolutionHash = resolutionHash.Value
                            VoteWeight = voteWeight.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid tx hash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getAccountState accountHash =
            if accountHash = accountHash1 then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            elif accountHash = accountHash2 then
                None
            else
                None

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            elif assetHash = assetHash2 then
                {
                    AssetState.AssetCode = None
                    ControllerAddress = senderWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound) |> TxActionError |> Failure
        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.Votes = Map.empty @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetAccountEligibility
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SetAccountEligibility insert success`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedEligibilityState =
            {
                EligibilityState.Eligibility =
                    {
                        IsPrimaryEligible = true;
                        IsSecondaryEligible = true
                    }
                KycControllerAddress = senderWallet.Address
            }
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)] = expectedEligibilityState @>

    [<Fact>]
    let ``Processing.processChanges SetAccountEligibility update success`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = false
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedEligibilityState =
            {
                EligibilityState.Eligibility =
                    {
                        IsPrimaryEligible = true;
                        IsSecondaryEligible = false
                    }
                KycControllerAddress = senderWallet.Address
            }
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)] = expectedEligibilityState @>

    [<Fact>]
    let ``Processing.processChanges SetAccountEligibility insert and update fails if not approved KYC provider`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash1, accountHash2 = AccountHash "Acc1", AccountHash "Acc2"
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash1.Value
                            AssetHash = assetHash1.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash2.Value
                            AssetHash = assetHash2.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = false
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid txHash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState (_, assetHash) =
            if assetHash = assetHash1 then
                None
            elif assetHash = assetHash2 then
                {
                    EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                    KycControllerAddress = senderWallet.Address
                }
                |> Some
            else
                None

        let getKycProvidersState _ =
            []

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = otherWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotApprovedKycProvider)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatus @>
        test <@ output.TxResults.[txHash2].Status = expectedStatus @>
        test <@ output.Eligibilities = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SetAccountEligibility update fails if approved KYC provider but not current`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = false
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = otherWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotCurrentKycController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)].KycControllerAddress = otherWallet.Address @>

    [<Fact>]
    let ``Processing.processChanges SetAccountEligibility insert and update fails if asset or account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash1, accountHash2 = AccountHash "Acc1", AccountHash "Acc2"
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash1.Value
                            AssetHash = assetHash1.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "SetAccountEligibility"
                    ActionData =
                        {
                            AccountHash = accountHash2.Value
                            AssetHash = assetHash2.Value
                            IsPrimaryEligible = true
                            IsSecondaryEligible = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid tx hash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            []

        let getAccountState accountHash =
            if accountHash = accountHash1 then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            elif accountHash = accountHash2 then
                None
            else
                None

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            elif assetHash = assetHash2 then
                {
                    AssetState.AssetCode = None
                    ControllerAddress = senderWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure
        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.Eligibilities = Map.empty @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetAssetEligibility
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SetAssetEligibility update success`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetEligibility"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            IsEligibilityRequired = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedAssetState =
            {
                AssetState.AssetCode = None
                ControllerAddress = senderWallet.Address
                IsEligibilityRequired = true
            }
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Assets.Count = 1 @>
        test <@ output.Assets.[assetHash] = expectedAssetState @>

    [<Fact>]
    let ``Processing.processChanges SetAssetEligibility fails if asset not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetEligibility"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            IsEligibilityRequired = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Assets = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SetAssetEligibility fails is sender is not asset controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetEligibility"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            IsEligibilityRequired = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = otherWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        let expectedAssetState =
            {
                AssetState.AssetCode = None
                ControllerAddress = otherWallet.Address
                IsEligibilityRequired = false
            }

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Assets.Count = 1 @>
        test <@ output.Assets.[assetHash] = expectedAssetState @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // ChangeKycControllerAddress
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            KycControllerAddress = otherWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedEligibilityState =
            {
                EligibilityState.Eligibility =
                    {
                        IsPrimaryEligible = true;
                        IsSecondaryEligible = true
                    }
                KycControllerAddress = otherWallet.Address
            }

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)] = expectedEligibilityState @>

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress fails if asset or account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash1, accountHash2 = AccountHash "Acc1", AccountHash "Acc2"
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash1.Value
                            AssetHash = assetHash1.Value
                            KycControllerAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash2.Value
                            AssetHash = assetHash2.Value
                            KycControllerAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid tx hash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState accountHash =
            if accountHash = accountHash1 then
                Some {AccountState.ControllerAddress = senderWallet.Address}
            elif accountHash = accountHash2 then
                None
            else
                None

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            elif assetHash = assetHash2 then
                {
                    AssetState.AssetCode = None
                    ControllerAddress = senderWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some
            else
                None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure
        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.Eligibilities = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress fails if no eligibility`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            KycControllerAddress = otherWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.EligibilityNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Eligibilities = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress fails SenderIsKycCtrlNotApprovedKycProvider`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            KycControllerAddress = otherWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            []

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = otherWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetControllerOrApprovedKycProvider)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)].KycControllerAddress = senderWallet.Address @>

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress ok SenderIsNotApprovedKycProviderButAssetCtrl`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            KycControllerAddress = otherWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = senderWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            []

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedEligibilityState =
            {
                EligibilityState.Eligibility =
                    {
                        IsPrimaryEligible = true;
                        IsSecondaryEligible = true
                    }
                KycControllerAddress = otherWallet.Address
            }

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)] = expectedEligibilityState @>

    [<Fact>]
    let ``Processing.processChanges ChangeKycControllerAddress fails SenderIsNotAssetCtrlOrApprovedKycProvider`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ChangeKycControllerAddress"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            AssetHash = assetHash.Value
                            KycControllerAddress = otherWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            {
                EligibilityState.Eligibility = {IsPrimaryEligible = true; IsSecondaryEligible = true}
                KycControllerAddress = otherWallet.Address
            }
            |> Some

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = otherWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetControllerOrApprovedKycProvider)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.Eligibilities.Count = 1 @>
        test <@ output.Eligibilities.[(accountHash, assetHash)].KycControllerAddress = otherWallet.Address @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddKycProvider
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges AddKycProvider`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "AddKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            []

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.KycProviders.Count = 1 @>
        test <@ output.KycProviders.[assetHash].[senderWallet.Address] = KycProviderChange.Add @>

    [<Fact>]
    let ``Processing.processChanges AddKycProvider fails if provider already exists`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "AddKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            [senderWallet.Address]

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.KycProviderAldreadyExists)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.KycProviders.Count = 1 @>

    [<Fact>]
    let ``Processing.processChanges AddKycProvider various errors`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "AddKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash1.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "AddKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash2.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]

        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid TxHash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            else
                {
                    AssetState.AssetCode = None
                    ControllerAddress = otherWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure

        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.KycProviders = Map.empty @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveKycProvider
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges RemoveKycProvider`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "RemoveKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.KycProviders.Count = 1 @>
        test <@ output.KycProviders.[assetHash].[senderWallet.Address] = KycProviderChange.Remove @>

    [<Fact>]
    let ``Processing.processChanges RemoveKycProvider various errors`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "RemoveKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash1.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "RemoveKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash2.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]

        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid TxHash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState assetHash =
            if assetHash = assetHash1 then
                None
            else
                {
                    AssetState.AssetCode = None
                    ControllerAddress = otherWallet.Address
                    IsEligibilityRequired = false
                }
                |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedStatusTxHash1 =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure

        let expectedStatusTxHash2 =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = expectedStatusTxHash1 @>
        test <@ output.TxResults.[txHash2].Status = expectedStatusTxHash2 @>
        test <@ output.KycProviders = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges Add and RemoveKycProvider mixed`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet()
        let otherWallet = Signing.generateWallet()
        let validatorWallet = Signing.generateWallet ()
        let assetHash1, assetHash2 = AssetHash "EQ1", AssetHash "EQ2"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash1, txEnvelope1 =
            [
                {
                    ActionType = "AddKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash1.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txHash2, txEnvelope2 =
            [
                {
                    ActionType = "RemoveKycProvider"
                    ActionData =
                        {
                            AssetHash = assetHash2.Value
                            ProviderAddress = senderWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet (nonce + 1) actionFee

        let txSet = [txHash1; txHash2]

        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx txHash =
            if txHash = txHash1 then Ok txEnvelope1
            elif txHash = txHash2 then Ok txEnvelope2
            else Result.appError "Invalid TxHash"

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getVoteState _ =
            None

        let getEligibilityState _ =
            None

        let getKycProvidersState _ =
            []

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetVoteStateFromStorage = getVoteState
                GetEligibilityStateFromStorage = getEligibilityState
                GetKycProvidersFromStorage = getKycProvidersState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        test <@ output.TxResults.Count = 2 @>
        test <@ output.TxResults.[txHash1].Status = Success @>
        test <@ output.TxResults.[txHash2].Status = Success @>
        test <@ output.KycProviders.Count = 2 @>
        test <@ output.KycProviders.[assetHash1].[senderWallet.Address] = KycProviderChange.Add @>
        test <@ output.KycProviders.[assetHash2].[senderWallet.Address] = KycProviderChange.Remove @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateAssetEmission
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges CreateAssetEmission`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let someOtherWallet = Signing.generateWallet ()
        let emissionAccountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let emissionAmount = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAssetEmission"
                    ActionData =
                        {
                            EmissionAccountHash = emissionAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = emissionAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = someOtherWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings.[emissionAccountHash, assetHash].Amount = emissionAmount @>
        test <@ output.Holdings.[emissionAccountHash, assetHash].IsEmission = true @>

    [<Fact>]
    let ``Processing.processChanges CreateAssetEmission additional emission`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let someOtherWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let emissionAccountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        let initialHoldingState =
            [
                (emissionAccountHash, assetHash), {HoldingState.Amount = AssetAmount 30m; IsEmission = false}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let emissionAmount = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAssetEmission"
                    ActionData =
                        {
                            EmissionAccountHash = emissionAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = emissionAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState key =
            initialHoldingState |> Map.tryFind key

        let getAccountState _ =
            Some {AccountState.ControllerAddress = someOtherWallet.Address}

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let emittedAssetBalance = initialHoldingState.[emissionAccountHash, assetHash].Amount + emissionAmount

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings.[emissionAccountHash, assetHash].Amount = emittedAssetBalance @>
        test <@ output.Holdings.[emissionAccountHash, assetHash].IsEmission = true @>

    [<Fact>]
    let ``Processing.processChanges CreateAssetEmission fails if sender not current controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let someOtherWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let emissionAccountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let emissionAmount = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAssetEmission"
                    ActionData =
                        {
                            EmissionAccountHash = emissionAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = emissionAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = someOtherWallet.Address}

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = currentControllerWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges CreateAssetEmission fails if asset not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let someOtherWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let emissionAccountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let emissionAmount = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAssetEmission"
                    ActionData =
                        {
                            EmissionAccountHash = emissionAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = emissionAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAccountState _ =
            Some {AccountState.ControllerAddress = someOtherWallet.Address}

        let getAssetState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges CreateAssetEmission fails if account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let emissionAccountHash = AccountHash "Acc1"
        let assetHash = AssetHash "EQ1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m
        let emissionAmount = AssetAmount 10m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAssetEmission"
                    ActionData =
                        {
                            EmissionAccountHash = emissionAccountHash.Value
                            AssetHash = assetHash.Value
                            Amount = emissionAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAccountState _ =
            None

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = currentControllerWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Holdings = Map.empty @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateAccount
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges CreateAccount`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAccount"
                    ActionData = CreateAccountTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        let accountHash =
            [
                Hashing.decode senderWallet.Address.Value
                nonce.Value |> Conversion.int64ToBytes
                1s |> Conversion.int16ToBytes
            ]
            |> Array.concat
            |> Hashing.hash
            |> AccountHash

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAccountState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Accounts.Count = 1 @>
        test <@ output.Accounts.[accountHash] = {ControllerAddress = senderWallet.Address} @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // CreateAsset
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges CreateAsset`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "CreateAsset"
                    ActionData = CreateAssetTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        let assetHash =
            [
                Hashing.decode senderWallet.Address.Value
                nonce.Value |> Conversion.int64ToBytes
                1s |> Conversion.int16ToBytes
            ]
            |> Array.concat
            |> Hashing.hash
            |> AssetHash

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getHoldingState _ =
            None

        let getAssetState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetHoldingStateFromStorage = getHoldingState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedAssetState =
            {
                AssetState.AssetCode = None
                ControllerAddress = senderWallet.Address
                IsEligibilityRequired = false
            }

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets.Count = 1 @>
        test <@ output.Assets.[assetHash] = expectedAssetState @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetAccountController
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SetAccountController`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountController"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAccountState _ =
            Some {AccountState.ControllerAddress = senderWallet.Address}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Accounts.[accountHash] = {AccountState.ControllerAddress = newControllerWallet.Address} @>

    [<Fact>]
    let ``Processing.processChanges SetAccountController fails if account not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountController"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAccountState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.AccountNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Accounts = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SetAccountController fails if sender not current controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let accountHash = AccountHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAccountController"
                    ActionData =
                        {
                            AccountHash = accountHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAccountState _ =
            Some {AccountState.ControllerAddress = currentControllerWallet.Address}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAccountStateFromStorage = getAccountState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotSourceAccountController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Accounts.[accountHash] <> {AccountState.ControllerAddress = newControllerWallet.Address} @>
        test <@ output.Accounts.[accountHash] = {AccountState.ControllerAddress = currentControllerWallet.Address} @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetAssetController
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SetAssetController`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetController"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets.[assetHash].ControllerAddress = newControllerWallet.Address @>

    [<Fact>]
    let ``Processing.processChanges SetAssetController fails if asset not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetController"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SetAssetController fails if sender not current controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let newControllerWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Acc1"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetController"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            ControllerAddress = newControllerWallet.Address.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = currentControllerWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets.[assetHash].ControllerAddress <> newControllerWallet.Address @>
        test <@ output.Assets.[assetHash].ControllerAddress = currentControllerWallet.Address @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetAssetCode
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges SetAssetCode`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Foo"
        let assetCode = AssetCode "BAR"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetCode"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            AssetCode = assetCode.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            Some {AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = false}

        let getAssetHashByCode _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetAssetHashByCodeFromStorage = getAssetHashByCode
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets.[assetHash].AssetCode = Some assetCode @>

    [<Fact>]
    let ``Processing.processChanges SetAssetCode fails if asset not found`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Foo"
        let assetCode = AssetCode "BAR"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetCode"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            AssetCode = assetCode.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            None

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.AssetNotFound)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges SetAssetCode fails if assetCode already exists`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Foo"
        let assetCode = AssetCode "BAR"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetCode"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            AssetCode = assetCode.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = senderWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getAssetHashByCode _ =
            AssetHash "BLA" |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetAssetHashByCodeFromStorage = getAssetHashByCode
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.AssetCodeAlreadyExists)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.Assets.[assetHash].AssetCode = None @>

    [<Fact>]
    let ``Processing.processChanges SetAssetCode fails if sender not current controller`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let currentControllerWallet = Signing.generateWallet ()
        let assetHash = AssetHash "Foo"
        let assetCode = AssetCode "BAR"

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "SetAssetCode"
                    ActionData =
                        {
                            AssetHash = assetHash.Value
                            AssetCode = assetCode.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getAssetState _ =
            {
                AssetState.AssetCode = None
                ControllerAddress = currentControllerWallet.Address
                IsEligibilityRequired = false
            }
            |> Some

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetAssetStateFromStorage = getAssetState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.SenderIsNotAssetController)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Assets.[assetHash].AssetCode = None @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // ConfigureValidator
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges ConfigureValidator - updating existing config`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let newSharedRewardPercent = 10m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ConfigureValidator"
                    ActionData =
                        {
                            ConfigureValidatorTxActionDto.NetworkAddress = newNetworkAddress.Value
                            SharedRewardPercent = newSharedRewardPercent
                            IsEnabled = false
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            Some {
                ValidatorState.NetworkAddress = NetworkAddress "old-address:12345"
                SharedRewardPercent = 6m
                TimeToLockDeposit = 0s
                TimeToBlacklist = 0s
                IsEnabled = true
            }

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

        let validatorState, validatorChange = output.Validators.[senderWallet.Address]
        test <@ validatorState.NetworkAddress = newNetworkAddress @>
        test <@ validatorState.SharedRewardPercent = newSharedRewardPercent @>
        test <@ validatorState.IsEnabled = false @>
        test <@ validatorChange = ValidatorChange.Update @>

    [<Fact>]
    let ``Processing.processChanges ConfigureValidator - inserting new config`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let newSharedRewardPercent = 10m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "ConfigureValidator"
                    ActionData =
                        {
                            ConfigureValidatorTxActionDto.NetworkAddress = newNetworkAddress.Value
                            SharedRewardPercent = newSharedRewardPercent
                            IsEnabled = true
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

        let validatorState, validatorChange = output.Validators.[senderWallet.Address]
        test <@ validatorState.NetworkAddress = newNetworkAddress @>
        test <@ validatorState.SharedRewardPercent = newSharedRewardPercent @>
        test <@ validatorChange = ValidatorChange.Add @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveValidator
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Processing.processChanges RemoveValidator`` () =
        // INIT STATE
        let senderValidatorWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let stakerAddress1, stakerAddress2 = BlockchainAddress "AAA", BlockchainAddress "BBB"

        let initialChxState =
            [
                senderValidatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "RemoveValidator"
                    ActionData = RemoveValidatorTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderValidatorWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            {
                ValidatorState.NetworkAddress = newNetworkAddress
                SharedRewardPercent = 5m
                TimeToLockDeposit = 0s
                TimeToBlacklist = 0s
                IsEnabled = true
            }
            |> Some

        let getStakeState (stakerAddress, _) =
            if stakerAddress = stakerAddress1 then
                {StakeState.Amount = ChxAmount 100m}
            elif stakerAddress = stakerAddress2 then
                {StakeState.Amount = ChxAmount 80m}
            else
                failwithf "getStakeState should not be called for %A" stakerAddress
            |> Some

        let getStakers _ =
            [
                stakerAddress1
                stakerAddress2
            ]

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetStakersFromStorage = getStakers
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderValidatorWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

        let validatorState, validatorChange = output.Validators.[senderValidatorWallet.Address]
        test <@ validatorState.NetworkAddress = newNetworkAddress @>
        test <@ validatorChange = ValidatorChange.Remove @>
        test <@ output.Stakes.[(stakerAddress1, senderValidatorWallet.Address)].Amount = ChxAmount 0m @>
        test <@ output.Stakes.[(stakerAddress2, senderValidatorWallet.Address)].Amount = ChxAmount 0m @>

    [<Fact>]
    let ``Processing.processChanges RemoveValidator fails if validator not found`` () =
        // INIT STATE
        let senderValidatorWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let stakerAddress1, stakerAddress2 = BlockchainAddress "AAA", BlockchainAddress "BBB"

        let initialChxState =
            [
                senderValidatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "RemoveValidator"
                    ActionData = RemoveValidatorTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderValidatorWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState (stakerAddress, _) =
            if stakerAddress = stakerAddress1 then
                {StakeState.Amount = ChxAmount 100m}
            elif stakerAddress = stakerAddress2 then
                {StakeState.Amount = ChxAmount 80m}
            else
                failwithf "getStakeState should not be called for %A" stakerAddress
            |> Some

        let getStakers _ =
            [
                stakerAddress1
                stakerAddress2
            ]

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetStakersFromStorage = getStakers
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderValidatorWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.ValidatorNotFound) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    [<Fact>]
    let ``Processing.processChanges RemoveValidator fails if validator is blacklisted`` () =
        // INIT STATE
        let senderValidatorWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let stakerAddress1, stakerAddress2 = BlockchainAddress "AAA", BlockchainAddress "BBB"

        let initialChxState =
            [
                senderValidatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "RemoveValidator"
                    ActionData = RemoveValidatorTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderValidatorWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            {
                ValidatorState.NetworkAddress = newNetworkAddress
                SharedRewardPercent = 5m
                TimeToLockDeposit = 0s
                TimeToBlacklist = 2s
                IsEnabled = true
            }
            |> Some

        let getStakeState (stakerAddress, _) =
            if stakerAddress = stakerAddress1 then
                {StakeState.Amount = ChxAmount 100m}
            elif stakerAddress = stakerAddress2 then
                {StakeState.Amount = ChxAmount 80m}
            else
                failwithf "getStakeState should not be called for %A" stakerAddress
            |> Some

        let getStakers _ =
            [
                stakerAddress1
                stakerAddress2
            ]

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetStakersFromStorage = getStakers
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderValidatorWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.ValidatorIsBlacklisted) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    [<Fact>]
    let ``Processing.processChanges RemoveValidator fails if validator deposit is locked`` () =
        // INIT STATE
        let senderValidatorWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let newNetworkAddress = NetworkAddress "localhost:5000"
        let stakerAddress1, stakerAddress2 = BlockchainAddress "AAA", BlockchainAddress "BBB"

        let initialChxState =
            [
                senderValidatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "RemoveValidator"
                    ActionData = RemoveValidatorTxActionDto()
                } :> obj
            ]
            |> Helpers.newTx senderValidatorWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            {
                ValidatorState.NetworkAddress = newNetworkAddress
                SharedRewardPercent = 5m
                TimeToLockDeposit = 2s
                TimeToBlacklist = 0s
                IsEnabled = true
            }
            |> Some

        let getStakeState (stakerAddress, _) =
            if stakerAddress = stakerAddress1 then
                {StakeState.Amount = ChxAmount 100m}
            elif stakerAddress = stakerAddress2 then
                {StakeState.Amount = ChxAmount 80m}
            else
                failwithf "getStakeState should not be called for %A" stakerAddress
            |> Some

        let getStakers _ =
            [
                stakerAddress1
                stakerAddress2
            ]

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetStakersFromStorage = getStakers
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderValidatorWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedStatus = (TxActionNumber 1s, TxErrorCode.ValidatorDepositLocked) |> TxActionError |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedStatus @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderValidatorWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // DelegateStake
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Theory>]
    [<InlineData(40, 10, 50)>]
    [<InlineData(40, -10, 30)>]
    let ``Processing.processChanges DelegateStake - increasing existing stake``
        (currentStakeAmount, stakeChangeAmount, newStakeAmount)
        =

        // INIT STATE
        let currentStakeAmount = currentStakeAmount |> decimal |> ChxAmount
        let stakeChangeAmount = stakeChangeAmount |> decimal |> ChxAmount
        let newStakeAmount = newStakeAmount |> decimal |> ChxAmount
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let stakeValidatorAddress = (Signing.generateWallet ()).Address

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "DelegateStake"
                    ActionData =
                        {
                            ValidatorAddress = stakeValidatorAddress.Value
                            Amount = stakeChangeAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState _ =
            Some {StakeState.Amount = currentStakeAmount}

        let getTotalChxStaked _ = currentStakeAmount

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Stakes.[senderWallet.Address, stakeValidatorAddress].Amount = newStakeAmount @>

    [<Fact>]
    let ``Processing.processChanges DelegateStake - setting new stake`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let stakeValidatorAddress = (Signing.generateWallet ()).Address
        let stakeAmount = ChxAmount 10m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "DelegateStake"
                    ActionData =
                        {
                            ValidatorAddress = stakeValidatorAddress.Value
                            Amount = stakeAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = Success @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Stakes.[senderWallet.Address, stakeValidatorAddress].Amount = stakeAmount @>

    [<Fact>]
    let ``Processing.processChanges DelegateStake - staking more than available balance`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let stakeValidatorAddress = (Signing.generateWallet ()).Address
        let stakeAmount = ChxAmount 101m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "DelegateStake"
                    ActionData =
                        {
                            ValidatorAddress = stakeValidatorAddress.Value
                            Amount = stakeAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState _ =
            None

        let getTotalChxStaked _ =
            ChxAmount 0m

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.InsufficientChxBalance)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Stakes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges DelegateStake - staking more than available balance due to the staked CHX`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let stakeValidatorAddress = (Signing.generateWallet ()).Address
        let stakeAmount = ChxAmount 40m
        let currentStakeAmount = ChxAmount 60m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "DelegateStake"
                    ActionData =
                        {
                            ValidatorAddress = stakeValidatorAddress.Value
                            Amount = stakeAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState _ =
            Some {StakeState.Amount = currentStakeAmount}

        let getTotalChxStaked _ = currentStakeAmount

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.InsufficientChxBalance)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Stakes = Map.empty @>

    [<Fact>]
    let ``Processing.processChanges DelegateStake - decreasing stake for more than already staked`` () =
        // INIT STATE
        let senderWallet = Signing.generateWallet ()
        let validatorWallet = Signing.generateWallet ()
        let stakeValidatorAddress = (Signing.generateWallet ()).Address
        let stakeAmount = ChxAmount -51m
        let currentStakeAmount = ChxAmount 50m

        let initialChxState =
            [
                senderWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 10L}
                validatorWallet.Address, {ChxBalanceState.Amount = ChxAmount 100m; Nonce = Nonce 30L}
            ]
            |> Map.ofList

        // PREPARE TX
        let nonce = Nonce 11L
        let actionFee = ChxAmount 1m

        let txHash, txEnvelope =
            [
                {
                    ActionType = "DelegateStake"
                    ActionData =
                        {
                            ValidatorAddress = stakeValidatorAddress.Value
                            Amount = stakeAmount.Value
                        }
                } :> obj
            ]
            |> Helpers.newTx senderWallet nonce actionFee

        let txSet = [txHash]
        let blockNumber = BlockNumber 1L;

        // COMPOSE
        let getTx _ =
            Ok txEnvelope

        let getChxBalanceState address =
            initialChxState |> Map.tryFind address

        let getLockedAndBlacklistedValidators _ =
            []

        let getValidatorState _ =
            None

        let getStakeState _ =
            Some {StakeState.Amount = currentStakeAmount}

        let getTotalChxStaked _ = currentStakeAmount

        let getTopStakers _ = []

        // ACT
        let output =
            { processChangesMockedDeps with
                GetTx = getTx
                GetChxBalanceStateFromStorage = getChxBalanceState
                GetLockedAndBlacklistedValidators = getLockedAndBlacklistedValidators
                GetValidatorStateFromStorage = getValidatorState
                GetStakeStateFromStorage = getStakeState
                GetTotalChxStakedFromStorage = getTotalChxStaked
                GetTopStakers = getTopStakers
                ValidatorAddress = validatorWallet.Address
                BlockNumber = blockNumber
                TxSet = txSet
            }
            |> processChanges

        // ASSERT
        let senderChxBalance = initialChxState.[senderWallet.Address].Amount - actionFee
        let validatorChxBalance = initialChxState.[validatorWallet.Address].Amount + actionFee
        let expectedTxStatus =
            (TxActionNumber 1s, TxErrorCode.InsufficientStake)
            |> TxActionError
            |> Failure

        test <@ output.TxResults.Count = 1 @>
        test <@ output.TxResults.[txHash].Status = expectedTxStatus @>
        test <@ output.ChxBalances.[senderWallet.Address].Nonce = nonce @>
        test <@ output.ChxBalances.[validatorWallet.Address].Nonce = initialChxState.[validatorWallet.Address].Nonce @>
        test <@ output.ChxBalances.[senderWallet.Address].Amount = senderChxBalance @>
        test <@ output.ChxBalances.[validatorWallet.Address].Amount = validatorChxBalance @>
        test <@ output.Stakes.[senderWallet.Address, stakeValidatorAddress].Amount = currentStakeAmount @>
