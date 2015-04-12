﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Transactions;
using LightningQueues.Model;
using Xunit;
using Should;

namespace LightningQueues.Tests
{
    public class CanStreamMessages : IDisposable
    {
        private QueueManager _sender;
        private QueueManager _receiver;

        public CanStreamMessages()
        {
            _sender = ObjectMother.QueueManager();
            _sender.Start();

            _receiver = ObjectMother.QueueManager("test2", 23457);
            _receiver.CreateQueues("a");
            _receiver.Start();
        }

        public void Dispose()
        {
            _sender.Dispose();
            _receiver.Dispose();
        }

        [Fact(Skip="Not on mono")]
        public void CanReceiveSingleMessageInAStream()
        {
            var handle = new ManualResetEvent(false);
            byte[] data = null;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var messages = _receiver.ReceiveStream("h", null);
                messages.Each(x =>
                {
                    data = x.Message.Data;
                    x.TransactionalScope.Commit();
                    handle.Set();
                });
            });
            using (var tx = new TransactionScope())
            {
                _sender.Send(new Uri("rhino.queues://localhost:23457/h"),
                    new MessagePayload
                    {
                        Data = new byte[] {1, 2, 4, 5}
                    });
                tx.Complete();
            }

            handle.WaitOne(TimeSpan.FromSeconds(3));
            new byte[] {1, 2, 4, 5}.ShouldEqual(data);
        }

        [Fact(Skip="Not on mono")]
        public void CanReceiveSeveralMessagesInAStreamConcurrently()
        {
            var received = new ConcurrentBag<Message>();

            for (int i = 0; i < 4; ++i)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    var messages = _receiver.ReceiveStream("h", null);
                    foreach(var x in messages)
                    {
                        received.Add(x.Message);
                        x.TransactionalScope.Commit();
                    }
                });
            }
            for (int i = 0; i < 20; ++i)
            {
                var scope = _sender.BeginTransactionalScope();
                scope.Send(new Uri("rhino.queues://localhost:23457/h"),
                    new MessagePayload
                    {
                        Data = new byte[] {(byte) i, 2, 4, 5}
                    });
                scope.Commit();
            }

            Wait.Until(() => received.Count == 20, timeoutInMilliseconds: 10000).ShouldBeTrue();
        }
    }
}