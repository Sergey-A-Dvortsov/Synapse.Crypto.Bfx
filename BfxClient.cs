using Bitfinex.Net.Clients;
using Bitfinex.Net.Enums;
using Bitfinex.Net.Objects.Models;
using CryptoExchange.Net;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using NLog;
using NSec.Cryptography;
using Synapse.Crypto.Trading;
using Synapse.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Synapse.Crypto.Bfx
{
 
    public class BfxFastBook : FastBook
    {

        private readonly int decimals;

        public BfxFastBook(string symbol, int decimals = 5) : base(symbol, double.NaN)
        {
            this.decimals = (int)decimals;
            logger = LogManager.GetLogger($"BfxFastBook.{symbol}");
        }

        private DateTime updateTime;
        public override DateTime UpdateTime => updateTime;

        public bool Update(DataEvent<BitfinexOrderBookEntry[]> evnt)
        {
            updateTime = evnt.DataTime.GetValueOrDefault();
            Delay = DateTime.UtcNow - UpdateTime;

            if (evnt.UpdateType == SocketUpdateType.Snapshot)
                return UpdateWithSnapshot(evnt);
            else if (evnt.UpdateType == SocketUpdateType.Update)
                return UpdateWithEntry(evnt);
            return false;
        }

        /// <summary>
        /// Полностью обновляет массивы Asks и Bids при помощи снапшота книги заявок.
        /// </summary>
        /// <param name="ss">Orderbook snapshot</param>
        public bool UpdateWithSnapshot(DataEvent<BitfinexOrderBookEntry[]> evnt)
        {
            var asks = evnt.Data.Where(e => e.Count > 0 && e.Quantity < 0).Select(e => new Quote((double)e.Price, (double)Math.Abs(e.Quantity))).ToArray();
            var bids = evnt.Data.Where(e => e.Count > 0 && e.Quantity > 0).Select(e => new Quote((double)e.Price, (double)e.Quantity)).ToArray();

            //TODO сделать проверку на валидность данных в asks/bids. Если данные не валидны, то генерируем ошибку, выставляем Valid = false
            Valid = true;

            

            Dictionary<BookSides, double[]> prices = new()
            {
                {BookSides.Ask, [(double)asks.First().Price, (double)asks.Last().Price]},
                {BookSides.Bid, [(double)bids.First().Price, (double)bids.Last().Price]}
            };


            // Создаем массивы с шагом цены равным ticksize и дипазоном цен на величину offset больше/меньше текущих первой и последней цены снапшота 
            var result = UpdateWithSnapshot(prices);

            if (result == false) return false;

            // Заполняем массивы Asks и Bids полученными котировками из снапшота
            for (int i = 0; i < asks.Length; i++)
            {
                var idx = GetIndex(asks[i].Price, BookSides.Ask);
                Asks[idx] = new Quote(asks[i].Price, asks[i].Size);
            }

            for (int i = 0; i < bids.Length; i++)
            {
                var idx = GetIndex(bids[i].Price, BookSides.Bid);
                Bids[idx] = new Quote(bids[i].Price, bids[i].Size);
            }

            return true;

        }

        /// <summary>
        /// Обновляет массивы Asks и Bids при помощи измененных котировок .
        /// </summary>
        /// <param name="ss">Orderbook delta</param>
        public bool UpdateWithEntry(DataEvent<BitfinexOrderBookEntry[]> evnt)
        {
            var entries = evnt.Data;

            try
            {

                //TODO сделать проверку на валидность данных в asks/bids. Если данные не валидны, то генерируем ошибку, выставляем Valid = false
                Valid = true;

                for (int i = 0; i < entries.Length; i++)
                {
                    var side = entries[i].Quantity > 0 ? BookSides.Bid : BookSides.Ask;
                    var idx = GetIndex((double)entries[i].Price, side);
                    var size = entries[i].Quantity == 0 ? 0 : (double)Math.Abs(entries[i].Quantity);

                    if (side == BookSides.Ask)
                    {
                        Asks[idx] = new Quote((double)entries[i].Price, size);

                        if ((double)entries[i].Price < BestAsk.Price) // если изменилась цена лучшего Ask в сторону умешьшения
                        {
                           if (entries[i].Count == 0)
                           {
                                var index = Asks.FindIndex<Quote>(0, q => q.Size > 0);
                                if (index == null) throw new NullReferenceException(nameof(index));
                                BestAskIndex = index.Value;
                                logger.Warn($"Неоднозначная ситуация. Обновление лучшего Ask в сторону умешьшения с Size = 0. Найден новый лучший индекс {index}.");
                           }
                           else
                            {
                                BestAskIndex = idx;
                            }
                        }
                        else if ((double)entries[i].Price == BestAsk.Price) // если изменился размер лучшего Ask или цена в сторону увеличения 
                        {
                            if (entries[i].Count == 0) // изменилась цена лучшего Ask в сторону увеличения, ищем новый лучший аск 
                            {
                                var index = Asks.FindIndex<Quote>(idx + 1, q => q.Size > 0);
                                if (index == null)
                                    throw new NullReferenceException(nameof(index));
                                BestAskIndex = index.Value;
                            }
                        }

                       // Asks[idx] = new Quote((double)entries[i].Price, size);

                    }
                    else if (side == BookSides.Bid)
                    {

                        Bids[idx] = new Quote((double)entries[i].Price, size);

                        if ((double)entries[i].Price > BestBid.Price) // если изменилась цена лучшего Bid в сторону увеличения
                        {

                            if (entries[i].Count == 0)
                            {
                                var index = Bids.FindIndex<Quote>(0, q => q.Size > 0);
                                if (index == null) throw new NullReferenceException(nameof(index));
                                BestBidIndex = index.Value;
                                logger.Warn($"Неоднозначная ситуация. Обновление лучшего Bid в сторону увеличения с Size = 0. Найден новый лучший индекс {index}.");
                            }
                            else
                            {
                                BestBidIndex = idx;
                            }

                        }
                        else if ((double)entries[i].Price == BestBid.Price) // если изменился размер лучшего Bid или цена в сторону уменьшения
                        {
                            if (entries[i].Count == 0) // изменилась цена лучшего Bid в сторону уменьшения, ищем новый лучший Bid
                            {
                                var index = Bids.FindIndex<Quote>(idx + 1, q => q.Size > 0);
                                if (index == null)
                                    throw new NullReferenceException(nameof(index));
                                BestBidIndex = index.Value;
                            }
                        }

                        
                    }
                     
                    }

                return true;
            }
            catch (Exception ex)
            {
                logger.ToError(ex);
            }

            return false;
        }

        //Algorithm to create and keep a trading book instance updated
        //1. subscribe to channel
        //2. receive the book snapshot and create your in-memory book structure
        //when count > 0 then you have to add or update the price level
        //3.1 if amount > 0 then add/update bids
        //3.2 if amount< 0 then add/update asks
        //when count = 0 then you have to delete the price level.
        //4.1 if amount = 1 then remove from bids
        //4.2 if amount = -1 then remove from asks
    }

    public class BfxClient
    {
        //private BitfinexRestClient rest;
        //private ApiCredentials credencials;
        //private BitfinexSocketClient socket;

        public BfxClient()
        {
            Instance = this;
        }

        /// <summary>
        /// Event of fastbook update
        /// </summary>
        public event Action<FastBook> FastBookUpdate = delegate { };

        private void OnFastBookUpdate(FastBook book)
        {
            FastBookUpdate?.Invoke(book);
        }

        public static BfxClient Instance { get; private set; }

        public Dictionary<string, BfxFastBook> FastBooks = new();

        public async Task<UpdateSubscription> SubscribeToOrderBookAsync(string symbol, int depth = 25,
            Precision precision = Precision.PrecisionLevel0, Frequency frequency = Frequency.Realtime)
        {
            var socket = new BitfinexSocketClient();

            var bitfinsubscription = await socket.SpotApi.SubscribeToOrderBookUpdatesAsync(symbol, precision, frequency, depth,
                e =>
                {  
                    if (!FastBooks.ContainsKey(e.Symbol))
                    { 
                        FastBooks[symbol] = new BfxFastBook(symbol, 1); // TODO: задать правильный тиксайз
                    }

                    if(FastBooks[symbol].Update(e))
                    {
                        OnFastBookUpdate(FastBooks[symbol]);
                    }
                    

                });

            if (bitfinsubscription.Success)
            {
                return bitfinsubscription.Data;
            }
            else
            {
                throw new Exception("Could not subscribe to order book updates: " + bitfinsubscription.Error);
            }

        }

        public async Task<UpdateSubscription> SubscribeToOrderBookAsync(BitfinexSocketClient socket, string symbol, int depth = 25,
            Precision precision = Precision.PrecisionLevel0, Frequency frequency = Frequency.Realtime)
        {

            if (socket == null) throw new NullReferenceException(nameof(socket));
    
            var bitfinsubscription = await socket.SpotApi.SubscribeToOrderBookUpdatesAsync(symbol, precision, frequency, depth,
                e =>
                {
                    if (!FastBooks.ContainsKey(e.Symbol))
                    {
                        FastBooks[symbol] = new BfxFastBook(symbol, 1); // TODO: задать правильный тиксайз
                    }

                    if (FastBooks[symbol].Update(e))
                    {
                        OnFastBookUpdate(FastBooks[symbol]);
                    }


                });

            if (bitfinsubscription.Success)
            {
                return bitfinsubscription.Data;
            }
            else
            {
                throw new Exception("Could not subscribe to order book updates: " + bitfinsubscription.Error);
            }

        }



    }
}





//tickSize = 10 ^ (floor(log10(P)) - precision + 1)




//var subscription = await ExchangeHelpers.ProcessQueuedAsync(writer => binanceSocketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync("ETHUSDT", writer), async (update) =>
//{
//    // Process the update asynchronously
//});
//await Task.Delay(TimeSpan.FromSeconds(10));
//await subscription.Data.CloseAsync();