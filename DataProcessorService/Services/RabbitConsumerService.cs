using DataProcessorService.Data;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Shared.Models;

namespace DataProcessorService.Services
{
    public class RabbitConsumerService : BackgroundService
    {
        private readonly ILogger<RabbitConsumerService> _log;
        private readonly IConfiguration _cfg;
        private readonly SqliteRepository _repo;
        private IConnection _conn;
        private IModel _channel;
        private readonly string _exchange;
        private readonly string _queue;
        private readonly string _routingKey;

        public RabbitConsumerService(ILogger<RabbitConsumerService> log, IConfiguration cfg, SqliteRepository repo)
        {
            _log = log;
            _cfg = cfg;
            _repo = repo;
            _exchange = _cfg.GetValue<string>("RabbitMQ:Exchange", "instrument_exchange");
            _queue = _cfg.GetValue<string>("RabbitMQ:Queue", "instrument_queue");
            _routingKey = _cfg.GetValue<string>("RabbitMQ:RoutingKey", "instrument.status");
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var host = _cfg.GetValue<string>("RabbitMQ:Host", "localhost");
            var port = _cfg.GetValue<int>("RabbitMQ:Port", 5672);
            var user = _cfg.GetValue<string>("RabbitMQ:Username", "guest");
            var pass = _cfg.GetValue<string>("RabbitMQ:Password", "guest");

            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = user,
                Password = pass,
                DispatchConsumersAsync = true
            };

            _conn = factory.CreateConnection();
            _channel = _conn.CreateModel();
            _channel.ExchangeDeclare(_exchange, ExchangeType.Direct, durable: true, autoDelete: false);
            _channel.QueueDeclare(_queue, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(_queue, _exchange, _routingKey);

            _log.LogInformation("Connected to RabbitMQ and bound queue {queue} to exchange {exchange} with routing key {rk}", _queue, _exchange, _routingKey);

            await base.StartAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += async (sender, ea) =>
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                try
                {
                    var msg = JsonSerializer.Deserialize<InstrumentMessage>(json);
                    if (msg != null)
                    {
                        _log.LogInformation("Received message PackageID={pkg} with {count} modules", msg.PackageID, msg.Modules.Count);
                        foreach (var m in msg.Modules)
                        {
                            await _repo.UpsertModuleAsync(m.ModuleCategoryID, m.ModuleState);
                        }
                    }
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to process message: {json}", json);
                    // optionally move to dead-letter queue - for now Nack and requeue with delay could be implemented
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            };

            _channel.BasicQos(0, 10, false);
            _channel.BasicConsume(queue: _queue, autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try { _channel?.Close(); } catch { }
            try { _conn?.Close(); } catch { }
            return base.StopAsync(cancellationToken);
        }

    }
}
