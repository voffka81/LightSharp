namespace LightSharp.Services;

/// <summary>Typed in-process message bus for communication between views and controllers.</summary>
public interface IMessageBus
{
    IObservable<TMessage> Observe<TMessage>();
    void Publish<TMessage>(TMessage message);
}

public sealed class MessageBus : IMessageBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<IObserver<object>>> _observers = [];

    public IObservable<TMessage> Observe<TMessage>() => new MessageStream<TMessage>(this);

    internal IDisposable Subscribe<TMessage>(IObserver<TMessage> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var adapter = new ObserverAdapter<TMessage>(observer);
        lock (_gate)
        {
            if (!_observers.TryGetValue(typeof(TMessage), out var observers))
                _observers[typeof(TMessage)] = observers = [];
            observers.Add(adapter);
        }
        return new Unsubscriber(() =>
        {
            lock (_gate)
            {
                if (_observers.TryGetValue(typeof(TMessage), out var observers))
                {
                    observers.Remove(adapter);
                    if (observers.Count == 0) _observers.Remove(typeof(TMessage));
                }
            }
        });
    }

    public void Publish<TMessage>(TMessage message)
    {
        IObserver<object>[] observers;
        lock (_gate)
            observers = _observers.TryGetValue(typeof(TMessage), out var registered)
                ? registered.ToArray()
                : [];

        foreach (var observer in observers) observer.OnNext(message!);
    }

    private sealed class MessageStream<TMessage>(MessageBus owner) : IObservable<TMessage>
    {
        public IDisposable Subscribe(IObserver<TMessage> observer) => owner.Subscribe(observer);
    }

    private sealed class ObserverAdapter<TMessage>(IObserver<TMessage> observer) : IObserver<object>
    {
        public void OnNext(object value) => observer.OnNext((TMessage)value);
        public void OnError(Exception error) => observer.OnError(error);
        public void OnCompleted() => observer.OnCompleted();
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}

public static class ObservableSubscriptionExtensions
{
    public static IDisposable Subscribe<TMessage>(this IObservable<TMessage> observable, Action<TMessage> onNext)
    {
        ArgumentNullException.ThrowIfNull(observable);
        ArgumentNullException.ThrowIfNull(onNext);
        return observable.Subscribe(new DelegateObserver<TMessage>(onNext));
    }

    private sealed class DelegateObserver<TMessage>(Action<TMessage> onNext) : IObserver<TMessage>
    {
        public void OnNext(TMessage value) => onNext(value);
        public void OnError(Exception error) => throw error;
        public void OnCompleted() { }
    }
}
