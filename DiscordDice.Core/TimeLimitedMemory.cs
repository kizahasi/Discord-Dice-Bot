using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

namespace DiscordDice
{
    internal enum TimeLimitedMemoryChangedType
    {
        ByRemoveMethod,
        Replaced,
        TimeLimit,
    }

    /// <summary>値が追加されてから一定時間経ったら自動的に削除される辞書。</summary>
    // 値の更新があっても自動的に削除されるまでの時間は変わらない。
    internal class TimeLimitedMemory<TKey, TValue> : IEnumerable<KeyValuePair<TKey, (TValue, DateTimeOffset)>>
    {
        readonly IDisposable _subscriptions;
        // Observable.Interval から操作するので、一応スレッドセーフな ConcurrentDictionary を使っている
        readonly ConcurrentDictionary<TKey, (TValue, DateTimeOffset)> _core = new ConcurrentDictionary<TKey, (TValue, DateTimeOffset)>();
        readonly Subject<TimeLimitedMemoryChangedValue> _updated = new Subject<TimeLimitedMemoryChangedValue>();
        readonly ITime _time;

        public TimeLimitedMemory(TimeSpan timeLimit, TimeSpan windowOfCheckingTimeLimit, ITime time)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            Updated = _updated.AsObservable();

            _subscriptions =
                 Observable.Interval(windowOfCheckingTimeLimit)
                 .Subscribe(_ =>
                 {
                     var now = time.GetUtcNow();
                     foreach (var pair in _core.ToArray())
                     {
                         var elapsed = now - pair.Value.Item2;
                         if (elapsed >= timeLimit)
                         {
                             if (_core.TryRemove(pair.Key, out var removedValue))
                             {
                                 _updated.OnNext(TimeLimitedMemoryChangedValue.CreateTimeLimit(pair.Key, removedValue.Item1, removedValue.Item2, _time.GetUtcNow()));
                             }
                         }
                     }
                 });
        }

        public IObservable<TimeLimitedMemoryChangedValue> Updated { get; }

        public bool TryAdd(TKey key, TValue value)
        {
            var tlValue = (value, _time.GetUtcNow());
            if (_core.TryAdd(key, tlValue))
            {
                return true;
            }
            return false;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            if (_core.TryRemove(key, out var removedValue))
            {
                _updated.OnNext(TimeLimitedMemoryChangedValue.CreateByRemoveMethod(key, removedValue.Item1, removedValue.Item2, _time.GetUtcNow()));
                value = removedValue.Item1;
                return true;
            }
            value = default(TValue);
            return false;
        }

        // Update されたら自動削除時間も更新される
        public (TValue value, DateTimeOffset createdAt) AddOrUpdate(TKey key, TValue value)
        {
            return _core.AddOrUpdate(key, (value, _time.GetUtcNow()), (_, oldValue) =>
            {
                // Replaced と言いながら実際には Replacing のタイミングなのはよくない…
                _updated.OnNext(TimeLimitedMemoryChangedValue.CreateReplaced(key, oldValue.Item1, value, oldValue.Item2, _time.GetUtcNow()));
                return (value, _time.GetUtcNow());
            });
        }

        public bool TryGetValue(TKey key, out (TValue value, DateTimeOffset createdAt) value) => _core.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, (TValue, DateTimeOffset)>> GetEnumerator()
        {
            return _core.ToArray().AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            IEnumerable core = _core.ToArray();
            return core.GetEnumerator();
        }

        public class TimeLimitedMemoryChangedValue
        {
            private TimeLimitedMemoryChangedValue(TimeLimitedMemoryChangedType type, TKey key, TValue oldValue, TValue newValue, DateTimeOffset createdAt, DateTimeOffset removedAt)
            {
                Type = type;
                Key = key;
                OldValue = oldValue;
                NewValue = newValue;
                CreatedAt = createdAt;
                RemovedAt = removedAt;
            }

            public static TimeLimitedMemoryChangedValue CreateByRemoveMethod(TKey key, TValue oldValue, DateTimeOffset createdAt, DateTimeOffset removedAt)
            {
                return new TimeLimitedMemoryChangedValue(TimeLimitedMemoryChangedType.ByRemoveMethod, key, oldValue, default, createdAt, removedAt);
            }

            public static TimeLimitedMemoryChangedValue CreateReplaced(TKey key, TValue oldValue, TValue newValue, DateTimeOffset createdAt, DateTimeOffset removedAt)
            {
                return new TimeLimitedMemoryChangedValue(TimeLimitedMemoryChangedType.Replaced, key, oldValue, newValue, createdAt, removedAt);
            }

            public static TimeLimitedMemoryChangedValue CreateTimeLimit(TKey key, TValue oldValue, DateTimeOffset createdAt, DateTimeOffset removedAt)
            {
                return new TimeLimitedMemoryChangedValue(TimeLimitedMemoryChangedType.TimeLimit, key, oldValue, default, createdAt, removedAt);
            }

            public TimeLimitedMemoryChangedType Type { get; }
            public TKey Key { get; }
            public TValue OldValue { get; }
            public TValue NewValue { get; }
            public DateTimeOffset CreatedAt { get; }
            public DateTimeOffset RemovedAt { get; }
        }
    }
}
