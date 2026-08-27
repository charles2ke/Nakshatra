using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Nakshatra.Shared.Messaging;

public class KafkaOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
}

/// <summary>Kafka backed publisher used to stream order and payment events.</summary>
public class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(KafkaOptions options, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    public async Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default)
    {
        try
        {
            await _producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = key,
                Value = JsonSerializer.Serialize(payload)
            }, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish event to topic {Topic}.", topic);
            throw;
        }
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Kafka backed subscriber; consumption runs until the cancellation token trips.</summary>
public class KafkaEventSubscriber : IEventSubscriber
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventSubscriber> _logger;

    public KafkaEventSubscriber(KafkaOptions options, ILogger<KafkaEventSubscriber> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task SubscribeAsync(string topic, string consumerGroup, Func<EventEnvelope, CancellationToken, Task> handler, CancellationToken ct)
        => Task.Run(async () =>
        {
            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = consumerGroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            }).Build();

            consumer.Subscribe(topic);
            _logger.LogInformation("Subscribed to Kafka topic {Topic} as {Group}.", topic, consumerGroup);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                        if (result?.Message is null)
                        {
                            continue;
                        }

                        await handler(new EventEnvelope(topic, result.Message.Key ?? string.Empty, result.Message.Value), ct);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Error consuming from topic {Topic}.", topic);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested.
            }
            finally
            {
                consumer.Close();
            }
        }, ct);
}
