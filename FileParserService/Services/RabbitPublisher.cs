using Microsoft.AspNetCore.Connections;
using RabbitMQ.Client;
using System.Text;

namespace FileParserService.Services
{
    public class RabbitPublisher : IDisposable
    {
        private readonly IConnection _conn;
        private readonly IModel _channel;
        private readonly ILogger<RabbitPublisher> _log;
        private readonly string _exchange;
        private readonly string _routingKey;
        private readonly bool _ownsConnection;

        public RabbitPublisher(IConfiguration cfg, ILogger<RabbitPublisher> log)
        {
            _log = log;
            var host = cfg.GetValue<string>("RabbitMQ:Host", "localhost");
            var port = cfg.GetValue<int>("RabbitMQ:Port", 5672);
            var user = cfg.GetValue<string>("RabbitMQ:Username", "guest");
            var pass = cfg.GetValue<string>("RabbitMQ:Password", "guest");
            _exchange = cfg.GetValue<string>("RabbitMQ:Exchange", "instrument_exchange");
            _routingKey = cfg.GetValue<string>("RabbitMQ:RoutingKey", "instrument.status");

            try
            {
                var factory = new ConnectionFactory()
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
                // optionally declare queue if needed; consumer will bind
                _log.LogInformation("Connected to RabbitMQ @ {host}:{port}", host, port);
                _ownsConnection = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Couldn't connect to RabbitMQ");
                throw;
            }
        }

        public void Publish(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var props = _channel.CreateBasicProperties();
            props.Persistent = true;

            _channel.BasicPublish(exchange: _exchange, routingKey: _routingKey, basicProperties: props, body: body);
            _log.LogInformation("Published message to exchange {exchange} routingKey {rk}", _exchange, _routingKey);
        }

        public void Dispose()
        {
            try { _channel?.Close(); } catch { }
            try { _conn?.Close(); } catch { }
        }
    }
}
