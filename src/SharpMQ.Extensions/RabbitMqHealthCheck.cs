using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharpMQ.Connections;

namespace SharpMQ.Extensions
{
    /// <summary>
    /// Health check for RabbitMQ connection status
    /// </summary>
    public class RabbitMqHealthCheck : IHealthCheck
    {
        private readonly IConnectionProvider _connectionProvider;

        /// <summary>
        /// Creates a new instance of RabbitMqHealthCheck
        /// </summary>
        /// <param name="connectionProvider">The connection provider to check health</param>
        public RabbitMqHealthCheck(IConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        }

        /// <summary>
        /// Checks the health of the RabbitMQ connection
        /// </summary>
        /// <param name="context">Health check context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Health check result</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var health = await _connectionProvider.GetHealthAsync(cancellationToken).ConfigureAwait(false);

                if (health == null)
                {
                    return HealthCheckResult.Unhealthy("Unable to retrieve connection health status");
                }

                var data = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "IsConnected", health.IsConnected },
                    { "Status", health.Status.ToString() },
                    { "LastCheckTime", health.LastCheckTime }
                };

                if (health.Uptime.HasValue)
                {
                    data.Add("Uptime", health.Uptime.Value.ToString());
                }

                if (health.Status == ConnectionHealthStatus.Healthy)
                {
                    return HealthCheckResult.Healthy(health.Details, data);
                }
                else if (health.Status == ConnectionHealthStatus.Degraded)
                {
                    return HealthCheckResult.Degraded(health.Details, data: data);
                }
                else if (health.Status == ConnectionHealthStatus.Disconnected)
                {
                    return HealthCheckResult.Unhealthy(health.Details, data: data);
                }
                else if (health.Status == ConnectionHealthStatus.Unhealthy)
                {
                    return HealthCheckResult.Unhealthy(health.Details, data: data);
                }
                else
                {
                    return HealthCheckResult.Unhealthy($"Unknown health status: {health.Status}", data: data);
                }
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Failed to check RabbitMQ connection health", ex);
            }
        }
    }
}
