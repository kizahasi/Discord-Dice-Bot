using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace DiscordDice
{
    // 乱数アルゴリズムを変更しやすいように乱数処理をここに集約している
    internal static class Random
    {
        static System.Random random = new System.Random();
        public static int Next(int min, int max)
        {
            return random.Next(min, max);
        }
    }

    // BOT を強制終了する際に使う。
    public class DiscordDiceException : ApplicationException
    {
        public DiscordDiceException(string message)
            : base(message)
        { }

        public DiscordDiceException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }

    // Result という名前は F# を参考にした
    internal sealed class Result<TValue, TError> : IEquatable<Result<TValue, TError>>
    {
        public bool HasValue { get; private set; }
        public TValue Value { get; private set; }
        public TError Error { get; private set; }

        // TValue == TError のときに備えて静的メソッドからインスタンスを作るようにしている
        public static Result<TValue, TError> CreateValue(TValue value)
        {
            return new Result<TValue, TError> { HasValue = true, Value = value, Error = default(TError) };
        }
        public static Result<TValue, TError> CreateError(TError error)
        {
            return new Result<TValue, TError> { HasValue = false, Value = default(TValue), Error = error };
        }

        public bool Equals(Result<TValue, TError> other)
        {
            if(other == null)
            {
                return false;
            }
            if(HasValue != other.HasValue)
            {
                return false;
            }
            if(HasValue)
            {
                return Equals(Value, other.Value);
            }
            return Equals(Error, other.Error);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Result<TValue, TError>);
        }

        private static int GetHashCode<T>(T value)
        {
            if(value == null)
            {
                return 0;
            }
            return value.GetHashCode();
        }

        public override int GetHashCode()
        {
            if(HasValue)
            {
                return HasValue.GetHashCode() ^ GetHashCode(Value);
            }
            else
            {
                return HasValue.GetHashCode() ^ GetHashCode(Error);
            }
        }

        public static bool operator ==(Result<TValue, TError> x, Result<TValue, TError> y)
        {
            if((object)x == null)
            {
                return (object)y == null;
            }
            return x.Equals(y);
        }

        public static bool operator !=(Result<TValue, TError> x, Result<TValue, TError> y)
        {
            return !(x == y);
        }
    }

    // 広範囲で使う拡張メソッドたち
    internal static class Extensions
    {
        public static IReadOnlyList<T> ToReadOnly<T>(this IList<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new ReadOnlyCollection<T>(source);
        }

        public static IReadOnlyDictionary<TKey, TValue> ToReadOnly<TKey, TValue>(this IDictionary<TKey, TValue> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new ReadOnlyDictionary<TKey, TValue>(source);
        }

        public static IEnumerable<T> SkipLast<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            T lastValue = default(T);
            bool hasLastValue = false;
            foreach (var elem in source)
            {
                if (hasLastValue)
                {
                    yield return lastValue;
                }

                lastValue = elem;
                hasLastValue = true;
            }
        }

        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, TValue value)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (source.ContainsKey(key))
            {
                return false;
            }
            source.Add(key, value);
            return true;
        }

        public static bool Remove<TKey, TValue>(this IDictionary<TKey, TValue> source, TKey key, out TValue value)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            if (source.TryGetValue(key, out var removing))
            {
                if (source.Remove(key))
                {
                    value = removing;
                    return true;
                }
            }
            value = default(TValue);
            return false;
        }

        // shuffles 引数は、このプロジェクトでのコードを見やすくするために設けている。
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, bool shuffles)
        {
            if (shuffles)
            {
                var list = source.ToList();
                foreach(var length in Enumerable.Range(0, list.Count).Reverse())
                {
                    var index = Random.Next(0, length + 1);
                    yield return list[index];
                    list.RemoveAt(index);
                }
            }
            else
            {
                foreach (var elem in source)
                {
                    yield return elem;
                }
            }
        }
    }

    internal sealed class RefereneEqualsEqualityComparer<T> : EqualityComparer<T>
    {
        public override bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public override int GetHashCode(T obj)
        {
            // https://stackoverflow.com/questions/1890058/iequalitycomparert-that-uses-referenceequals
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
