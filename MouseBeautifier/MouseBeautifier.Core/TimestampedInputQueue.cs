using System;
using System.Collections.Generic;

namespace MouseBeautifier.Core
{
    public readonly struct TimestampedInput<T>
    {
        public TimestampedInput(long timestamp, T value)
        {
            Timestamp = timestamp;
            Value = value;
        }

        public long Timestamp { get; }

        public T Value { get; }
    }

    /// <summary>
    /// Thread-safe FIFO used at native input boundaries. Capacity is fixed so a
    /// stalled render loop cannot create unbounded latency or memory growth.
    /// </summary>
    public sealed class TimestampedInputQueue<T>
    {
        private readonly object _gate = new();
        private readonly Queue<TimestampedInput<T>> _items = new();
        private long _lastTimestamp = long.MinValue;

        public TimestampedInputQueue(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public void Enqueue(long timestamp, T value)
        {
            lock (_gate)
            {
                // Preserve FIFO timestamp ordering even if two native producers
                // race or a platform timestamp briefly moves backwards.
                timestamp = Math.Max(timestamp, _lastTimestamp);
                _lastTimestamp = timestamp;

                if (_items.Count == Capacity)
                {
                    _items.Dequeue();
                }

                _items.Enqueue(new TimestampedInput<T>(timestamp, value));
            }
        }

        public bool TryDequeue(out TimestampedInput<T> input)
        {
            lock (_gate)
            {
                if (_items.Count > 0)
                {
                    input = _items.Dequeue();
                    return true;
                }
            }

            input = default;
            return false;
        }

        public bool TryDequeueUpTo(
            long inclusiveTimestamp,
            out TimestampedInput<T> input)
        {
            lock (_gate)
            {
                if (_items.Count > 0 &&
                    _items.Peek().Timestamp <= inclusiveTimestamp)
                {
                    input = _items.Dequeue();
                    return true;
                }
            }

            input = default;
            return false;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
                _lastTimestamp = long.MinValue;
            }
        }
    }
}
