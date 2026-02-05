using Bitfinex.Net.Objects.Models;
using CryptoExchange.Net.Objects.Sockets;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Synapse.Crypto.Bfx
{

    // Класс менеджера очередей
    public class BookUpdateTaskQueue
    {

        private readonly ConcurrentDictionary<string, Channel<Func<Task>>> _queues = new();
        //private readonly ILogger<PerSymbolTaskQueue> _logger; // Опционально, для логирования

        //public PerSymbolTaskQueue(ILogger<PerSymbolTaskQueue> logger = null)
        //{
        //    _logger = logger;
        //}

        // Метод для добавления обновления в очередь символа
        public void Enqueue(DataEvent<BitfinexOrderBookEntry[]> update, Func<DataEvent<BitfinexOrderBookEntry[]>, Task> processor)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));

            var symbol = update.Symbol;
            var queue = _queues.GetOrAdd(symbol, _ =>
            {
                var ch = Channel.CreateUnbounded<Func<Task>>(); // Или Bounded для ограничения памяти
                                                                // Запускаем обработчик для новой очереди
                Task.Run(() => ProcessQueueAsync(symbol, ch.Reader));
                return ch;
            });

            queue.Writer.TryWrite(() => processor(update)); // Простой TryWrite для скорости (если очередь полная — можно добавить await WriteAsync)
        }

        // Обработчик для одной очереди (последовательный)
        private async Task ProcessQueueAsync(string symbol, ChannelReader<Func<Task>> reader)
        {
            //_logger?.LogInformation($"Started processing queue for {symbol}");

            await foreach (var task in reader.ReadAllAsync())
            {
                try
                {
                    await task(); // Выполняем обновление order book
                }
                catch (Exception ex)
                {
                   // _logger?.LogError(ex, $"Error processing update for {symbol}");
                    // Здесь можно добавить retry или пропуск
                }
            }

            //_logger?.LogInformation($"Stopped processing queue for {symbol}");
        }

        // Если это BackgroundService — обязательный метод
        protected async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Здесь можно ждать завершения всех очередей, но в простом случае — ничего
            await Task.CompletedTask;
        }

    }
}
