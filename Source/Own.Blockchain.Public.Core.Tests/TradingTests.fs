namespace Own.Blockchain.Public.Core.Tests

open System
open Xunit
open Swensen.Unquote
open Own.Common.FSharp
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Core.Dtos
open Own.Blockchain.Public.Crypto

module TradingTests =

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let private createTradeOrderHash senderAddress nonce actionNumber =
        Hashing.deriveHash senderAddress (Nonce nonce) (TxActionNumber actionNumber) |> TradeOrderHash

    let private initHolding
        (controllerAddress : BlockchainAddress)
        (accountHash : AccountHash, assetHash : AssetHash, amount, isSecondaryEligible : bool)
        =

        (accountHash, assetHash),
        {|
            HoldingState =
                {
                    HoldingState.Balance = AssetAmount amount
                    IsEmission = false
                }
            EligibilityState =
                {
                    EligibilityState.Eligibility =
                        {
                            Eligibility.IsPrimaryEligible = true
                            IsSecondaryEligible = isSecondaryEligible
                        }
                    KycControllerAddress = controllerAddress
                }
        |}

    let private createTradeOrderState
        (baseAssetHash, quoteAssetHash)
        (blockNumber, txPosition, actionNumber)
        accountHash
        (side, amount, orderType, limitPrice, stopPrice, trailingOffset, trailingOffsetIsPercentage, timeInForce)
        (isExecutable, amountFilled, orderStatus)
        =

        // Just a dummy timestamp
        let blockTimestamp =
            DateTimeOffset.FromUnixTimeMilliseconds(Utils.getMachineTimestamp ())
                .AddHours(-1.)
                .AddMinutes(float blockNumber)
                .ToUnixTimeMilliseconds()

        let expirationTimestamp =
            DateTimeOffset.FromUnixTimeMilliseconds(blockTimestamp)
                .AddHours(3.)
                .ToUnixTimeMilliseconds()

        {
            TradeOrderState.BlockTimestamp = Timestamp blockTimestamp
            BlockNumber = BlockNumber blockNumber
            TxPosition = txPosition
            ActionNumber = TxActionNumber actionNumber
            AccountHash = accountHash
            BaseAssetHash = baseAssetHash
            QuoteAssetHash = quoteAssetHash
            Side = side
            Amount = AssetAmount amount
            OrderType = orderType
            LimitPrice = AssetAmount limitPrice
            StopPrice = AssetAmount stopPrice
            TrailingOffset = AssetAmount trailingOffset
            TrailingOffsetIsPercentage = trailingOffsetIsPercentage
            TimeInForce = timeInForce
            ExpirationTimestamp = Timestamp expirationTimestamp
            IsExecutable = isExecutable
            AmountFilled = AssetAmount amountFilled
            Status = orderStatus
        }

    let private placeOrder
        (baseAssetHash, quoteAssetHash)
        accountHash
        (side, amount, orderType, limitPrice, stopPrice, trailingOffset, trailingOffsetIsPercentage, timeInForce)
        =

        {
            PlaceTradeOrderTxActionDto.AccountHash = accountHash
            BaseAssetHash = baseAssetHash
            QuoteAssetHash = quoteAssetHash
            Side = side
            Amount = amount
            OrderType = orderType
            LimitPrice = limitPrice
            StopPrice = stopPrice
            TrailingOffset = trailingOffset
            TrailingOffsetIsPercentage = trailingOffsetIsPercentage
            TimeInForce = timeInForce
        }

    let private matchOrders blockNumber senderWallet holdings existingOrders incomingOrders =
        // INIT STATE
        let validatorWallet = Signing.generateWallet ()

        let initialChxState =
            [
                senderWallet.Address, { ChxAddressState.Nonce = Nonce 0L; Balance = ChxAmount 10m }
                validatorWallet.Address, { ChxAddressState.Nonce = Nonce 4L; Balance = ChxAmount 10000m }
            ]
            |> Map.ofList

        let holdings =
            holdings
            |> List.map (initHolding senderWallet.Address)
            |> Map.ofList

        let existingOrders = existingOrders |> List.map (fun o -> Helpers.randomHash () |> TradeOrderHash, o)
        let existingOrderHashes = existingOrders |> List.map fst
        let existingOrders = existingOrders |> Map.ofList

        let incomingOrderHashes =
            incomingOrders
            |> List.mapi (fun txIndex actions ->
                let nonce = int64 txIndex + 1L
                actions
                |> List.mapi (fun actionIndex _ ->
                    let actionNumber = Convert.ToInt16 actionIndex + 1s
                    createTradeOrderHash senderWallet.Address nonce actionNumber
                )
            )
            |> List.concat

        // PREPARE TX
        let actionFee = ChxAmount 0.01m

        let txs =
            incomingOrders
            |> List.mapi (fun i actions ->
                let nonce = int64 i + 1L |> Nonce
                actions
                |> List.map (fun action -> { ActionType = "PlaceTradeOrder"; ActionData = action })
                |> Helpers.newTx senderWallet nonce (Timestamp 0L) actionFee
            )

        let txSet = txs |> List.map fst

        let txs = txs |> Map.ofList

        // COMPOSE
        let getTx =
            txs
            |> flip Map.find
            >> Ok

        let getChxAddressState =
            initialChxState
            |> flip Map.tryFind

        let getAccountState _ =
            Some { AccountState.ControllerAddress = senderWallet.Address }

        let getAssetState _ =
            Some { AssetState.AssetCode = None; ControllerAddress = senderWallet.Address; IsEligibilityRequired = true }

        let getHoldingState =
            holdings
            |> flip Map.tryFind
            >> Option.map (fun v -> v.HoldingState)

        let getEligibilityState _ =
            {
                EligibilityState.KycControllerAddress = senderWallet.Address
                Eligibility =
                    {
                        IsPrimaryEligible = true
                        IsSecondaryEligible = true
                    }
            }
            |> Some

        let getTradingPairState _ =
            Some {
                TradingPairState.IsEnabled = true
                LastPrice = AssetAmount 0m
                PriceChange = AssetAmount 0m
            }

        let getTradeOrderState =
            existingOrders
            |> flip Map.tryFind

        let getTradeOrdersFromStorage _ =
            existingOrders
            |> Map.toList
            |> List.map Mapping.tradeOrderStateToInfo

        let getHoldingInTradeOrdersFromStorage _ =
            AssetAmount 0m

        let output =
            { Helpers.processChangesMockedDeps with
                GetTx = getTx
                GetChxAddressStateFromStorage = getChxAddressState
                GetAccountStateFromStorage = getAccountState
                GetAssetStateFromStorage = getAssetState
                GetHoldingStateFromStorage = getHoldingState
                GetEligibilityStateFromStorage = getEligibilityState
                GetTradingPairStateFromStorage = getTradingPairState
                GetTradeOrderStateFromStorage = getTradeOrderState
                GetTradeOrdersFromStorage = getTradeOrdersFromStorage
                GetHoldingInTradeOrdersFromStorage = getHoldingInTradeOrdersFromStorage
                ValidatorAddress = validatorWallet.Address
                TxSet = txSet
                BlockNumber = blockNumber
            }
            |> Helpers.processChanges

        existingOrderHashes, incomingOrderHashes, output

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Tests
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Matching - LIMIT BUY`` () =
        // ARRANGE
        let senderWallet = Signing.generateWallet ()
        let accountHash1 = Helpers.randomHash () |> AccountHash
        let accountHash2 = Helpers.randomHash () |> AccountHash
        let baseAssetHash = Helpers.randomHash () |> AssetHash
        let quoteAssetHash = Helpers.randomHash () |> AssetHash

        let createTradeOrderState = createTradeOrderState (baseAssetHash, quoteAssetHash)
        let placeOrder = placeOrder (baseAssetHash.Value, quoteAssetHash.Value)

        let holdings =
            [
                accountHash1, baseAssetHash, 1000m, true
                accountHash2, quoteAssetHash, 2000m, true
            ]

        let existingOrders =
            [
                createTradeOrderState
                    (1L, 1, 1s)
                    accountHash1
                    (Sell, 100m, TradeOrderType.Limit, 5m, 0m, 0m, false, GoodTilCancelled)
                    (true, 0m, TradeOrderStatus.Open)
            ]

        let incomingOrders =
            [
                [ // TX1
                    placeOrder accountHash2.Value ("BUY", 100m, "LIMIT", 5m, 0m, 0m, false, "GTC")
                ]
            ]

        // ACT
        let existingOrderHashes, incomingOrderHashes, output =
            matchOrders (BlockNumber 2L) senderWallet holdings existingOrders incomingOrders

        // ASSERT
        test <@ output.TxResults.Count = incomingOrders.Length @>
        for txResult in output.TxResults |> Map.values do
            test <@ txResult.Status = Success @>

        test <@ output.TradeOrders.Count = 2 @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[0]]
        test <@ tradeOrderState.IsExecutable = true @>
        test <@ tradeOrderState.AmountFilled = tradeOrderState.Amount @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[0]]
        test <@ tradeOrderState.IsExecutable = true @>
        test <@ tradeOrderState.AmountFilled = tradeOrderState.Amount @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        test <@ output.Holdings.[accountHash1, baseAssetHash].Balance.Value = 900m @>
        test <@ output.Holdings.[accountHash2, baseAssetHash].Balance.Value = 100m @>
        test <@ output.Holdings.[accountHash1, quoteAssetHash].Balance.Value = 500m @>
        test <@ output.Holdings.[accountHash2, quoteAssetHash].Balance.Value = 1500m @>

    [<Fact>]
    let ``Matching - Stop and Limit price in TRAILING orders follows price`` () =
        // ARRANGE
        let senderWallet = Signing.generateWallet ()
        let accountHash1 = Helpers.randomHash () |> AccountHash
        let accountHash2 = Helpers.randomHash () |> AccountHash
        let baseAssetHash = Helpers.randomHash () |> AssetHash
        let quoteAssetHash = Helpers.randomHash () |> AssetHash

        let createTradeOrderState = createTradeOrderState (baseAssetHash, quoteAssetHash)
        let placeOrder = placeOrder (baseAssetHash.Value, quoteAssetHash.Value)

        let holdings =
            [
                accountHash1, baseAssetHash, 1000m, true
                accountHash2, quoteAssetHash, 2000m, true
            ]

        let existingOrders =
            [
                createTradeOrderState
                    (1L, 1, 1s)
                    accountHash1
                    (Sell, 100m, TradeOrderType.TrailingStopMarket, 0m, 3m, 1m, false, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
                createTradeOrderState
                    (1L, 2, 1s)
                    accountHash1
                    (Buy, 100m, TradeOrderType.TrailingStopMarket, 0m, 7m, 1m, false, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
                createTradeOrderState
                    (1L, 1, 1s)
                    accountHash1
                    (Sell, 100m, TradeOrderType.TrailingStopLimit, 2.5m, 3m, 1m, false, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
                createTradeOrderState
                    (1L, 2, 1s)
                    accountHash1
                    (Buy, 100m, TradeOrderType.TrailingStopLimit, 7.5m, 7m, 1m, false, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
                createTradeOrderState
                    (1L, 1, 1s)
                    accountHash1
                    (Sell, 100m, TradeOrderType.TrailingStopLimit, 2.5m, 3m, 20m, true, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
                createTradeOrderState
                    (1L, 2, 1s)
                    accountHash1
                    (Buy, 100m, TradeOrderType.TrailingStopLimit, 7.5m, 7m, 20m, true, ImmediateOrCancel)
                    (false, 0m, TradeOrderStatus.Open)
            ]

        let incomingOrders =
            [
                [ // TX1
                    placeOrder accountHash1.Value ("SELL", 100m, "LIMIT", 5m, 0m, 0m, false, "GTC")
                ]
                [ // TX2
                    placeOrder accountHash2.Value ("BUY", 100m, "LIMIT", 5m, 0m, 0m, false, "GTC")
                ]
            ]

        // ACT
        let existingOrderHashes, incomingOrderHashes, output =
            matchOrders (BlockNumber 2L) senderWallet holdings existingOrders incomingOrders

        // ASSERT
        test <@ output.TxResults.Count = incomingOrders.Length @>
        for txResult in output.TxResults |> Map.values do
            test <@ txResult.Status = Success @>

        test <@ output.TradeOrders.Count = 8 @>

        // Old orders
        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[0]]
        test <@ tradeOrderState.LimitPrice.Value = 0m @>
        test <@ tradeOrderState.StopPrice.Value = 4m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[1]]
        test <@ tradeOrderState.LimitPrice.Value = 0m @>
        test <@ tradeOrderState.StopPrice.Value = 6m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[2]]
        test <@ tradeOrderState.LimitPrice.Value = 3.5m @>
        test <@ tradeOrderState.StopPrice.Value = 4m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[3]]
        test <@ tradeOrderState.LimitPrice.Value = 6.5m @>
        test <@ tradeOrderState.StopPrice.Value = 6m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[4]]
        test <@ tradeOrderState.LimitPrice.Value = 3.5m @>
        test <@ tradeOrderState.StopPrice.Value = 4m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[5]]
        test <@ tradeOrderState.LimitPrice.Value = 6.5m @>
        test <@ tradeOrderState.StopPrice.Value = 6m @>
        test <@ tradeOrderState.IsExecutable = false @>
        test <@ tradeOrderState.AmountFilled.Value = 0m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        // New orders
        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[0]]
        test <@ tradeOrderState.IsExecutable = true @>
        test <@ tradeOrderState.AmountFilled = tradeOrderState.Amount @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[1]]
        test <@ tradeOrderState.IsExecutable = true @>
        test <@ tradeOrderState.AmountFilled = tradeOrderState.Amount @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        test <@ output.Holdings.[accountHash1, baseAssetHash].Balance.Value = 900m @>
        test <@ output.Holdings.[accountHash2, baseAssetHash].Balance.Value = 100m @>
        test <@ output.Holdings.[accountHash1, quoteAssetHash].Balance.Value = 500m @>
        test <@ output.Holdings.[accountHash2, quoteAssetHash].Balance.Value = 1500m @>

    [<Fact>]
    let ``Matching - Fill MARKET order up to available quote asset balance`` () =
        // ARRANGE
        let senderWallet = Signing.generateWallet ()
        let accountHash1 = Helpers.randomHash () |> AccountHash
        let accountHash2 = Helpers.randomHash () |> AccountHash
        let baseAssetHash = Helpers.randomHash () |> AssetHash
        let quoteAssetHash = Helpers.randomHash () |> AssetHash

        let createTradeOrderState = createTradeOrderState (baseAssetHash, quoteAssetHash)
        let placeOrder = placeOrder (baseAssetHash.Value, quoteAssetHash.Value)

        let holdings =
            [
                accountHash1, baseAssetHash, 1000m, true
                accountHash2, quoteAssetHash, 2000m, true
            ]

        let existingOrders =
            [
                createTradeOrderState
                    (1L, 1, 1s)
                    accountHash1
                    (Sell, 1000m, TradeOrderType.Limit, 5m, 0m, 0m, false, GoodTilCancelled)
                    (true, 0m, TradeOrderStatus.Open)
            ]

        let incomingOrders =
            [
                [ // TX1
                    placeOrder accountHash2.Value ("BUY", 1000m, "MARKET", 0m, 0m, 0m, false, "IOC")
                ]
            ]

        // ACT
        let existingOrderHashes, incomingOrderHashes, output =
            matchOrders (BlockNumber 2L) senderWallet holdings existingOrders incomingOrders

        // ASSERT
        test <@ output.TxResults.Count = incomingOrders.Length @>
        for txResult in output.TxResults |> Map.values do
            test <@ txResult.Status = Success @>

        test <@ output.TradeOrders.Count = 2 @>

        let amountFilled = 400m

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[existingOrderHashes.[0]]
        test <@ tradeOrderState.AmountFilled.Value = amountFilled @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Update @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[0]]
        test <@ tradeOrderState.AmountFilled.Value = amountFilled @>
        TradeOrderStatus.Cancelled TradeOrderCancelReason.InsufficientQuoteAssetBalance
        |> fun s -> test <@ tradeOrderState.Status = s @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        test <@ output.Trades.Length = 1 @>

        test <@ output.Trades.[0].Direction = TradeOrderSide.Buy @>
        test <@ output.Trades.[0].BuyOrderHash = incomingOrderHashes.[0] @>
        test <@ output.Trades.[0].SellOrderHash = existingOrderHashes.[0] @>
        test <@ output.Trades.[0].Amount.Value = amountFilled @>
        test <@ output.Trades.[0].Price.Value = 5m @>

        test <@ output.Holdings.[accountHash1, baseAssetHash].Balance.Value = 600m @>
        test <@ output.Holdings.[accountHash2, baseAssetHash].Balance.Value = 400m @>
        test <@ output.Holdings.[accountHash1, quoteAssetHash].Balance.Value = 2000m @>
        test <@ output.Holdings.[accountHash2, quoteAssetHash].Balance.Value = 0m @>

    [<Fact>]
    let ``Matching - Last trade price and positive price change stored in trading pair`` () =
        // ARRANGE
        let senderWallet = Signing.generateWallet ()
        let accountHash1 = Helpers.randomHash () |> AccountHash
        let accountHash2 = Helpers.randomHash () |> AccountHash
        let baseAssetHash = Helpers.randomHash () |> AssetHash
        let quoteAssetHash = Helpers.randomHash () |> AssetHash

        let createTradeOrderState = createTradeOrderState (baseAssetHash, quoteAssetHash)
        let placeOrder = placeOrder (baseAssetHash.Value, quoteAssetHash.Value)

        let holdings =
            [
                accountHash1, baseAssetHash, 1000m, true
                accountHash2, quoteAssetHash, 2000m, true
            ]

        let existingOrders = []

        let incomingOrders =
            [
                [ // TX1
                    placeOrder accountHash1.Value ("SELL", 100m, "LIMIT", 5.5m, 0m, 0m, false, "GTC")
                    placeOrder accountHash1.Value ("SELL", 100m, "LIMIT", 5m, 0m, 0m, false, "GTC")
                ]
                [ // TX2
                    placeOrder accountHash2.Value ("BUY", 120m, "MARKET", 0m, 0m, 0m, false, "IOC")
                ]
            ]

        // ACT
        let existingOrderHashes, incomingOrderHashes, output =
            matchOrders (BlockNumber 2L) senderWallet holdings existingOrders incomingOrders

        // ASSERT
        test <@ output.TxResults.Count = incomingOrders.Length @>
        for txResult in output.TxResults |> Map.values do
            test <@ txResult.Status = Success @>

        test <@ output.TradeOrders.Count = 3 @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[0]]
        test <@ tradeOrderState.AmountFilled.Value = 20m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Add @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[1]]
        test <@ tradeOrderState.AmountFilled.Value = 100m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[2]]
        test <@ tradeOrderState.AmountFilled.Value = 120m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        test <@ output.Trades.Length = 2 @>

        test <@ output.Trades.[0].Direction = TradeOrderSide.Buy @>
        test <@ output.Trades.[0].BuyOrderHash = incomingOrderHashes.[2] @>
        test <@ output.Trades.[0].SellOrderHash = incomingOrderHashes.[1] @>
        test <@ output.Trades.[0].Amount.Value = 100m @>
        test <@ output.Trades.[0].Price.Value = 5m @>

        test <@ output.Trades.[1].Direction = TradeOrderSide.Buy @>
        test <@ output.Trades.[1].BuyOrderHash = incomingOrderHashes.[2] @>
        test <@ output.Trades.[1].SellOrderHash = incomingOrderHashes.[0] @>
        test <@ output.Trades.[1].Amount.Value = 20m @>
        test <@ output.Trades.[1].Price.Value = 5.5m @>

        test <@ output.Holdings.[accountHash1, baseAssetHash].Balance.Value = 880m @>
        test <@ output.Holdings.[accountHash2, baseAssetHash].Balance.Value = 120m @>
        test <@ output.Holdings.[accountHash1, quoteAssetHash].Balance.Value = 610m @>
        test <@ output.Holdings.[accountHash2, quoteAssetHash].Balance.Value = 1390m @>

        test <@ output.TradingPairs.[baseAssetHash, quoteAssetHash].LastPrice.Value = 5.5m @>
        test <@ output.TradingPairs.[baseAssetHash, quoteAssetHash].PriceChange.Value = 0.5m @>

    [<Fact>]
    let ``Matching - Last trade price and negative price change stored in trading pair`` () =
        // ARRANGE
        let senderWallet = Signing.generateWallet ()
        let accountHash1 = Helpers.randomHash () |> AccountHash
        let accountHash2 = Helpers.randomHash () |> AccountHash
        let baseAssetHash = Helpers.randomHash () |> AssetHash
        let quoteAssetHash = Helpers.randomHash () |> AssetHash

        let createTradeOrderState = createTradeOrderState (baseAssetHash, quoteAssetHash)
        let placeOrder = placeOrder (baseAssetHash.Value, quoteAssetHash.Value)

        let holdings =
            [
                accountHash1, baseAssetHash, 1000m, true
                accountHash2, quoteAssetHash, 2000m, true
            ]

        let existingOrders = []

        let incomingOrders =
            [
                [ // TX1
                    placeOrder accountHash2.Value ("BUY", 100m, "LIMIT", 4.5m, 0m, 0m, false, "GTC")
                    placeOrder accountHash2.Value ("BUY", 100m, "LIMIT", 5m, 0m, 0m, false, "GTC")
                ]
                [ // TX2
                    placeOrder accountHash1.Value ("SELL", 120m, "LIMIT", 4m, 0m, 0m, false, "GTC")
                ]
            ]

        // ACT
        let existingOrderHashes, incomingOrderHashes, output =
            matchOrders (BlockNumber 2L) senderWallet holdings existingOrders incomingOrders

        // ASSERT
        test <@ output.TxResults.Count = incomingOrders.Length @>
        for txResult in output.TxResults |> Map.values do
            test <@ txResult.Status = Success @>

        test <@ output.TradeOrders.Count = 3 @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[0]]
        test <@ tradeOrderState.AmountFilled.Value = 20m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Open @>
        test <@ tradeOrderChange = TradeOrderChange.Add @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[1]]
        test <@ tradeOrderState.AmountFilled.Value = 100m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        let tradeOrderState, tradeOrderChange = output.TradeOrders.[incomingOrderHashes.[2]]
        test <@ tradeOrderState.AmountFilled.Value = 120m @>
        test <@ tradeOrderState.Status = TradeOrderStatus.Filled @>
        test <@ tradeOrderChange = TradeOrderChange.Remove @>

        test <@ output.Trades.Length = 2 @>

        test <@ output.Trades.[0].Direction = TradeOrderSide.Sell @>
        test <@ output.Trades.[0].BuyOrderHash = incomingOrderHashes.[1] @>
        test <@ output.Trades.[0].SellOrderHash = incomingOrderHashes.[2] @>
        test <@ output.Trades.[0].Amount.Value = 100m @>
        test <@ output.Trades.[0].Price.Value = 5m @>

        test <@ output.Trades.[1].Direction = TradeOrderSide.Sell @>
        test <@ output.Trades.[1].BuyOrderHash = incomingOrderHashes.[0] @>
        test <@ output.Trades.[1].SellOrderHash = incomingOrderHashes.[2] @>
        test <@ output.Trades.[1].Amount.Value = 20m @>
        test <@ output.Trades.[1].Price.Value = 4.5m @>

        test <@ output.Holdings.[accountHash1, baseAssetHash].Balance.Value = 880m @>
        test <@ output.Holdings.[accountHash2, baseAssetHash].Balance.Value = 120m @>
        test <@ output.Holdings.[accountHash1, quoteAssetHash].Balance.Value = 590m @>
        test <@ output.Holdings.[accountHash2, quoteAssetHash].Balance.Value = 1410m @>

        test <@ output.TradingPairs.[baseAssetHash, quoteAssetHash].LastPrice.Value = 4.5m @>
        test <@ output.TradingPairs.[baseAssetHash, quoteAssetHash].PriceChange.Value = -0.5m @>