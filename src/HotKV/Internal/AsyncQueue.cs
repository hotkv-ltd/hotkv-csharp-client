// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV.Internal
{
    /// <summary>
    /// A minimal unbounded async producer/consumer queue backing the
    /// <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/> side of
    /// pub/sub message delivery. Hand rolled (rather than
    /// System.Threading.Channels) to keep the package free of runtime
    /// dependencies on netstandard2.1.
    /// </summary>
    internal sealed class AsyncQueue<T>
    {
        private readonly Queue<T> _items = new Queue<T>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly object _gate = new object();
        private bool _completed;
        private Exception? _fault;

        public void Enqueue(T item)
        {
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }
                _items.Enqueue(item);
            }
            _signal.Release();
        }

        public void Complete(Exception? fault = null)
        {
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }
                _completed = true;
                _fault = fault;
            }
            _signal.Release();
        }

        public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

                bool hasItem;
                T item;
                bool done;
                Exception? fault;
                lock (_gate)
                {
                    hasItem = _items.Count > 0;
                    item = hasItem ? _items.Dequeue() : default!;
                    done = !hasItem && _completed;
                    fault = _fault;
                }

                if (hasItem)
                {
                    yield return item;
                    continue;
                }

                if (done)
                {
                    if (fault is not null)
                    {
                        throw fault;
                    }
                    yield break;
                }
            }
        }
    }
}
