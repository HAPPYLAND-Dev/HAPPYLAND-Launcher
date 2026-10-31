using System;
using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Minimal in-tree multicast <see cref="IObservable{T}"/> for strings (stdout lines). We
/// avoid taking a hard dep on System.Reactive for one subject; this implementation supports
/// concurrent <see cref="Subscribe"/> and <see cref="OnNext"/> with copy-on-iterate semantics
/// so a subscriber that errors out cannot break other subscribers.
/// </summary>
public sealed class HeadlessLineSubject : IObservable<string>
{
    private readonly object _gate = new();
    private List<IObserver<string>> _observers = new();
    private bool _completed;

    /// <summary>Push a stdout line to every current subscriber.</summary>
    public void OnNext(string line)
    {
        List<IObserver<string>> snapshot;
        lock (_gate)
        {
            if (_completed) return;
            snapshot = _observers;
        }
        foreach (var o in snapshot)
        {
            try { o.OnNext(line); }
            catch { /* one bad subscriber shouldn't stop the others */ }
        }
    }

    /// <summary>Signal the end of the stream (e.g. server process exited).</summary>
    public void OnCompleted()
    {
        List<IObserver<string>> snapshot;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            snapshot = _observers;
            _observers = new List<IObserver<string>>();
        }
        foreach (var o in snapshot)
        {
            try { o.OnCompleted(); }
            catch { /* see OnNext */ }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<string> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            if (_completed)
            {
                try { observer.OnCompleted(); } catch { /* ignore */ }
                return new NullSubscription();
            }
            // Copy-on-write so concurrent iterators in OnNext stay safe without a re-lock.
            var next = new List<IObserver<string>>(_observers.Count + 1);
            next.AddRange(_observers);
            next.Add(observer);
            _observers = next;
        }
        return new Subscription(this, observer);
    }

    private void Remove(IObserver<string> observer)
    {
        lock (_gate)
        {
            if (!_observers.Contains(observer)) return;
            var next = new List<IObserver<string>>(_observers.Count);
            foreach (var o in _observers)
                if (!ReferenceEquals(o, observer)) next.Add(o);
            _observers = next;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly HeadlessLineSubject _parent;
        private IObserver<string>? _observer;
        public Subscription(HeadlessLineSubject parent, IObserver<string> observer)
        {
            _parent = parent;
            _observer = observer;
        }
        public void Dispose()
        {
            var o = _observer;
            if (o is null) return;
            _observer = null;
            _parent.Remove(o);
        }
    }

    private sealed class NullSubscription : IDisposable
    {
        public void Dispose() { }
    }
}
