using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordDice
{
    public static class ObservableExtensions
    {
        public static IDisposable SubscribeAsync<T>(this IObservable<T> source, Func<T, Task> action)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (action == null) throw new ArgumentNullException(nameof(action));

            return
                source
                .SelectMany(async v =>
                    {
                        await action(v);
                        return Unit.Default;
                    })
                .Subscribe();
        }
    }
}
