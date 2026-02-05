using Bitfinex.Net;
using Bitfinex.Net.Clients;
using Bitfinex.Net.Enums;
using Bitfinex.Net.Objects.Models;
using CryptoExchange.Net;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.SharedApis;
using NLog;
using NSec.Cryptography;
using Synapse.Crypto.Bfx;
using Synapse.Crypto.Trading;
using Synapse.General;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Synapse.Crypto.Bfx
{


    //using Microsoft.Extensions.Hosting; // Если в ASP.NET Core, иначе уберите
    //using Microsoft.Extensions.Logging;
    //using System;
    //using System.Collections.Concurrent;
    //using System.Threading.Channels;
    //using System.Threading.Tasks;

    public class OrderBookUpdate
    {
        public string Symbol { get; init; } // Тикер акции (AAPL, GOOG и т.д.)
        public string Data { get; init; }   // Пример: JSON с обновлением order book
                                            // Добавьте другие поля по необходимости (bid/ask levels и т.д.)
    }


    public class BookSubscription
    {
        public BookSubscription(string symbol, UpdateSubscription subscription)
        {
            Book = new BfxFastBook(symbol);
            Subscription = subscription;
        }

        public BfxFastBook Book { get; private set; }
        public UpdateSubscription Subscription { get; set; }
    }


    public class BfxClient
    {

        private readonly BitfinexRestClient rest;
        //private ApiCredentials credencials;
        private readonly BitfinexSocketClient socket;
        private BookUpdateTaskQueue bookQueue;

        public BfxClient()
        {
            rest = new();
            socket = new();
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

        public Dictionary<string, BookSubscription> FastBooks = [];

        public string GetSymbol(string baseAsset, string quoteAsset, TradingMode mode)
        {
            return BitfinexExchange.FormatSymbol(baseAsset, quoteAsset, mode);
        }

        //public async Task<CallResult<BitfinexTicker>> GetAssets()
        //{
        //    return await rest.SpotApi.ExchangeData.;
        //}


        #region websoket

        public async Task<UpdateSubscription> SubscribeToOrderBookAsync(string symbol, int depth = 25,
            Precision precision = Precision.PrecisionLevel0, Frequency frequency = Frequency.Realtime)
        {

            var bitfinsubscription = await socket.SpotApi.SubscribeToOrderBookUpdatesAsync(symbol, precision, frequency, depth,
                e =>
                {

                    if (!FastBooks.ContainsKey(e.Symbol))
                    {
                        if (e.UpdateType == SocketUpdateType.Snapshot)
                        {
                            if(!FastBooks.TryAdd(e.Symbol, new(e.Symbol, null)))
                            {
                                if (!FastBooks.ContainsKey(e.Symbol))
                                {
                                    throw new ArgumentException($"Failed to add {e.Symbol} to FastBooks.");
                                }
                            }
                        }
                        else
                         throw new ArgumentException($"FastBooks don't contains {e.Symbol}.");
                    }
                        
                    if (bookQueue == null)
                    {
                        if (e.UpdateType == SocketUpdateType.Snapshot)
                        {
                            bookQueue = new();
                        }
                        else
                            throw new NullReferenceException(nameof(bookQueue));

                    }

                    bookQueue.Enqueue(e, ProcessUpdate);

                });

            if (bitfinsubscription.Success)
            {
                if (!FastBooks.ContainsKey(symbol))
                {
                    if(!FastBooks.TryAdd(symbol, new(symbol, bitfinsubscription.Data)))
                    {
                        if (FastBooks.ContainsKey(symbol))
                            FastBooks[symbol].Subscription = bitfinsubscription.Data;
                    }
                }
                else if (FastBooks[symbol].Subscription == null)
                    FastBooks[symbol].Subscription = bitfinsubscription.Data;

                if (bookQueue == null)
                    bookQueue = new BookUpdateTaskQueue();

                return bitfinsubscription.Data;
            }
            else
            {
                throw new Exception("Could not subscribe to order book updates: " + bitfinsubscription.Error);
            }

        }


        /// Функция обработки (ваша логика обновления order book)
        async Task ProcessUpdate(DataEvent<BitfinexOrderBookEntry[]> update)
        {
            string symbol = update.Symbol;

            if (FastBooks[symbol].Book.Update(update))
                OnFastBookUpdate(FastBooks[symbol].Book);
        }


        #endregion

    }
}






//var queue = new PerSymbolTaskQueue();

//// Функция обработки (ваша логика обновления order book)
//async Task ProcessUpdate(OrderBookUpdate update)
//{
//    // Здесь: парсинг данных, обновление in-memory order book для символа
//    Console.WriteLine($"Processed update for {update.Symbol}: {update.Data}");
//    await Task.Delay(1); // Симуляция работы (1 мс)
//}

//// При получении обновления из API / WebSocket
//var update1 = new OrderBookUpdate { Symbol = "AAPL", Data = "bid:100 ask:101" };
//queue.Enqueue(update1, ProcessUpdate);




//tickSize = 10 ^ (floor(log10(P)) - precision + 1)




//var subscription = await ExchangeHelpers.ProcessQueuedAsync(writer => binanceSocketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync("ETHUSDT", writer), async (update) =>
//{
//    // Process the update asynchronously
//});
//await Task.Delay(TimeSpan.FromSeconds(10));
//await subscription.Data.CloseAsync();