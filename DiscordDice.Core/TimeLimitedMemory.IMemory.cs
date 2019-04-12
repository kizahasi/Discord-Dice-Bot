using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiscordDice
{
    internal interface IMemory<TKey, TValue>
    {
        IEnumerable<KeyValuePair<TKey, TValue>> ToEnumerable();
        IQueryable<KeyValuePair<TKey, TValue>> ToQueryable();
        bool TryAdd(TKey key, TValue value);
        bool TryRemove(TKey key, out TValue removed);
        // 結果として1個も削除されなくともremove処理が行われた場合は戻り値はtrue。
        // 問題があったりして削除処理が行われなかった場合はfalse。実装がDBなどの場合は使われる可能性あり。
        bool TryRemoveMany(Func<TKey, TValue, bool> predicate, out IReadOnlyDictionary<TKey, TValue> removed);
        TValue AddOrUpdate(TKey key, TValue value, Func<TKey, TValue, TValue> updateValueFactory);
        bool TryGetValue(TKey key, out TValue value);
    }

    internal class ConcurrentDictionaryMemory<TKey, TValue> : IMemory<TKey, TValue>
    {
        ConcurrentDictionary<TKey, TValue> _core = new ConcurrentDictionary<TKey, TValue>();

        public TValue AddOrUpdate(TKey key, TValue value, Func<TKey, TValue, TValue> updateValueFactory) => _core.AddOrUpdate(key, value, updateValueFactory);

        public IEnumerable<KeyValuePair<TKey, TValue>> ToEnumerable() => _core.ToArray();

        public IQueryable<KeyValuePair<TKey, TValue>> ToQueryable() => ToEnumerable().AsQueryable();

        public bool TryAdd(TKey key, TValue value) => _core.TryAdd(key, value);

        public bool TryGetValue(TKey key, out TValue value) => _core.TryGetValue(key, out value);

        public bool TryRemove(TKey key, out TValue removed) => _core.TryRemove(key, out removed);

        public bool TryRemoveMany(Func<TKey, TValue, bool> predicate, out IReadOnlyDictionary<TKey, TValue> removed)
        {
            var result = new Dictionary<TKey, TValue>();
            foreach (var pair in ToQueryable())
            {
                if (!predicate(pair.Key, pair.Value))
                {
                    continue;
                }
                if (_core.TryRemove(pair.Key, out var value))
                {
                    result[pair.Key] = value;
                }
            }
            removed = result.ToReadOnly();
            return true;
        }
    }

    internal class SqliteMemory<TKey, TValue> : IMemory<TKey, TValue>
    {
        public TValue AddOrUpdate(TKey key, TValue value, Func<TKey, TValue, TValue> updateValueFactory)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<KeyValuePair<TKey, TValue>> ToEnumerable()
        {
            throw new NotImplementedException();
        }

        public IQueryable<KeyValuePair<TKey, TValue>> ToQueryable()
        {
            throw new NotImplementedException();
        }

        public bool TryAdd(TKey key, TValue value)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            throw new NotImplementedException();
        }

        public bool TryRemove(TKey key, out TValue removed)
        {
            throw new NotImplementedException();
        }

        public bool TryRemoveMany(Func<TKey, TValue, bool> predicate, out IReadOnlyDictionary<TKey, TValue> removed)
        {
            throw new NotImplementedException();
        }
    }
}