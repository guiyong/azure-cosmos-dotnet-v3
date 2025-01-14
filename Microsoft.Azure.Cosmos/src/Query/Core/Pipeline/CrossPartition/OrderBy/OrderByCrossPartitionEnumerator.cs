﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Pagination
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Query.Core.Collections;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Query.Core.Pipeline.CrossPartition.OrderBy;
    using Microsoft.Azure.Cosmos.Tracing;

    internal sealed class OrderByCrossPartitionEnumerator : IEnumerator<OrderByQueryResult>
    {
        private readonly PriorityQueue<IEnumerator<OrderByQueryResult>> queue;

        private bool started;

        public OrderByQueryResult Current => this.queue.Peek().Current;

        object IEnumerator.Current => this.Current;

        public OrderByCrossPartitionEnumerator(PriorityQueue<IEnumerator<OrderByQueryResult>> queue)
        {
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public static async Task<(IEnumerator<OrderByQueryResult> orderbyQueryResultEnumerator, double totalRequestCharge)> CreateAsync(
            IEnumerable<OrderByQueryPartitionRangePageAsyncEnumerator> enumerators,
            IComparer<OrderByQueryResult> comparer,
            int levelSize,
            ITrace trace,
            CancellationToken cancellationToken)
        {
            if (enumerators == null)
            {
                throw new ArgumentNullException(nameof(enumerators));
            }

            if (comparer == null)
            {
                throw new ArgumentNullException(nameof(comparer));
            }

            double totalRequestCharge = 0;
            EnumeratorComparer enumeratorComparer = new EnumeratorComparer(comparer);
            PriorityQueue<IEnumerator<OrderByQueryResult>> queue = new PriorityQueue<IEnumerator<OrderByQueryResult>>(enumeratorComparer);
            foreach (ITracingAsyncEnumerator<TryCatch<OrderByQueryPage>> enumerator in enumerators)
            {
                while (await enumerator.MoveNextAsync(trace, cancellationToken))
                {
                    TryCatch<OrderByQueryPage> currentPage = enumerator.Current;
                    if (currentPage.Failed)
                    {
                        throw currentPage.Exception;
                    }

                    totalRequestCharge += currentPage.Result.RequestCharge;
                    IReadOnlyList<CosmosElement> page = currentPage.Result.Page.Documents;

                    if (page.Count > 0)
                    {
                        PageEnumerator pageEnumerator = new PageEnumerator(page);
                        pageEnumerator.MoveNext();

                        queue.Enqueue(pageEnumerator);

                        if (queue.Count >= levelSize)
                        {
                            OrderByCrossPartitionEnumerator newEnumerator = new OrderByCrossPartitionEnumerator(queue);
                            newEnumerator.MoveNext();

                            queue = new PriorityQueue<IEnumerator<OrderByQueryResult>>(enumeratorComparer);
                            queue.Enqueue(newEnumerator);
                        }
                    }
                }
            }

            if (queue.Count == 0)
            {
                return (EmptyEnumerator.Instance, totalRequestCharge);
            }

            return (new OrderByCrossPartitionEnumerator(queue), totalRequestCharge);
        }

        public bool MoveNext()
        {
            if (this.queue.Count == 0)
            {
                return false;
            }

            if (!this.started)
            {
                // We never start empty
                this.started = true;
                return true;
            }

            IEnumerator<OrderByQueryResult> enumerator = this.queue.Dequeue();
            if (enumerator.MoveNext())
            {
                this.queue.Enqueue(enumerator);
            }

            return this.queue.Count > 0;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            while (this.queue.Count > 0)
            {
                IEnumerator<OrderByQueryResult> enumerator = this.queue.Dequeue();
                enumerator.Dispose();
            }
        }

        private sealed class EmptyEnumerator : IEnumerator<OrderByQueryResult>
        {
            public static readonly EmptyEnumerator Instance = new EmptyEnumerator();

            public OrderByQueryResult Current => throw new InvalidOperationException();

            object IEnumerator.Current => this.Current;

            private EmptyEnumerator()
            {
            }

            public bool MoveNext()
            {
                return false;
            }

            public void Reset()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class EnumeratorComparer : IComparer<IEnumerator<OrderByQueryResult>>
        {
            private readonly IComparer<OrderByQueryResult> comparer;

            public EnumeratorComparer(IComparer<OrderByQueryResult> comparer)
            {
                this.comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
            }

            public int Compare(IEnumerator<OrderByQueryResult> x, IEnumerator<OrderByQueryResult> y)
            {
                return this.comparer.Compare(x.Current, y.Current);
            }
        }

        private sealed class PageEnumerator : IEnumerator<OrderByQueryResult>
        {
            private readonly IEnumerator<CosmosElement> enumerator;

            public OrderByQueryResult Current { get; private set; }

            object IEnumerator.Current => this.Current;

            public PageEnumerator(IReadOnlyList<CosmosElement> page)
            {
                this.enumerator = page?.GetEnumerator() ?? throw new ArgumentNullException(nameof(page));
            }

            public bool MoveNext()
            {
                if (this.enumerator.MoveNext())
                {
                    this.Current = new OrderByQueryResult(this.enumerator.Current);
                    return true;
                }

                return false;
            }

            public void Reset()
            {
                this.enumerator.Reset();
            }

            public void Dispose()
            {
                this.enumerator.Dispose();
            }
        }
    }
}