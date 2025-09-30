using System;
using System.Threading.Tasks;
using ChatSchema;
using LazyMagic;

namespace ChatModule
{
    /// <summary>
    /// Stub implementation of HealthService for Phase 1 testing.
    /// This will be replaced with actual implementation in Phase 2.
    /// </summary>
    public class HealthService
    {
        public static async Task<HealthCheckResponse> CheckAsync(ICallerInfo callerInfo)
        {
            await Task.Delay(0);

            return new HealthCheckResponse
            {
                Status = HealthStatus.Healthy,
                Timestamp = DateTime.UtcNow,
                Version = "1.0.0-stub"
            };
        }
    }
}