using SharpMQ.Abstractions;

namespace SharpMQ.Connections
{
    internal class RawConnectionAccessor : IRawConnectionAccessor
    {
        private readonly IConnectionManager _connectionManager;

        public RawConnectionAccessor(IConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public object GetConnection()
        {
            return _connectionManager.GetOrCreateConnection();
        }
    }
}