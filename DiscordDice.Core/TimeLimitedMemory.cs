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
    internal class TimeLimitedMemory<TKey, TValue>
    {
        readonly IDisposable _subscriptions;
        readonly IMemory<TKey, (TValue, DateTimeOffset)> _implementedMemory;
        readonly Subject<TimeLimitedMemoryChangedValue> _updated = new Subject<TimeLimitedMemoryChangedValue>();
        readonly ITime _time;

        public TimeLimitedMemory(TimeSpan timeLimit, TimeSpan windowOfCheckingTimeLimit, ITime time, IMemory<TKey, (TValue, DateTimeOffset)> implementedMemory)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _implementedMemory = implementedMemory ?? throw new ArgumentNullException(nameof(implementedMemory));
            Updated = _updated.AsObservable();

            _subscriptions =
                 Observable.Interval(windowOfCheckingTimeLimit)
                 .Subscribe(_ =>
                 {
                     var now = time.GetUtcNow();
                     Func<TKey, (TValue, DateTimeOffset), bool> predicate = (key, value) =>
                     {
                         var elapsed = now - value.Item2;
                         return elapsed >= timeLimit;
                     };
                     if (_implementedMemory.TryRemoveMany(predicate, out var removed))
                     {
                         foreach (var pair in removed)
                         {
                             _updated.OnNext(TimeLimitedMemoryChangedValue.CreateTimeLimit(pair.Key, pair.Value.Item1, pair.Value.Item2, _time.GetUtcNow()));
                         }
                     }
                 });
        }

        public IObservable<TimeLimitedMemoryChangedValue> Updated { get; }

        public bool TryAdd(TKey key, TValue value)
        {
            var tlValue = (value, _time.GetUtcNow());
            return _implementedMemory.TryAdd(key, tlValue);
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            if (_implementedMemory.TryRemove(key, out var removedValue))
            {
                _updated.OnNext(TimeLimitedMemoryChangedValue.CreateByRemoveMethod(key, removedValue.Item1, removedValue.Item2, _time.GetUtcNow()));
                value = removedValue.Item1;
                return true;
            }
            value = default;
            return false;
        }

        // Update されたら自動削除時間も更新される
        public (TValue value, DateTimeOffset createdAt) AddOrUpdate(TKey key, TValue value)
        {
            return _implementedMemory.AddOrUpdate(key, (value, _time.GetUtcNow()), (_, oldValue) =>
            {
                // Replaced と言いながら実際には Replacing のタイミングなのはよくない…
                _updated.OnNext(TimeLimitedMemoryChangedValue.CreateReplaced(key, oldValue.Item1, value, oldValue.Item2, _time.GetUtcNow()));
                return (value, _time.GetUtcNow());
            });
        }

        public bool TryGetValue(TKey key, out (TValue value, DateTimeOffset createdAt) value) => _implementedMemory.TryGetValue(key, out value);

        public IEnumerable<KeyValuePair<TKey, (TValue, DateTimeOffset)>> ToEnumerable() => _implementedMemory.ToEnumerable();

        public IQueryable<KeyValuePair<TKey, (TValue, DateTimeOffset)>> ToQueryable() => _implementedMemory.ToQueryable();

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
