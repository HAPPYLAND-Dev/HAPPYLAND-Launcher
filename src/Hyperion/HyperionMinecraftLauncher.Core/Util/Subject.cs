using System;
using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Util;

/// <summary>
/// Minimal thread-safe multicast <see cref="IObservable{T}"/> implementation - a stripped-down
/// version of the <c>System.Reactive.Subjects.Subject&lt;T&gt;</c> shape. We hand-roll it here
/// instead of pulling the full <c>System.Reactive</c> NuGet because the launcher only needs
/// <c>OnNext</c> / <c>OnCompleted</c> for the game-log piping and avoiding a ~1 MB transitive
/// dependency keeps Core lean.
/// </summary>
/// <typeparam name="T">The type of value produced by the stream.</typeparam>
/// <remarks>
/// <para>
/// Behavior summary, kept deliberately small:
/// </para>
/// <list type="bullet">
///   <item><description><c>OnNext</c> is fan-out: every current subscriber receives the value synchronously on the publisher's thread.</description></item>
///   <item><description><c>OnCompleted</c> flips a latched state; subsequent <c>Subscribe</c> calls fire <c>OnCompleted</c> immediately and return an empty disposable.</description></item>
///   <item><description>The returned <see cref="IDisposable"/> from <see cref="Subscribe"/> removes the observer when disposed - cheap, no allocations beyond the disposal token.</description></item>
///   <item><description>Subscriber exceptions are caught and swallowed so one bad observer can't poison the rest of the fan-out. (The full ReactiveX Subject re-throws; we'd rather keep the game-log pipe alive even if the UI subscriber blows up.)</description></item>
/// </list>
/// </remarks>
public sealed class Subject<T> : IObservable<T>
{
    private readonly object _gate = new();
    private List<IObserver<T>> _observers = new();
    private bool _completed;

    /// <summary>Subscribe an observer. The returned token unsubscribes when disposed.</summary>
    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
        {
            if (_completed)
            {
                // Already-completed subject: deliver OnCompleted immediately and hand back a no-op.
                observer.OnCompleted();
                return NoopDisposable.Instance;
            }

            // Copy-on-write so iterating subscribers during OnNext doesn't blow up if a handler
            // unsubscribes mid-iteration. The list is small (typically 0-1 entries) so this is
            // cheap compared to taking a snapshot every emission.
            var next = new List<IObserver<T>>(_observers.Count + 1);
            next.AddRange(_observers);
            next.Add(observer);
            _observers = next;
        }

        return new Subscription(this, observer);
    }

    /// <summary>Broadcast <paramref name="value"/> to every current observer.</summary>
    public void OnNext(T value)
    {
        List<IObserver<T>> snapshot;
        lock (_gate)
        {
            if (_completed) return;
            snapshot = _observers;
        }

        // Iterate the snapshot outside the lock so a re-entrant Subscribe/Unsubscribe inside
        // a handler doesn't deadlock and a slow observer doesn't stall publishers.
        foreach (var o in snapshot)
        {
            try { o.OnNext(value); }
            catch { /* swallow - see <remarks> on the class. */ }
        }
    }

    /// <summary>Latch the stream as completed. All current and future subscribers see <see cref="IObserver{T}.OnCompleted"/>.</summary>
    public void OnCompleted()
    {
        List<IObserver<T>> snapshot;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            snapshot = _observers;
            _observers = new List<IObserver<T>>();
        }

        foreach (var o in snapshot)
        {
            try { o.OnCompleted(); }
            catch { /* swallow */ }
        }
    }

    private void Unsubscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            if (_observers.Count == 0) return;
            var next = new List<IObserver<T>>(_observers.Count);
            foreach (var o in _observers)
            {
                if (!ReferenceEquals(o, observer)) next.Add(o);
            }
            _observers = next;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Subject<T>? _owner;
        private IObserver<T>? _observer;

        public Subscription(Subject<T> owner, IObserver<T> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            var owner = System.Threading.Interlocked.Exchange(ref _owner, null);
            var observer = System.Threading.Interlocked.Exchange(ref _observer, null);
            if (owner is not null && observer is not null)
                owner.Unsubscribe(observer);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Tiny delegate-based <see cref="IObserver{T}"/> so callers can <c>subject.Subscribe(line => ...)</c>
/// without hand-rolling a class each time. Equivalent to <c>System.Reactive.Linq.ObservableExtensions.Subscribe</c>.
/// </summary>
public static class ObservableExtensions
{
    /// <summary>Subscribe with a single <paramref name="onNext"/> callback; ignores <c>OnError</c> / <c>OnCompleted</c>.</summary>
    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onNext);
        return source.Subscribe(new DelegateObserver<T>(onNext, null, null));
    }

    /// <summary>Subscribe with explicit <paramref name="onNext"/> and <paramref name="onCompleted"/> callbacks.</summary>
    public static IDisposable Subscribe<T>(this IObservable<T> source, Action<T> onNext, Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onNext);
        ArgumentNullException.ThrowIfNull(onCompleted);
        return source.Subscribe(new DelegateObserver<T>(onNext, null, onCompleted));
    }

    private sealed class DelegateObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception>? _onError;
        private readonly Action? _onCompleted;

        public DelegateObserver(Action<T> onNext, Action<Exception>? onError, Action? onCompleted)
        {
            _onNext = onNext;
            _onError = onError;
            _onCompleted = onCompleted;
        }

        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError?.Invoke(error);
        public void OnCompleted() => _onCompleted?.Invoke();
    }
}
