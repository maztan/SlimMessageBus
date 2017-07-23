using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using Confluent.Kafka;
using SlimMessageBus.Host.Collections;

namespace SlimMessageBus.Host.Kafka
{
    public class KafkaGroupConsumer : IDisposable, IKafkaCommitController
    {
        private static readonly ILog Log = LogManager.GetLogger<KafkaGroupConsumer>();

        public KafkaMessageBus MessageBus { get; }
        public string Group { get; }
        public string[] Topics { get; }

        private readonly SafeDictionaryWrapper<TopicPartition, IKafkaTopicPartitionProcessor> _processors;
        private Consumer _consumer;

        private Task _consumerTask;
        private CancellationTokenSource _consumerCts;

        public KafkaGroupConsumer(KafkaMessageBus messageBus, string group, string[] topics, Func<TopicPartition, IKafkaCommitController, IKafkaTopicPartitionProcessor> processorFactory)
        {
            Log.InfoFormat("Creating for group: {0}, topics: {1}", group, string.Join(", ", topics));

            MessageBus = messageBus;
            Group = group;
            Topics = topics;

            _processors = new SafeDictionaryWrapper<TopicPartition, IKafkaTopicPartitionProcessor>(tp => processorFactory(tp, this));
            
            _consumer = CreateConsumer(group);
            _consumer.OnMessage += OnMessage;
            _consumer.OnPartitionsAssigned += OnPartitionAssigned;
            _consumer.OnPartitionsRevoked += OnPartitionRevoked;
            _consumer.OnPartitionEOF += OnPartitionEndReached;
            _consumer.OnOffsetsCommitted += OnOffsetsCommitted;
            _consumer.OnStatistics += OnStatistics;
        }

        #region Implementation of IDisposable

        public virtual void Dispose()
        {
            if (_consumerTask != null)
            {
                Stop();
            }

            _processors.Clear(x => x.DisposeSilently("processor", Log));          

            // dispose the consumer
            if (_consumer != null)
            {
                _consumer.DisposeSilently("consumer", Log);
                _consumer = null;
            }
        }

        #endregion

        protected Consumer CreateConsumer(string group)
        {
            var config = MessageBus.KafkaSettings.ConsumerConfigFactory(group);
            config[KafkaConfigKeys.Servers] = MessageBus.KafkaSettings.BrokerList;
            config[KafkaConfigKeys.Consumer.GroupId] = group;
            // ToDo: add support for auto commit
            config[KafkaConfigKeys.Consumer.EnableAutoCommit] = false;
            var consumer = MessageBus.KafkaSettings.ConsumerFactory(group, config);
            return consumer;
        }

        public void Start()
        {
            if (_consumerTask != null)
            {
                throw new MessageBusException($"Consumer for group {Group} already started");
            }

            Log.InfoFormat("Group [{0}]: Subscribing to topics: {1}", Group, string.Join(", ", Topics));
            _consumer.Subscribe(Topics);

            _consumerCts = new CancellationTokenSource();
            var ct = _consumerCts.Token;
            var ts = TimeSpan.FromSeconds(2);
            _consumerTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        Log.Trace("Polling consumer");
                        try
                        {
                            _consumer.Poll(ts);
                        }
                        catch (Exception e)
                        {
                            Log.ErrorFormat("Group [{0}]: Error occured while polling new messages (will retry)", e, Group);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.ErrorFormat("Group [{0}]: Error occured in group loop (terminated)", e, Group);
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

  

        public void Stop()
        {
            if (_consumerTask == null)
            {
                throw new MessageBusException($"Consumer for group {Group} not yet started");
            }

            Log.InfoFormat("Group [{0}]: Unassigning partitions", Group);
            _consumer.Unassign();

            Log.InfoFormat("Group [{0}]: Unsubscribing from topics", Group);
            _consumer.Unsubscribe();

            _consumerCts.Cancel();
            try
            {
                _consumerTask.Wait();
            }
            finally
            {
                _consumerTask = null;
                _consumerCts = null;
            }
        }

        protected virtual void OnPartitionAssigned(object sender, List<TopicPartition> partitions)
        {
            if (Log.IsDebugEnabled)
            {
                Log.DebugFormat("Group [{0}]: Assigned partitions: {1}", Group, string.Join(", ", partitions));
            }

            // Ensure processors exist for each assigned topic-partition
            partitions.ForEach(tp => _processors.GetOrAdd(tp));

            _consumer?.Assign(partitions);
        }

        protected virtual void OnPartitionRevoked(object sender, List<TopicPartition> partitions)
        {
            if (Log.IsDebugEnabled)
            {
                Log.DebugFormat("Group [{0}]: Revoked partitions: {1}", Group, string.Join(", ", partitions));
            }

            partitions.ForEach(tp => _processors.GetOrAdd(tp).OnPartitionRevoked().Wait());

            _consumer?.Unassign();
        }

        protected virtual void OnPartitionEndReached(object sender, TopicPartitionOffset offset)
        {
            Log.DebugFormat("Group [{0}]: Reached end of partition: {1}, next message will be at offset: {2}", Group, offset.TopicPartition, offset.Offset);

            var processor = _processors.Dictonary[offset.TopicPartition];
            processor.OnPartitionEndReached(offset).Wait();
        }

        protected virtual void OnMessage(object sender, Message message)
        {
            Log.DebugFormat("Group [{0}]: Received message with offset: {1}, payload size: {2}", Group, message.TopicPartitionOffset, message.Value.Length);

            var processor = _processors.Dictonary[message.TopicPartition];
            processor.OnMessage(message).Wait();
        }

        protected virtual void OnOffsetsCommitted(object sender, CommittedOffsets e)
        {
            if (e.Error)
            {
                if (Log.IsWarnEnabled)
                    Log.WarnFormat("Group [{0}]: Failed to commit offsets: [{1}], error: {2}", Group, string.Join(", ", e.Offsets), e.Error);
            }
            else
            {
                if (Log.IsTraceEnabled)
                    Log.TraceFormat("Group [{0}]: Successfully committed offsets: [{1}]", Group, string.Join(", ", e.Offsets));
            }
        }

        protected virtual void OnStatistics(object sender, string e)
        {
            if (Log.IsTraceEnabled)
            {
                Log.TraceFormat("Group [{0}]: Statistics: {1}", Group, e);
            }
        }

        #region Implementation of IKafkaCoordinator

        public Task Commit(TopicPartitionOffset offset)
        {
            Log.DebugFormat("Group [{0}]: Commit offset: {1}", Group, offset);
            return _consumer.CommitAsync(new List<TopicPartitionOffset> { offset });
        }    

        #endregion
    }
}