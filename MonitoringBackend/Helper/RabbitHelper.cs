using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MonitoringBackend.Helper
{
    public class RabbitHelper : IDisposable
    {
        private IConnection? _conn;
        private IChannel? _ch;
        //private readonly LoggerService _log;

        public const string EX_BROADCAST = "branch.broadcast.patch";
        public const string EX_DIRECT = "branch.direct.patch";
        public const string EX_STATUS = "branch.status.patch";
        public const string EX_STATUS_ALL_PATCH = "all.status.patch";


        //public RabbitHelper(LoggerService log)
        //{
        //    _log = log;
        //}

        public async Task RabbitHelperInit(string host, string user, string pass)
        {

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = host,
                    UserName = user,
                    Password = pass,
                };

                _conn = await factory.CreateConnectionAsync();
                _ch = await _conn.CreateChannelAsync();

                // Declare exchanges
                await _ch.ExchangeDeclareAsync(EX_BROADCAST, ExchangeType.Fanout, durable: true);
                await _ch.ExchangeDeclareAsync(EX_DIRECT, ExchangeType.Direct, durable: true);
                await _ch.ExchangeDeclareAsync(EX_STATUS, ExchangeType.Fanout, durable: true);
                await _ch.ExchangeDeclareAsync(EX_STATUS_ALL_PATCH, ExchangeType.Fanout, durable: true);

            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to connect to RabbitMQ");
                //await _log.WriteLog("RabiMQ Exception (RabbitHelperInit)", $"Unexpected error: {ex}", 3);
            }
        }

        public async Task PublishBroadcast(object payload)
        {
            try
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _ch.BasicPublishAsync(EX_BROADCAST, "", body);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to PublishBroadcast event ");
                //await _log.WriteLog("RabiMQ Exception (PublishBroadcast)", $"Unexpected error: {ex}", 3);
            }

        }

        public async Task PublishToBranch(string branchId, object payload)
        {
            try
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _ch.BasicPublishAsync(EX_DIRECT, branchId, body);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to PublishToBranch event ");
                //await _log.WriteLog("RabiMQ Exception (PublishToBranch)", $"Unexpected error: {ex}", 3);
            }
        }

        public async Task PublishStatusOnBranch(object payload)
        {
            try
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _ch.BasicPublishAsync(EX_STATUS, "", body);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to PublishStatus event ");
                //await _log.WriteLog("RabiMQ Exception (PublishStatus)", $"Unexpected error: {ex}", 3);
            }
        }

        public async Task PublishAllBranchStatus(object payload)
        {
            try
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _ch.BasicPublishAsync(EX_STATUS_ALL_PATCH, "", body);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to PublishStatus event ");
                //await _log.WriteLog("RabiMQ Exception (PublishStatus)", $"Unexpected error: {ex}", 3);
            }
        }

        public async Task SubscribeBranchQueue(string branchId, Func<string, Task> handler)
        {
            try
            {
                string queueName = $"branch.queue.{branchId}.patch";

                await _ch.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);

                // Bind same queue to fanout and direct
                await _ch.QueueBindAsync(queueName, EX_BROADCAST, "");
                await _ch.QueueBindAsync(queueName, EX_DIRECT, branchId);

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.ReceivedAsync += async (ch, args) =>
                {
                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
                    await handler(msg);
                    await _ch.BasicAckAsync(args.DeliveryTag, false);
                };

                await _ch.BasicConsumeAsync(queueName, false, consumer);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to SubscribeBranchQueue event ");
                //await _log.WriteLog("RabiMQ Exception (SubscribeBranchQueue)", $"Unexpected error: {ex}", 3);
            }

        }

        public async Task SubscribeToStatusForAllPatch(Func<string, Task> handler)
        {
            try
            {
                string queue = "main.status.queue.all.patch";


                await _ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
                await _ch.QueueBindAsync(queue, EX_STATUS_ALL_PATCH, "#"); // all routing keys (all branches)

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.ReceivedAsync += async (ch, args) =>
                {
                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
                    await handler(msg);
                    await _ch.BasicAckAsync(args.DeliveryTag, false);
                };

                await _ch.BasicConsumeAsync(queue, false, consumer);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to SubscribeToStatusForAllPatch event ");
                //await _log.WriteLog("RabiMQ Exception (SubscribeToStatusForAllPatch)", $"Unexpected error: {ex}", 3);
            }
        }



        public async Task SubscribeToStatusOneBranchPatch(Func<string, Task> handler)
        {
            try
            {
                string queue = "branch.status.queue.one.patch";


                await _ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
                await _ch.QueueBindAsync(queue, EX_STATUS, "");

                var consumer = new AsyncEventingBasicConsumer(_ch);
                consumer.ReceivedAsync += async (ch, args) =>
                {
                    var msg = Encoding.UTF8.GetString(args.Body.ToArray());
                    await handler(msg);
                    await _ch.BasicAckAsync(args.DeliveryTag, false);
                };

                await _ch.BasicConsumeAsync(queue, false, consumer);
            }
            catch (Exception ex)
            {
                //await _log.WriteLog("RabiMQ Error", $"Failed to SubscribeToStatusOneBranchPatch event ");
                //await _log.WriteLog("RabiMQ Exception (SubscribeToStatusOneBranchPatch)", $"Unexpected error: {ex}", 3);
            }
        }

        public void Dispose()
        {
            _ch?.CloseAsync().GetAwaiter().GetResult();
            _conn?.CloseAsync().GetAwaiter().GetResult();
        }
    }
}
