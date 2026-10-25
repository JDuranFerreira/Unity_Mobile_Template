namespace Template.Core.Events
{
    /// <summary>Published once, before the first service initializes.</summary>
    public readonly struct AppBootStarted : IEvent
    {
        public AppBootStarted(int serviceCount)
        {
            ServiceCount = serviceCount;
        }

        public int ServiceCount { get; }
    }

    /// <summary>One per service, in boot order, after that service has finished initializing.</summary>
    public readonly struct ServiceInitialized : IEvent
    {
        public ServiceInitialized(string serviceName, int order)
        {
            ServiceName = serviceName;
            Order = order;
        }

        public string ServiceName { get; }

        public int Order { get; }
    }

    /// <summary>Every service initialized. The first real scene is loaded after this.</summary>
    public readonly struct AppBootCompleted : IEvent
    {
        public AppBootCompleted(int serviceCount)
        {
            ServiceCount = serviceCount;
        }

        public int ServiceCount { get; }
    }

    /// <summary>
    /// A service threw while initializing. The chain stops here: a later service that depends on
    /// the broken one must not be allowed to start on half-built state.
    /// </summary>
    public readonly struct AppBootFailed : IEvent
    {
        public AppBootFailed(string serviceName, string reason)
        {
            ServiceName = serviceName;
            Reason = reason;
        }

        public string ServiceName { get; }

        public string Reason { get; }
    }
}
